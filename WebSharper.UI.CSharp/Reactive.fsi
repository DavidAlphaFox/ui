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
namespace WebSharper.UI

open System
open System.Threading.Tasks
open System.Runtime.CompilerServices
open WebSharper.UI

[<Extension; Class>]
type ViewExtensions =
    [<Extension>]
    static member Map : View<'A> * Func<'A, 'B> -> View<'B>

    [<Extension>]
    static member MapAsync : View<'A> * Func<'A, Task<'B>> -> View<'B>

    [<Extension>]
    static member Bind : View<'A> * Func<'A, View<'B>> -> View<'B>

    [<Extension>]
    static member MapCached<'A,'B when 'A : equality> : View<'A> * Func<'A, 'B> -> View<'B>

    [<Extension>]
    static member MapSeqCached<'A,'B when 'A : equality> :
        View<seq<'A>> * Func<'A, 'B> -> View<seq<'B>>

    [<Extension>]
    static member MapSeqCached<'A,'B when 'A : equality> :
        View<seq<'A>> * Func<View<'A>, 'B> -> View<seq<'B>>

    [<Extension>]
    static member MapSeqCached<'A,'B,'K when 'K : equality> :
        View<seq<'A>> * Func<'A, 'K> * Func<'A, 'B> -> View<seq<'B>>

    [<Extension>]
    static member MapSeqCached<'A,'B,'K when 'K : equality> :
        View<seq<'A>> * Func<'A, 'K> * Func<'K, View<'A>, 'B> -> View<seq<'B>>

    [<Extension>]
    static member Sink : View<'A> * Func<'A, unit> -> unit

    [<Extension>]
    static member Map2 : View<'A> * View<'B> * Func<'A, 'B, 'C> -> View<'C>

    [<Extension>]
    static member MapAsync2 : View<'A> * View<'B> * Func<'A, 'B, Task<'C>> -> View<'C>

    [<Extension>]
    static member Apply : View<Func<'A, 'B>> * View<'A> -> View<'B>

    [<Extension>]
    static member Join : View<View<'A>> -> View<'A>

    [<Extension>]
    static member Sequence : seq<View<'A>> -> View<seq<'A>>

    [<Extension>]
    static member SnapshotOn : View<'B> * 'B * View<'A> -> View<'B>

    [<Extension>]
    static member UpdateWhile : View<'A> * 'A * View<bool> -> View<'A>

[<Extension; Sealed>]
type VarExtension =
    [<Extension>]
    static member Update : Var<'A> * Func<'A, 'A> -> unit

    [<Extension>]
    static member Lens : Var<'A> * Func<'A, 'B> * Func<'A, 'B, 'A> -> Var<'B>

[<Extension; Sealed>]
type VarExtensions =
    
    [<Extension>]
    static member Update : Var<'A> * Func<'A, 'A> -> unit

[<Extension; Sealed>]
type DocExtension =
    /// Embeds time-varying fragments.
    /// Equivalent to Doc.BindView.
    [<Extension>]
    static member Doc : View<'T> * Func<'T, Doc> -> Doc

    /// Converts a collection to Doc using View.MapSeqCached and embeds the concatenated result.
    /// Shorthand for Doc.BindSeqCached.
    [<Extension>]
    static member DocSeqCached<'T when 'T : equality>
        : View<seq<'T>>
        * Func<'T, Doc>
        -> Doc

    /// DocSeqCached with a custom key.
    /// Shorthand for Doc.BindSeqCachedBy.
    [<Extension>]
    static member DocSeqCached<'T, 'K when 'K : equality>
        : View<seq<'T>>
        * Func<'T,'K>
        * Func<'T, Doc>
        -> Doc

    /// Converts a collection to Doc using View.MapSeqCachedView and embeds the concatenated result.
    /// Shorthand for Doc.BindSeqCachedView.
    [<Extension>]
    static member DocSeqCached<'T when 'T : equality>
        : View<seq<'T>>
        * Func<View<'T>, Doc>
        -> Doc

    /// DocSeqCached with a custom key.
    /// Shorthand for Doc.BindSeqCachedViewBy.
    [<Extension>]
    static member DocSeqCached<'T, 'K when 'K : equality>
        : View<seq<'T>>
        * Func<'T, 'K>
        * Func<'K, View<'T>, Doc>
        -> Doc

    [<Extension>]
    static member DocLens<'T, 'K when 'K : equality>
        : Var<list<'T>>
        * Func<'T, 'K>
        * Func<Var<'T>, Doc>
        -> Doc

    /// Converts a ListModel to Doc using MapSeqCachedBy and embeds the concatenated result.
    /// Shorthand for Doc.BindListModel.
    [<Extension>]
    static member Doc<'T, 'K when 'K : equality>
        : ListModel<'K, 'T>
        * Func<'T, Doc>
        -> Doc
    
    /// Converts a ListModel to Doc using MapSeqCachedViewBy and embeds the concatenated result.
    /// Shorthand for Doc.BindListModelView.
    [<Extension>]
    static member Doc<'T, 'K when 'K : equality>
        : ListModel<'K, 'T>
        * Func<'K, View<'T>, Doc>
        -> Doc

    /// Convert a ListModel's items to Doc and concatenate result.
    /// Shorthand for Doc.BindListModelLens
    [<Extension>]
    static member DocLens<'T, 'K when 'K : equality>
        : ListModel<'K, 'T>
        * Func<'K, Var<'T>, Doc>
        -> Doc