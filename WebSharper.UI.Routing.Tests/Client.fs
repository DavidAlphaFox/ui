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
namespace WebSharper.UI.Routing.Tests

open WebSharper
open WebSharper.JavaScript
open WebSharper.UI
open WebSharper.UI.Client
open WebSharper.UI.Html
open WebSharper.UI.Notation
open WebSharper.Sitelets
open WebSharper.UI.Routing
open Actions

[<JavaScript>]
module Client =

    let RouterTestBody clientOrServer test =
        match test with
        | RouterTestsHome ->
            let fSharpTests =
                RouterTestValues |> Seq.collect (fun test ->
                    [
                        div [] [ Link (clientOrServer (Inferred test)) (sprintf "F# inferred %A" test) ]
                        div [] [ Link (clientOrServer (Constructed test)) (sprintf "F# constructed %A" test) ]
                    ]
                )
            let cSharpTests =
                // WebSharper.UI.CSharp.Routing.Tests.Root.TestValues |> Seq.map (fun test ->
                //     div [] [ Link (clientOrServer (CSharpInferred test)) (sprintf "C# inferred %A" test) ]
                // )
                []
            Doc.Concat (Seq.append fSharpTests cSharpTests)
        | Inferred test ->
            Doc.Concat [
                div [] [ text (sprintf "%A" test) ]
                (match test with
                | Root -> div [] [ a [ attr.href "constructed/about/None/None" ] [ text "Plain relative URL to constructed About(null, null)" ] ]
                | _ -> Doc.Empty)
            ]
        | Constructed test ->
            div [] [ text (sprintf "%A" test) ]
        // | CSharpInferred test ->
        //     div [] [ text (sprintf "%A" test) ]

    let ClientSideRoutingPage () =
        let location = 
            router
            |> Router.Slice 
                (function ClientRouting r -> Some r | _ -> None)
                ClientRouting
            |> Router.Install RouterTestsHome 

        Doc.Concat [
            div [] [ text "Client-side routing tests" ]
            location.View.Doc(RouterTestBody ClientRouting)
            div [] [ Link (ClientRouting RouterTestsHome) "Back to client side tests root" ]
        ]
