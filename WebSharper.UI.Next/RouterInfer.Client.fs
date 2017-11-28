﻿// $begin{copyright}
//
// This file is part of WebSharper
//
// Copyright (c) 2008-2014 IntelliFactory
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

namespace WebSharper.UI.Next.Routing

open WebSharper
open WebSharper.Core
open WebSharper.Core.AST
open System.Collections.Generic
open WebSharper.UI.Next.Routing.RouterInferCommon

module M = WebSharper.Core.Metadata
module P = FSharp.Quotations.Patterns
module S = ServerRouting

[<AutoOpen>]
module private Internals =

    type MetadataAttributeReader() =
        inherit AttributeReader<TypeDefinition * M.ParameterObject[]>()
        override this.GetAssemblyName attr =
            (fst attr).Value.Assembly
        override this.GetName attr =
            let n = (fst attr).Value.FullName
            n.Substring(n.LastIndexOf('.') + 1)
        override this.GetCtorArgOpt attr = 
            (snd attr) |> Array.tryHead |> Option.map (M.ParameterObject.ToObj >> unbox<string>) 
        override this.GetCtorParamArgs attr = 
            match attr with
            | _, [| M.ParameterObject.Array a |] -> a |> Array.map (M.ParameterObject.ToObj >> unbox<string>)
            | _ -> [||]

    let attrReader = MetadataAttributeReader()
    
    let cString s = !~ (Literal.String s)
    let inline cInt i = !~ (Int i)

    let routerOpsModule =
        match <@ RouterOperators.rString @> with
        | P.PropertyGet(_, pi, _) -> Reflection.ReadTypeDefinition pi.DeclaringType
        | e -> 
            eprintfn "Reflection error in Warp.Internals, not a PropertyGet: %A" e
            Unchecked.defaultof<_>

    let getMethod expr =
        match expr with
        | P.Call(_, mi, _) -> Reflection.ReadMethod mi
        | P.PropertyGet(_, pi, _) -> Reflection.ReadMethod (pi.GetGetMethod())
        | _ ->              
            eprintfn "Reflection error in Warp.Internals, not a Call or PropertyGet: %A" expr
            Unchecked.defaultof<_>
    
    let rStringOp = getMethod <@ RouterOperators.rString @>
    let rCharOp = getMethod <@ RouterOperators.rChar @>
    let rGuidOp = getMethod <@ RouterOperators.rGuid @>
    let rBoolOp = getMethod <@ RouterOperators.rBool @>
    let rIntOp = getMethod <@ RouterOperators.rInt @>
    let rDoubleOp = getMethod <@ RouterOperators.rDouble @>
    let TupleOp = getMethod <@ RouterOperators.JSTuple [||] @>
    let ArrayOp = getMethod <@ RouterOperators.JSArray RouterOperators.rString @>
    let ListOp = getMethod <@ RouterOperators.JSList RouterOperators.rString @>
    let RecordOp = getMethod <@ RouterOperators.JSRecord null [||] @>
    let UnionOp = getMethod <@ RouterOperators.JSUnion null [||] @>
    let QueryOp = getMethod <@ RouterOperators.JSQuery null RouterOperators.rString @>
    let OptionOp = getMethod <@ RouterOperators.JSOption RouterOperators.rString @>
    let ClassOp = getMethod <@ RouterOperators.JSClass null [||] [||] @>
    let BoxOp = getMethod <@ RouterOperators.JSBox Unchecked.defaultof<_> @>
    
    let (|T|) (t: TypeDefinition) = t.Value.FullName
    let (|C|_|) (t: Type) =
        match t with 
        | ConcreteType { Entity = e; Generics = g} -> Some (e, g)
        | _ -> None

    let getAnnot attrsOpt =
        match attrsOpt with
        | Some attrs -> attrReader.GetAnnotation attrs
        | _ -> Annotation.Empty

type RoutingMacro() =
    inherit Macro()
    
    let mutable allJSClassesInitialized = false
    let allJSClasses = Dictionary()
    let parsedClassEndpoints = Dictionary()

    override this.TranslateCall(c) =
        match c.Method.Generics with
        | t :: _ -> 
            let comp = c.Compilation
            let top = comp.AssemblyName.Replace(".","$") + "_Router"
            let deps = HashSet()

            let recurringOn = HashSet()

            let rec getRouter t =
                let key = M.CompositeEntry [ M.StringEntry top; M.TypeEntry t ]
                match comp.GetMetadataEntries key with 
                | M.CompositeEntry [ M.TypeDefinitionEntry gtd; M.MethodEntry gm ] :: _ ->
                    Call(None, NonGeneric gtd, NonGeneric gm, [])
                | _ ->
                    let isTrivial, res = createRouter t
                    if isTrivial then res else
                    let gtd, gm, _ = comp.NewGenerated([top; "r"])
                    comp.AddGeneratedCode(gm, Lambda([], res))
                    comp.AddMetadataEntry(key, M.CompositeEntry [ M.TypeDefinitionEntry gtd; M.MethodEntry gm ])
                    Call(None, NonGeneric gtd, NonGeneric gm, [])
            
            and createRouter t =
                match t with
                | C (T "System.String", []) ->
                    true, Call(None, NonGeneric routerOpsModule, NonGeneric rStringOp, []) 
                | C (T "System.Char", []) ->
                    true, Call(None, NonGeneric routerOpsModule, NonGeneric rCharOp, []) 
                | C (T "System.Guid", []) ->
                    true, Call(None, NonGeneric routerOpsModule, NonGeneric rGuidOp, []) 
                | C (T "System.Boolean", []) ->
                    true, Call(None, NonGeneric routerOpsModule, NonGeneric rBoolOp, []) 
                | C (T "System.Int32", []) ->
                    true, Call(None, NonGeneric routerOpsModule, NonGeneric rIntOp, []) 
                | C (T "System.Double", []) ->
                    true, Call(None, NonGeneric routerOpsModule, NonGeneric rDoubleOp, []) 
                | TupleType (ts, _) ->
                    let fields = NewArray (ts |> List.map getRouter)
                    true, Call(None, NonGeneric routerOpsModule, NonGeneric TupleOp, [ fields ])    
                | ArrayType (t, 1) ->
                    true, Call(None, NonGeneric routerOpsModule, Generic ArrayOp [ t ], [ getRouter t ])    
                | C (T "Microsoft.FSharp.Collections.FSharpList`1", [ t ]) ->
                    true, Call(None, NonGeneric routerOpsModule, Generic ListOp [ t ], [ getRouter t ])    
                | C (T "Microsoft.FSharp.Core.FSharpOption`1", [ t ]) ->
                    true, Call(None, NonGeneric routerOpsModule, Generic OptionOp [ t ], [ getRouter t ])    
                | C (e, g) ->
                    if not (recurringOn.Add t) then
                        failwithf "Recursive types for Endpoint are currently not supported: %O" t
                    let getProto() =
                        match comp.GetClassInfo e with
                        | Some cls -> 
                            deps.Add (M.TypeNode e) |> ignore
                            if cls.HasWSPrototype then
                                GlobalAccess cls.Address.Value
                            else
                                Undefined
                        | _ -> Undefined

                    let mutable isTrivial = false

                    let res =
                        match comp.GetCustomTypeInfo e with
                        | M.EnumInfo t ->
                            isTrivial <- true
                            createRouter (NonGenericType t) |> snd
                        | M.FSharpRecordInfo r ->
                            let fields =
                                NewArray (
                                    r |> List.map (fun f ->
                                        NewArray [ cString f.JSName; getRouter (f.RecordFieldType.SubstituteGenerics(Array.ofList g)) ]
                                    )
                                )
                            Call(None, NonGeneric routerOpsModule, NonGeneric RecordOp, [ getProto(); fields ])    
                        | M.FSharpUnionInfo u ->
                            let someOf x = Object [ "$", cInt 1; "$0", x ]
                            let none = Value Null
                            let cases =
                                NewArray (
                                    u.Cases |> List.map (fun c ->
                                        let annot = comp.GetMethodAttributes(e, M.UnionCaseConstructMethod e c) |> getAnnot
                                        let path, isEmpty = 
                                            match annot.EndPoint with 
                                            | Some (m, e) -> 
                                                match S.ReadEndPointString e with
                                                | [||] -> NewArray [], true
                                                | s -> s |> Seq.map cString |> List.ofSeq |> NewArray, false
                                            | _ -> NewArray [ cString c.Name ], false 
                                        match c.Kind with
                                        | M.ConstantFSharpUnionCase v ->
                                            NewArray [ someOf (Value v); path ]
                                        | M.SingletonFSharpUnionCase ->
                                            NewArray [ none; path; NewArray [] ]
                                        | M.NormalFSharpUnionCase fields ->
                                            if isEmpty && not (List.isEmpty fields) then
                                                failwithf "Union case %s.%s with root EndPoint cannot have any fields" e.Value.FullName c.Name
                                            let queryFields = annot.Query |> Option.map HashSet
                                            let fRouters = 
                                                fields |> List.map (fun f ->
                                                    let r = getRouter (f.UnionFieldType.SubstituteGenerics(Array.ofList g))
                                                    if queryFields|> Option.exists (fun q -> q.Remove f.Name) then
                                                        Call(None, NonGeneric routerOpsModule, NonGeneric QueryOp, [ cString f.Name; r ])
                                                    else r
                                                )
                                            if queryFields |> Option.exists (fun q -> q.Count > 0) then
                                                failwithf "Union case field specified by Query attribute not found: %s" (Seq.head queryFields.Value)
                                            NewArray [ none; path; NewArray fRouters ]
                                    )
                                )

                            Call(None, NonGeneric routerOpsModule, NonGeneric UnionOp, [ getProto(); cases ])    
                        | M.FSharpUnionCaseInfo _ ->
                            failwithf "Failed to create Router for type %O, F# union case types are not supported yet" t
                        | M.DelegateInfo _ ->
                            failwithf "Failed to create Router for type %O, delegate types are not supported" t
                        | M.StructInfo
                        | M.NotCustomType ->
                            if not allJSClassesInitialized then
                                for td in comp.GetJavaScriptClasses() do
                                    match comp.GetClassInfo(td) with
                                    | Some cls ->
                                        allJSClasses.Add(td, cls)
                                    | _ -> ()
                                allJSClassesInitialized <- true
                            match allJSClasses.TryFind e with
                            | Some cls -> 
                                let rec getClassAnnotation td =
                                    match parsedClassEndpoints.TryFind(td) with
                                    | Some ep -> ep
                                    | None ->
                                        let b = 
                                            match cls.BaseClass with
                                            | None -> Annotation.Empty
                                            | Some bc when bc = Definitions.Object -> Annotation.Empty
                                            | Some bc -> getClassAnnotation bc
                                        let annot = comp.GetTypeAttributes(e) |> getAnnot |> Annotation.Combine b
                                        parsedClassEndpoints.Add(td, annot)
                                        annot
                                let annot = getClassAnnotation e 
                                let endpoint = 
                                    match annot.EndPoint with
                                    | Some (_, e) -> e |> Path.FromUrl |> S.GetPathHoles |> fst
                                    | None -> [||]
                                let subClasses =
                                    let nestedIn = e.Value.FullName + "+"
                                    allJSClasses |> Seq.choose (fun (KeyValue(td, cls)) ->
                                        if td.Value.FullName.StartsWith nestedIn then
                                            match cls.BaseClass with
                                            | Some bc when bc = e -> Some td
                                            | _ -> None
                                        else None
                                    ) |> List.ofSeq
                                let choice i x = Object [ "$", cInt i; "$0", x ] 
                                //match cls.Constructors.TryFind(ConstructorInfo.Default()) with
                                //| Some ctor ->
                                //| None -> failwithf "Failed to create Router for type %O, it does not have a parameterless constructor" t
                                
                                let rec findField f (cls: M.IClassInfo) =
                                    match cls.Fields.TryFind f with
                                    | Some fi -> Some fi
                                    | _ ->
                                        match cls.BaseClass with
                                        | None -> None
                                        | Some bc when bc = Definitions.Object -> None
                                        | Some bc -> findField f allJSClasses.[bc]

                                let partsAndFields =
                                    NewArray (
                                        endpoint |> Seq.map (function
                                            | S.StringSegment s -> choice 0 (cString s)
                                            | S.FieldSegment f ->
                                                match findField f cls with
                                                | Some (f, _, fTyp) ->
                                                    match f with
                                                    | M.InstanceField n ->
                                                        choice 1 (NewArray [ cString n; getRouter (fTyp.SubstituteGenerics(Array.ofList g)) ])
                                                    | M.IndexedField i ->
                                                        choice 1 (NewArray [ cInt i; getRouter (fTyp.SubstituteGenerics(Array.ofList g)) ])
                                                    | M.OptionalField n ->
                                                        choice 1 (NewArray [ cString n; getRouter (fTyp.SubstituteGenerics(Array.ofList g)) ]) // todo optional
                                                    | M.StaticField _ ->
                                                        failwith "Static field cannot be encoded to URL path"
                                                | _ ->
                                                    failwithf "Could not find field %s" f
                                        )
                                        |> List.ofSeq
                                    )
                                let subClassRouters =
                                    NewArray (
                                        subClasses |> List.map (fun sc -> getRouter (GenericType sc g))
                                    )
                                let unboxed = Call(None, NonGeneric routerOpsModule, NonGeneric ClassOp, [ getProto(); partsAndFields; subClassRouters ]) // todo use ctor instead of getProto   
                                Call(None, NonGeneric routerOpsModule, Generic BoxOp [ t ], [ unboxed ])
                            | _ -> failwithf "Failed to create Router for type %O, it does not have the JavaScript attribute" t
                        
                    recurringOn.Remove t |> ignore
                    isTrivial, res
                | _ -> failwithf "Failed to create Router for type %O, invalid shape" t
                
            let res = MacroOk <| getRouter t
            if deps.Count > 0 then
                WebSharper.Core.MacroDependencies (List.ofSeq deps, res)
            else res    

        | _ -> MacroError "Expecting a type argument for RoutingMacro"

[<AutoOpen>]
module InferRouter =

    [<Macro(typeof<RoutingMacro>)>]
    module Router =
        /// Creates a router based on type shape and WebSharper attributes Endpoint and Query.
        let Infer<'T> = S.getRouter typeof<'T> |> Router.UnboxUnsafe<'T>