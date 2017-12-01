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

module internal WebSharper.UI.Routing.RouterInferCommon

open System
open System.Collections.Generic
open WebSharper
open WebSharper.Core

module M = WebSharper.Core.Metadata
module P = FSharp.Quotations.Patterns

type Annotation =
    {
        EndPoint : option<option<string> * string>
        Query : option<Set<string>>
        Json : option<option<string>>
        FormData : option<Set<string>>
        IsWildcard : bool
    }

module Annotation =
    let Empty = 
        {
            EndPoint = None
            Query = None
            Json = None
            FormData = None
            IsWildcard = false
        }

    let Combine a b =
        let comb f a b =
            match a, b with
            | Some a, Some b -> Some (f a b)
            | Some _, _ -> a
            | _, Some _ -> b
            | _ -> None
        let pcomb a b =
            if String.IsNullOrEmpty a || a = "/" then b
            elif String.IsNullOrEmpty b || b = "/" then a
            elif b.StartsWith "/" then a + b else a + "/" + b
        {
            EndPoint = comb (fun (_, ae) (_, be) -> None, pcomb ae be) a.EndPoint b.EndPoint // todo combine methods
            Query = comb Set.union a.Query b.Query
            Json = comb (fun a b -> comb (fun _ _ -> failwith "multiple json fields") a b) a.Json b.Json 
            FormData = comb Set.union a.FormData b.FormData 
            IsWildcard = b.IsWildcard
        }
    
[<AbstractClass>]
type AttributeReader<'A>() =

    abstract GetAssemblyName : 'A -> string
    abstract GetName : 'A -> string
    abstract GetCtorArgOpt : 'A -> string option
    abstract GetCtorParamArgs : 'A -> string[]

    member this.GetAnnotation(attrs: seq<'A>) =
        let ep = ResizeArray()
        let ms = ResizeArray()
        let q = ref None
        let fd = ref None
        let mutable j = None
        let mutable w = false
        let addToSet s attr =
            let set =
                match !s with
                | Some set -> set
                | None ->
                    let set = HashSet()
                    s := Some set
                    set
            this.GetCtorParamArgs(attr) |> Array.iter (set.Add >> ignore)
        for attr in attrs do
            if this.GetAssemblyName attr = "WebSharper.Core" then
                match this.GetName attr with
                | "EndPointAttribute" ->
                    this.GetCtorArgOpt(attr) |> Option.iter ep.Add
                | "MethodAttribute" ->
                    this.GetCtorParamArgs(attr) |> Array.iter ms.Add
                | "QueryAttribute" ->
                    addToSet q attr
                | "JsonAttribute" ->
                    j <- this.GetCtorArgOpt(attr) |> Some
                | "FormDataAttribute" ->
                    addToSet fd attr
                | "WildcardAttribute" ->
                    w <- true
                | _ -> ()
        let endpoints =
            match ep.Count, ms.Count with
            | 0, 0 -> []
            | 0, _ -> ms |> Seq.map (fun m -> Some m, "") |> List.ofSeq
            | _, 0 -> ep |> Seq.map (fun e -> None, e) |> List.ofSeq
            | _ ->
                [
                    for e in ep do
                        for m in ms ->
                            Some m, e
                ]
        {
            EndPoint = endpoints |> List.tryHead
            Query = !q |> Option.map Set.ofSeq
            Json = j
            FormData = !fd |> Option.map Set.ofSeq
            IsWildcard = w
        }


                        

