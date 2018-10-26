// $begin{copyright}
//
// This file is part of WebSharper
//
// Copyright (c) 2008-2018 IntelliFactory
//
// Licensed under the Apache License, Version 2.0 (the "License"); you
// may not use this file except in compliance with the License.  You may
// obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
// implied.  See the License for the specific language governing
// permissions and limitations under the License.
//
// $end{copyright}

module WebSharper.UI.Templating.Runtime.Server

open System
open System.IO
open System.Collections.Generic
open FSharp.Quotations
open WebSharper
open WebSharper.Core.Resources
open WebSharper.Web
open WebSharper.UI
open WebSharper.UI.Server
open WebSharper.UI.Templating
open WebSharper.UI.Templating.AST
open WebSharper.Sitelets
open WebSharper.Sitelets.Content
open System.Collections.Concurrent

module M = WebSharper.Core.Metadata
module J = WebSharper.Core.Json
module P = FSharp.Quotations.Patterns
type private Holes = Dictionary<HoleName, TemplateHole>
type private DomElement = WebSharper.JavaScript.Dom.Element
type private DomEvent = WebSharper.JavaScript.Dom.Event

type ValTy =
    | String = 0
    | Number = 1
    | Bool = 2

[<JavaScript; Serializable>]
type TemplateInitializer(id: string, vars: array<string * ValTy>) =

    member this.Instance =
        if JavaScript.JS.HasOwnProperty this "instance" then
            JavaScript.JS.Get "instance" this : TemplateInstance
        else
            let d = Dictionary()
            for n, t in vars do
                d.[n] <-
                    match t with
                    | ValTy.Bool -> box (Var.Create false)
                    | ValTy.Number -> box (Var.Create 0.)
                    | ValTy.String -> box (Var.Create "")
                    | _ -> failwith "Invalid value type"
            let i = TemplateInstance(CompletedHoles.Client(d), Doc.Empty)
            JavaScript.JS.Set this "instance" i
            i

    // Members unused, but necessary to force `id` and `vars` to be fields
    // (and not just ctor arguments)
    member this.Id = id
    member this.Vars = vars

    interface IRequiresResources with
        [<JavaScript false>]
        member this.Requires(meta) =
            [| M.TypeNode(Core.AST.Reflection.ReadTypeDefinition(typeof<TemplateInitializer>)) |] :> _
        [<JavaScript false>]
        member this.Encode(meta, json) =
            [id, json.GetEncoder<TemplateInitializer>().Encode(this)]

and [<JavaScript>] TemplateInstances() =
    [<JavaScript>]
    static member GetInstance key =
        let i = JavaScript.JS.Get key WebSharper.Activator.Instances : TemplateInitializer
        i.Instance

and CompletedHoles =
    | Client of Dictionary<string, obj>
    | Server of TemplateInitializer

and TemplateInstance(c: CompletedHoles, doc: Doc) =
    
    member this.Doc = doc

    member this.Hole(name: string): obj = failwith "Cannot access template vars from the server side"

type TemplateEvent<'TI, 'E when 'E :> DomEvent> =
    {
        /// The reactive variables of this template instance.
        Vars : 'TI
        /// The DOM element targeted by this event.
        Target : DomElement
        /// The DOM event data.
        Event : 'E
    }

type Handler private () =

    static member EventQ (holeName: string, isGenerated: bool, [<JavaScript>] f: Expr<DomElement -> DomEvent -> unit>) =
        TemplateHole.EventQ(holeName, isGenerated, f)

    static member EventQ2<'E when 'E :> DomEvent> (key: string, holeName: string, ti: (unit -> TemplateInstance), [<JavaScript>] f: Expr<TemplateEvent<obj, 'E> -> unit>) =
        Handler.EventQ(holeName, true, <@ fun el ev ->
            let k = key
            (WebSharper.JavaScript.Pervasives.As<TemplateEvent<obj, 'E> -> unit> f)
                {
                    Vars = box (TemplateInstances.GetInstance k)
                    Target = el
                    Event = ev :?> 'E
                }
        @>)

    static member CompleteHoles(key: string, filledHoles: seq<TemplateHole>, vars: array<string * ValTy>) : seq<TemplateHole> * CompletedHoles =
        let filledVars = HashSet()
        for h in filledHoles do
            match h with
            | TemplateHole.VarStr(n, _)
            | TemplateHole.VarIntUnchecked(n, _)
            | TemplateHole.VarInt(n, _)
            | TemplateHole.VarFloatUnchecked(n, _)
            | TemplateHole.VarFloat(n, _)
            | TemplateHole.VarBool(n, _) ->
                filledVars.Add n |> ignore
            | _ -> ()
        let strHole s =
            Handler.EventQ(s, true,
                <@  (fun key s el ev ->
                        (TemplateInstances.GetInstance(key).Hole(s) :?> Var<string>).Value <- JavaScript.JS.Get "value" el
                    ) key s @>)
        let extraHoles =
            vars |> Array.choose (fun (name, ty) ->
                if filledVars.Contains name then None else
                let h =
                    match ty with
                    | ValTy.String -> strHole name
                    //| ValTy.Number ->
                    //    let r = Var.Create 0.
                    //    TemplateHole.VarFloatUnchecked (name, r), box r
                    //| ValTy.Bool ->
                    //    let r = Var.Create false
                    //    TemplateHole.VarBool (name, r), box r
                    | _ -> failwith "Invalid value type"
                Some h
            )
        let holes =
            filledHoles
            |> Seq.map (function
                | TemplateHole.VarStr(s, _) -> strHole s
                | x -> x
            )
            |> Seq.append extraHoles
            |> Seq.cache
        holes, Server (new TemplateInitializer(id = key, vars = vars))

type private RenderContext =
    {
        Context : Web.Context
        Writer : HtmlTextWriter
        Resources: option<RenderedResources>
        Templates: Map<Parsing.WrappedTemplateName, Template>
        FillWith: Holes
        RequireResources: Dictionary<string, IRequiresResources>
    }

[<JavaScript>]
type ProviderBuilder =
    {
        [<Name "i">] mutable Instance: TemplateInstance
        [<Name "k">] Key: string
        [<Name "h">] Holes: ResizeArray<TemplateHole>
        [<OptionalField; Name "s">] Source: option<string>
    }

    member this.WithHole(h) =
        this.Holes.Add(h)
        this

    [<Inline>]
    member this.SetInstance(i) =
        this.Instance <- i
        i

    static member Make() =
        {
            Instance = Unchecked.defaultof<TemplateInstance>
            Key = Guid.NewGuid().ToString()
            Holes = ResizeArray()
            Source = None
        }

    static member Make(src) =
        {
            Instance = Unchecked.defaultof<TemplateInstance>
            Key = Guid.NewGuid().ToString()
            Holes = ResizeArray()
            Source = Some src
        }

type Runtime private () =

    static let loaded = ConcurrentDictionary<string, Map<Parsing.WrappedTemplateName, Template>>()

    static let watchers = ConcurrentDictionary<string, FileSystemWatcher>()

    static let toInitialize = ConcurrentQueue<Web.Context -> unit>()

    static let reloader = MailboxProcessor.Start(fun mb -> async {
        while true do
            let! baseName, fullPath as msg = mb.Receive()
            try Some (File.ReadAllText fullPath)
            with _ ->
                async {
                    do! Async.Sleep(1000)
                    mb.Post(msg)
                }
                |> Async.StartImmediate
                None
            |> Option.iter (fun src ->
                let parsed, _, _ = Parsing.ParseSource baseName src
                loaded.AddOrUpdate(baseName, parsed, fun _ _ -> parsed)
                |> ignore
            )
    })

    static let getTemplate baseName name (templates: IDictionary<_,_>) : Template =
        match templates.TryGetValue name with
        | false, _ -> failwithf "Template not defined: %s/%A" baseName name
        | true, template -> template

    static let buildFillDict fillWith (holes: IDictionary<HoleName, HoleDefinition>) =
        let d : Holes = Dictionary(StringComparer.InvariantCultureIgnoreCase)
        for f in fillWith do
            let name = TemplateHole.Name f
            if holes.ContainsKey name then d.[name] <- f
        d

    /// Different nodes need to be wrapped in different container to be handled properly.
    /// See http://krasimirtsonev.com/blog/article/Revealing-the-magic-how-to-properly-convert-HTML-string-to-a-DOM-element
    static let templateWrappers =
        Map [
            Some "option", ("""<select multiple="multiple" style="display:none" {0}="{1}">""", "</select>")
            Some "legend", ("""<fieldset style="display:none" {0}="{1}">""", "</fieldset>")
            Some "area", ("""<map style="display:none" {0}="{1}">""", "</map>")
            Some "param", ("""<object style="display:none" {0}="{1}">""", "</object>")
            Some "thead", ("""<table style="display:none" {0}="{1}">""", "</table>")
            Some "tbody", ("""<table style="display:none" {0}="{1}">""", "</table>")
            Some "tfoot", ("""<table style="display:none" {0}="{1}">""", "</table>")
            Some "tr", ("""<table style="display:none"><tbody {0}="{1}">""", """</tbody></table>""")
            Some "col", ("""<table style="display:none"><tbody></tbody><colgroup {0}="{1}">""", """</colgroup></table>""")
            Some "td", ("""<table style="display:none"><tbody><tr {0}="{1}">""", """</tr></tbody></table>""")
        ]
    static let defaultTemplateWrappers = ("""<div style="display:none" {0}="{1}">""", "</div>")

    static let holeTagName parent =
        match parent with
        | Some "select" -> "option"
        | Some "fieldset" -> "legend"
        | Some "map" -> "area"
        | Some "object" -> "param"
        | Some "table" -> "tbody"
        | Some "tbody" -> "tr"
        | Some "colgroup" -> "col"
        | Some "tr" -> "td"
        | _ -> "div"

    static member RunTemplate (fillWith: seq<TemplateHole>): Doc =
        failwith "Template.Bind() can only be called from the client side."

    static member GetOrLoadTemplate
            (
                baseName: string,
                name: option<string>,
                path: option<string>,
                origSrc: string,
                dynSrc: option<string>,
                fillWith: seq<TemplateHole>,
                inlineBaseName: option<string>,
                serverLoad: ServerLoad,
                refs: array<string * option<string> * string>,
                completed: CompletedHoles,
                isElt: bool,
                keepUnfilled: bool,
                serverOnly: bool
            ) : Doc =
        let getSrc src =
            let t, _, _ = Parsing.ParseSource baseName src in t
        let getOrLoadSrc src =
            loaded.GetOrAdd(baseName, fun _ -> getSrc src)
        let getOrLoadPath fullPath =
            loaded.GetOrAdd(baseName, fun _ -> let t, _, _ = Parsing.ParseSource baseName (File.ReadAllText fullPath) in t)
        let requireResources = Dictionary(StringComparer.InvariantCultureIgnoreCase)
        let addTemplateHole (dict: Dictionary<_,_>) x =
            match x with
            | TemplateHole.Elt (n, d) when not (obj.ReferenceEquals(d, null)) ->
                dict.Add(n, d :> IRequiresResources)
            | TemplateHole.Attribute (n, a) when not (obj.ReferenceEquals(a, null)) ->
                dict.Add(n, a :> IRequiresResources)
            | TemplateHole.EventQ (n, _, e) ->
                dict.Add(n, Attr.HandlerImpl("", e) :> IRequiresResources)
            | TemplateHole.AfterRenderQ (n, e) ->
                dict.Add(n, Attr.OnAfterRenderImpl(e) :> IRequiresResources)
            | _ -> ()
        
        fillWith |> Seq.iter (addTemplateHole requireResources)

        let rec writeWrappedTemplate templateName (template: Template) ctx =
            let tagName = template.Value |> Array.tryPick (function
                | Node.Element (name, _, _, _)
                | Node.Input (name, _, _, _) -> Some name
                | Node.Text _ | Node.DocHole _ | Node.Instantiate _ -> None
            )
            let before, after = defaultArg (Map.tryFind tagName templateWrappers) defaultTemplateWrappers
            ctx.Writer.Write(before, ChildrenTemplateAttr, templateName)
            writeTemplate template true [] ctx
            ctx.Writer.Write(after)

        and writeTemplate (template: Template) (plain: bool) (extraAttrs: list<UI.Attr>) (ctx: RenderContext) =
            let writeStringParts text (w: HtmlTextWriter) =
                text |> Array.iter (function
                    | StringPart.Text t -> w.Write(t)
                    | StringPart.Hole holeName ->
                        let doPlain() = w.Write("${" + holeName + "}")
                        if plain then doPlain() else
                        match ctx.FillWith.TryGetValue holeName with
                        | true, TemplateHole.Text (_, t) -> w.WriteEncodedText(t)
                        | true, _ -> failwithf "Invalid hole, expected text: %s" holeName
                        | false, _ -> if keepUnfilled then doPlain()
                )
            let unencodedStringParts text =
                text
                |> Array.map (function
                    | StringPart.Text t -> t
                    | StringPart.Hole holeName ->
                        let doPlain() = "${" + holeName + "}"
                        if plain then doPlain() else
                        match ctx.FillWith.TryGetValue holeName with
                        | true, TemplateHole.Text (_, t) -> t
                        | true, _ -> failwithf "Invalid hole, expected text: %s" holeName
                        | false, _ -> if keepUnfilled then doPlain() else ""
                )
                |> String.concat ""
            let writeAttr plain = function
                | Attr.Attr holeName ->
                    let doPlain() = ctx.Writer.WriteAttribute(AttrAttr, holeName)
                    if plain then doPlain() else
                    match ctx.RequireResources.TryGetValue holeName with
                    | true, (:? UI.Attr as a) ->
                        a.Write(ctx.Context.Metadata, ctx.Writer, true)
                    | _ ->
                        if ctx.FillWith.ContainsKey holeName then
                            failwithf "Invalid hole, expected attribute: %s" holeName
                        elif keepUnfilled then doPlain()
                | Attr.Simple(name, value) ->
                    ctx.Writer.WriteAttribute(name, value)
                | Attr.Compound(name, value) ->
                    ctx.Writer.WriteAttribute(name, unencodedStringParts value)
                | Attr.Event(event, holeName) ->
                    let doPlain() = ctx.Writer.WriteAttribute(EventAttrPrefix + event, holeName)
                    if plain then doPlain() else
                    match ctx.RequireResources.TryGetValue holeName with
                    | true, (:? UI.Attr as a) ->
                        a.WithName("on" + event).Write(ctx.Context.Metadata, ctx.Writer, true)
                    | _ ->
                        if ctx.FillWith.ContainsKey holeName then
                            failwithf "Invalid hole, expected quoted event: %s" holeName
                        elif keepUnfilled then doPlain()
                | Attr.OnAfterRender holeName ->
                    let doPlain() = ctx.Writer.WriteAttribute(AfterRenderAttr, holeName)
                    if plain then doPlain() else
                    match ctx.RequireResources.TryGetValue holeName with
                    | true, (:? UI.Attr as a) ->
                        a.Write(ctx.Context.Metadata, ctx.Writer, true)
                    | _ ->
                        if ctx.FillWith.ContainsKey holeName then
                            failwithf "Invalid hole, expected onafterrender: %s" holeName
                        elif keepUnfilled then doPlain()
            let rec writeElement isRoot plain tag attrs wsVar children =
                ctx.Writer.WriteBeginTag(tag)
                attrs |> Array.iter (writeAttr plain)
                if isRoot then
                    extraAttrs |> List.iter (fun a -> a.Write(ctx.Context.Metadata, ctx.Writer, true))
                wsVar |> Option.iter (fun v -> ctx.Writer.WriteAttribute("ws-var", v))
                if Array.isEmpty children && HtmlTextWriter.IsSelfClosingTag tag then
                    ctx.Writer.Write(HtmlTextWriter.SelfClosingTagEnd)
                else
                    ctx.Writer.Write(HtmlTextWriter.TagRightChar)
                    Array.iter (writeNode (Some tag) plain) children
                    if tag = "body" && Option.isNone name && Option.isSome inlineBaseName then
                        ctx.Templates |> Seq.iter (fun (KeyValue(k, v)) ->
                            match k.NameAsOption with
                            | Some templateName -> writeWrappedTemplate templateName v ctx
                            | None -> ()
                        )
                    ctx.Writer.WriteEndTag(tag)
            and writeNode parent plain = function
                | Node.Element (tag, _, attrs, children) ->
                    writeElement (Option.isNone parent) plain tag attrs None children
                | Node.Input (tag, holeName, attrs, children) ->
                    let doPlain() = writeElement (Option.isNone parent) plain tag attrs (Some holeName) children
                    if plain then doPlain() else
                    let wsVar, attrs =
                        match ctx.FillWith.TryGetValue holeName with
                        | true, TemplateHole.EventQ (_, _, _) ->
                            None, Array.append [|Attr.Event("input", holeName)|] attrs
                        | _ ->
                            Some holeName, attrs
                    writeElement (Option.isNone parent) plain tag attrs wsVar children
                | Node.Text text ->
                    writeStringParts text ctx.Writer
                | Node.DocHole holeName ->
                    let doPlain() =
                        let tagName = holeTagName parent
                        ctx.Writer.WriteBeginTag(tagName)
                        ctx.Writer.WriteAttribute(ReplaceAttr, holeName)
                        ctx.Writer.Write(HtmlTextWriter.TagRightChar)
                        ctx.Writer.WriteEndTag(tagName)
                    if plain then doPlain() else
                    match holeName with
                    | "scripts" | "styles" | "meta" when Option.isSome ctx.Resources ->
                        ctx.Writer.Write(ctx.Resources.Value.[holeName])
                    | _ ->
                        match ctx.RequireResources.TryGetValue holeName with
                        | true, (:? UI.Doc as doc) ->
                            doc.Write(ctx.Context, ctx.Writer, ctx.Resources)
                        | _ ->
                            match ctx.FillWith.TryGetValue holeName with
                            | true, TemplateHole.Text (_, txt) -> ctx.Writer.WriteEncodedText(txt)
                            | true, _ -> failwithf "Invalid hole, expected Doc: %s" holeName
                            | false, _ -> if keepUnfilled then doPlain()
                | Node.Instantiate (fileName, templateName, holeMaps, attrHoles, contentHoles, textHole) ->
                    if plain then
                        writePlainInstantiation fileName templateName holeMaps attrHoles contentHoles textHole
                    else
                        writeInstantiation parent fileName templateName holeMaps attrHoles contentHoles textHole
            and writeInstantiation parent fileName templateName holeMaps attrHoles contentHoles textHole =
                let attrFromInstantiation (a: Attr) =
                    match a with
                    | Attr.Attr holeName -> 
                        match ctx.FillWith.TryGetValue holeName with
                        | true, TemplateHole.Attribute (_, a) -> a
                        | true, _ -> failwithf "Invalid hole, expected Attr: %s" holeName
                        | false, _ -> Attr.Empty
                    | Attr.Simple(name, value) ->
                        Attr.Create name value
                    | Attr.Compound(name, value) ->
                        Attr.Create name (unencodedStringParts value)
                    | Attr.Event(event, holeName) ->
                        match ctx.RequireResources.TryGetValue holeName with
                        | true, (:? UI.Attr as a) ->
                            a.WithName("on" + event)
                        | _ ->
                            if ctx.FillWith.ContainsKey holeName then
                                failwithf "Invalid hole, expected quoted event: %s" holeName
                            else Attr.Empty
                    | Attr.OnAfterRender holeName ->
                        match ctx.RequireResources.TryGetValue holeName with
                        | true, (:? UI.Attr as a) ->
                            a
                        | _ ->
                            if ctx.FillWith.ContainsKey holeName then
                                failwithf "Invalid hole, expected onafterrender: %s" holeName
                            else Attr.Empty
                let holes = Dictionary(StringComparer.InvariantCultureIgnoreCase)
                let reqRes = Dictionary(StringComparer.InvariantCultureIgnoreCase)
                for KeyValue(k, v) in holeMaps do
                    let mapped = ctx.FillWith.[v].WithName k
                    holes.Add(k, mapped)
                    mapped |> addTemplateHole reqRes
                for KeyValue(k, v) in attrHoles do
                    let attr = TemplateHole.Attribute(k, Attr.Concat (v |> Seq.map attrFromInstantiation))
                    holes.Add(k, attr)
                    attr |> addTemplateHole reqRes
                for KeyValue(k, v) in contentHoles do
                    match v with
                    | [| Node.Text text |] ->
                        holes.Add(k, TemplateHole.Text(k, unencodedStringParts text))
                    | _ ->
                        let writeContent ctx w r =
                            v |> Array.iter (writeNode parent false)
                        let doc = TemplateHole.Elt(k, Server.Internal.TemplateDoc([], writeContent))
                        holes.Add(k, doc)
                        doc |> addTemplateHole reqRes
                let templates =
                    match fileName with
                    | None -> ctx.Templates
                    | Some fileName ->
                        match loaded.TryGetValue fileName with
                        | true, templates -> templates
                        | false, _ -> failwithf "Template file reference has not been loaded: %s" fileName
                match templates |> Map.tryFind (Parsing.WrappedTemplateName.OfOption templateName) with
                | Some instTemplate ->
                    writeTemplate instTemplate false []
                        { ctx with 
                            Templates = templates
                            RequireResources = reqRes
                            FillWith = holes
                        }
                | None ->
                    let fullName = 
                        Option.toList fileName @ Option.toList templateName |> String.concat "." 
                    failwithf "Sub-template not found: %s" fullName
            and writePlainInstantiation fileName templateName holeMaps attrHoles contentHoles textHole =
                let tagName =
                    let filePrefix =
                        match fileName with
                        | None -> ""
                        | Some p -> p + "."
                    "ws-" + filePrefix + defaultArg templateName ""
                ctx.Writer.WriteBeginTag(tagName)
                for KeyValue(k, v) in holeMaps do
                    ctx.Writer.WriteAttribute(k, v)
                ctx.Writer.Write(HtmlTextWriter.TagRightChar)
                for KeyValue(k, v) in attrHoles do
                    writeElement false true k v None [||]
                for KeyValue(k, v) in contentHoles do
                    writeElement false true k [||] None v
                textHole |> Option.iter ctx.Writer.WriteEncodedText
                ctx.Writer.WriteEndTag(tagName)
            Array.iter (writeNode None plain) template.Value
        let templates = ref None
        let getTemplates (ctx: Web.Context) =
            let t =
                match dynSrc, !templates with
                | Some dynSrc, _ -> getSrc dynSrc
                | None, Some t -> t
                | None, None ->
                let t =
                    match path, serverLoad with
                    | None, _
                    | Some _, ServerLoad.Once ->
                        getOrLoadSrc origSrc
                    | Some path, ServerLoad.PerRequest ->
                        let fullPath = Path.Combine(ctx.RootFolder, path)
                        getSrc (File.ReadAllText fullPath)
                    | Some path, ServerLoad.WhenChanged ->
                        let fullPath = Path.Combine(ctx.RootFolder, path)
                        let watcher = watchers.GetOrAdd(baseName, fun _ ->
                            let dir = Path.GetDirectoryName fullPath
                            let file = Path.GetFileName fullPath
                            let watcher =
                                new FileSystemWatcher(
                                    Path = dir,
                                    Filter = file,
                                    NotifyFilter = (NotifyFilters.LastWrite ||| NotifyFilters.Security ||| NotifyFilters.FileName),
                                    EnableRaisingEvents = true)
                            let handler _ =
                                reloader.Post(baseName, fullPath)
                            watcher.Changed.Add handler
                            watcher)
                        getOrLoadPath fullPath
                    | Some _, _ -> failwith "Invalid ServerLoad"
                templates := Some t
                t
            getTemplate baseName (Parsing.WrappedTemplateName.OfOption name) t, t
        let tplInstance =
            if obj.ReferenceEquals(completed, null) then
                Seq.empty
            else
                match completed with
                | CompletedHoles.Server i -> Seq.singleton (i :> IRequiresResources)
                | CompletedHoles.Client _ -> failwith "Shouldn't happen"
        let requireResourcesSeq = Seq.append tplInstance requireResources.Values
        let write extraAttrs ctx w r =
            let rec runInits() = 
                match toInitialize.TryDequeue() with
                | true, init ->
                    init ctx
                    runInits ()
                | _ -> ()
            runInits()
            let template, templates = getTemplates ctx
            let r =
                if r then
                    SpecialHole.RenderResources template.SpecialHoles ctx
                        (Seq.append requireResourcesSeq (Seq.cast extraAttrs))
                    |> Some
                else None
            let fillWith = buildFillDict fillWith template.Holes
            writeTemplate template false extraAttrs {
                Context = ctx
                Writer = w
                Resources = r
                Templates = templates
                FillWith = fillWith
                RequireResources = requireResources
            }
        if not (loaded.ContainsKey baseName) then
            toInitialize.Enqueue(fun ctx ->
                if not (loaded.ContainsKey baseName) then
                    getTemplates ctx |> ignore
            )
        if isElt then
            Server.Internal.TemplateElt(requireResourcesSeq, write) :> _
        else
            Server.Internal.TemplateDoc(requireResourcesSeq, write []) :> _
