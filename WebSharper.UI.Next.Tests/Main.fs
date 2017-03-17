namespace WebSharper.UI.Next.Tests

open WebSharper
open WebSharper.JavaScript
open WebSharper.Testing
open WebSharper.UI.Next
open WebSharper.UI.Next.Html
open WebSharper.UI.Next.Client

[<JavaScript>]
module Main =

    let TestAnim =
        let linearAnim = Anim.Simple Interpolation.Double (Easing.Custom id) 300.
        let cubicAnim = Anim.Simple Interpolation.Double Easing.CubicInOut 300.
        let swipeTransition =
            Trans.Create linearAnim
            |> Trans.Enter (fun x -> cubicAnim (x - 100.) x)
            |> Trans.Exit (fun x -> cubicAnim x (x + 100.))
        let rvLeftPos = Var.Create 0.
        
        divAttr [
            Attr.Style "position" "relative"
            Attr.AnimatedStyle "left" swipeTransition rvLeftPos.View (fun pos -> string pos + "%")
        ] [
            Doc.TextNode "content"
        ]

    let VarTest =
        TestCategory "Var" {
       
            Test "Create" {
                let rv1 = Var.Create 1
                let rv2 = Var.Create "a"
                expect 0
            }

            Test "Value" {
                let rv = Var.Create 2
                equalMsg rv.Value 2 "get Value"
                equalMsg (Var.Get rv) 2 "Var.Get"
                rv.Value <- 42
                equalMsg rv.Value 42 "set Value"
                Var.Set rv 35
                equalMsg rv.Value 35 "Var.Set"
            }

            Test "Update" {
                let rv = Var.Create 4
                Var.Update rv ((+) 16)
                equal rv.Value 20
            }

            Test "SetFinal" {
                let rv = Var.Create 5
                Var.SetFinal rv 27
                equalMsg rv.Value 27 "SetFinal sets"
                rv.Value <- 33
                equalMsg rv.Value 27 "Subsequently setting changes nothing"
            }

            Test "View" {
                let rv = Var.Create 3
                let v = View.GetAsync rv.View
                equalMsgAsync v 3 "initial"
                rv.Value <- 57
                equalMsgAsync v 57 "after set"
            }

        }

    let ViewTest =
        TestCategory "View" {

            Test "Const" {
                let v = View.Const 12 |> View.GetAsync
                equalAsync v 12
            }

            Test "ConstAsync" {
                let a = async { return 86 }
                let v = View.ConstAsync a |> View.GetAsync
                equalAsync v 86
            }

            Test "FromVar" {
                let rv = Var.Create 38
                let v = View.FromVar rv |> View.GetAsync
                equalMsgAsync v 38 "initial"
                rv.Value <- 92
                equalMsgAsync v 92 "after set"
            }

            Test "Map" {
                let count = ref 0
                let rv = Var.Create 7
                let v = View.Map (fun x -> incr count; x + 15) rv.View |> View.GetAsync
                equalMsgAsync v (7 + 15) "initial"
                rv.Value <- 23
                equalMsgAsync v (23 + 15) "after set"
                equalMsg !count 2 "function call count"
            }

            Test "Map failure" {
                let count = ref 0
                let rv = Var.Create 7
                let ev = View.Map (fun x -> if x = 0 then failwith "zero" else x) rv.View
                let v = View.Map (fun x -> incr count; x + 15) ev |> View.GetAsync
                equalMsgAsync v (7 + 15) "initial"
                rv.Value <- 0 // should not change or obsolete v
                rv.Value <- 23
                equalMsgAsync v (23 + 15) "after set"
                equalMsg !count 2 "function call count"
            }

            Test "MapCached" {
                let count = ref 0
                let rv = Var.Create 9
                let v = View.MapCached (fun x -> incr count; x + 21) rv.View |> View.GetAsync
                equalMsgAsync v (9 + 21) "initial"
                rv.Value <- 66
                equalMsgAsync v (66 + 21) "after set"
                rv.Value <- 66
                equalMsgAsync v (66 + 21) "after set to the same value"
                equalMsg !count 2 "function call count"
            }

            Test "MapCachedBy" {
                let count = ref 0
                let rv = Var.Create 9
                let v = View.MapCachedBy (fun x y -> x % 10 = y % 10) (fun x -> incr count; x + 21) rv.View |> View.GetAsync
                equalMsgAsync v (9 + 21) "initial"
                rv.Value <- 66
                equalMsgAsync v (66 + 21) "after set"
                rv.Value <- 56
                equalMsgAsync v (66 + 21) "after set to the same value"
                equalMsg !count 2 "function call count"
            }

            Test "MapAsync" {
                let count = ref 0
                let rv = Var.Create 11
                let v =
                    View.MapAsync
                        (fun x -> async { incr count; return x * 43 })
                        rv.View
                    |> View.GetAsync
                equalMsgAsync v (11 * 43) "initial"
                rv.Value <- 45
                equalMsgAsync v (45 * 43) "after set"
                equalMsg !count 2 "function call count"
            }

            Test "Map2" {
                let count = ref 0
                let rv1 = Var.Create 29
                let rv2 = Var.Create 8
                let v = View.Map2 (fun x y -> incr count; x * y) rv1.View rv2.View |> View.GetAsync
                equalMsgAsync v (29*8) "initial"
                rv1.Value <- 78
                equalMsgAsync v (78*8) "after set v1"
                rv2.Value <- 30
                equalMsgAsync v (78*30) "after set v2"
                equalMsg !count 3 "function call count"
            }

            Test "MapAsync2" {
                let count = ref 0
                let rv1 = Var.Create 36
                let rv2 = Var.Create 22
                let v =
                    View.MapAsync2
                        (fun x y -> async { incr count; return x - y })
                        rv1.View rv2.View
                    |> View.GetAsync
                equalMsgAsync v (36-22) "initial"
                rv1.Value <- 82
                equalMsgAsync v (82-22) "after set v1"
                rv2.Value <- 13
                equalMsgAsync v (82-13) "after set v2"
                equalMsg !count 3 "function call count"
            }

            Test "Apply" {
                let count = ref 0
                let rv1 = Var.Create (fun x -> incr count; x + 5)
                let rv2 = Var.Create 57
                let v = View.Apply rv1.View rv2.View |> View.GetAsync
                equalMsgAsync v (57 + 5) "initial"
                rv1.Value <- fun x -> incr count; x * 4
                equalMsgAsync v (57 * 4) "after set v1"
                rv2.Value <- 33
                equalMsgAsync v (33 * 4) "after set v2"
                equalMsg !count 3 "function call count"
            }

            Test "Join" {
                let count = ref 0
                let rv1 = Var.Create 76
                let rv2 = Var.Create rv1.View
                let v = View.Join (rv2.View |> View.Map (fun x -> incr count; x)) |> View.GetAsync
                equalMsgAsync v 76 "initial"
                rv1.Value <- 44
                equalMsgAsync v 44 "after set inner"
                rv2.Value <- View.Const 39 |> View.Map (fun x -> incr count; x)
                equalMsgAsync v 39 "after set outer"
                equalMsg !count 3 "function call count"
            }

            Test "Bind" {
                let outerCount = ref 0
                let innerCount = ref 0
                let rv1 = Var.Create 93
                let rv2 = Var.Create 27
                let v =
                    View.BindInner
                        (fun x ->
                            incr outerCount
                            View.Map (fun y -> incr innerCount; x + y) rv1.View)
                        rv2.View
                    |> View.GetAsync
                equalMsgAsync v (93 + 27) "initial"
                rv1.Value <- 74
                equalMsgAsync v (74 + 27) "after set inner"
                rv2.Value <- 22
                equalMsgAsync v (74 + 22) "after set outer"
                equalMsg (!outerCount, !innerCount) (2, 3) "function call count"
            }

            Test "UpdateWhile" {
                let outerCount = ref 0
                let innerCount = ref 0
                let rv1 = Var.Create false
                let v1 = rv1.View |> View.Map (fun x -> incr outerCount; x)
                let rv2 = Var.Create 0
                let v2 = rv2.View |> View.Map (fun x -> incr innerCount; x)
                let v =
                    View.UpdateWhile 1 v1 v2
                    |> View.GetAsync
                equalMsgAsync v 1 "initial"
                rv2.Value <- 27
                equalMsgAsync v 1 "changing inner should have no effect"
                rv1.Value <- true
                equalMsgAsync v 27 "after set pred true"
                rv2.Value <- 22
                equalMsgAsync v 22 "after set inner"
                rv1.Value <- false
                equalMsgAsync v 22 "after set pred false"
                rv2.Value <- 0
                equalMsgAsync v 22 "changing inner should have no effect"
                equalMsg (!outerCount, !innerCount) (3, 2) "function call count"
            }

            Test "SnapShotOn" {
                let outerCount = ref 0
                let innerCount = ref 0
                let rv1 = Var.Create ()
                let v1 = rv1.View |> View.Map (fun x -> incr outerCount; x)
                let rv2 = Var.Create 0
                let v2 = rv2.View |> View.Map (fun x -> incr innerCount; x)
                let v =
                    View.SnapshotOn 1 v1 v2
                    |> View.GetAsync
                equalMsgAsync v 1 "initial"
                rv2.Value <- 27
                equalMsgAsync v 1 "changing inner should have no effect"
                rv1.Value <- ()
                equalMsgAsync v 27 "after taking snapshot"
                rv2.Value <- 22
                equalMsgAsync v 27 "changing inner should have no effect"
                rv1.Value <- ()
                equalMsgAsync v 22 "after taking snapshot"
                equalMsg (!outerCount, !innerCount) (3, 2) "function call count"
            }

            Test "Sequence" {
                let seqCount = ref 0
                let innerCount = ref 0
                let rv1 = Var.Create 93
                let rv2 = Var.Create 27                 
                let v2 = rv2.View |> View.Map (fun x -> incr innerCount; x)
                let rvs = 
                    seq {
                        incr seqCount
                        yield rv1.View
                        if !seqCount = 2 then
                            yield v2
                    }
                let v = 
                    View.Sequence rvs
                    |> View.Map List.ofSeq
                    |> View.GetAsync
                equalMsgAsync v [ 93 ] "initial"
                rv1.Value <- 94
                equalMsgAsync v [ 94; 27 ] "setting an item"
                rv2.Value <- 0
                equalMsgAsync v [ 94 ] "setting an item"
                rv2.Value <- 1
                equalMsgAsync v [ 94 ] "setting an outside item"
                equalMsg (!seqCount, !innerCount) (3, 1) "function call count"
            }

            Test "Get" {
                let rv = Var.Create 53
                let get1 = Async.FromContinuations (fun (ok, _, _) -> View.Get ok rv.View)
                equalMsgAsync get1 53 "initial"
                rv.Value <- 84
                equalMsgAsync get1 84 "after set"
                let v = rv.View |> View.MapAsync (fun x -> async {
                    do! Async.Sleep 100
                    return x
                })
                let get2 = Async.FromContinuations (fun (ok, _, _) -> View.Get ok v)
                equalMsgAsync get2 84 "async before set"
                rv.Value <- 12
                equalMsgAsync get2 12 "async after set"
            }

            Test "GetAsync" {
                let rv = Var.Create 79
                let get1 = View.GetAsync rv.View
                equalMsgAsync get1 79 "initial"
                rv.Value <- 62
                equalMsgAsync get1 62 "after set"
                let v = rv.View |> View.MapAsync (fun x -> async {
                    do! Async.Sleep 100
                    return x
                })
                let get2 = View.GetAsync v
                equalMsgAsync get2 62 "async before set"
                rv.Value <- 43
                equalMsgAsync get2 43 "async after set"
            }

        }

    let ListModelTest =
        TestCategory "ListModel" {
            Test "Wrap" {
                let u = ListModel.Create fst [1, "11"; 2, "22"]
                let l = u.Wrap
                        <| fun (k, s, f) -> (k, s)
                        <| fun (k, s) -> (k, s, float k)
                        <| fun (_, _, f) (k, s) -> (k, s, f)
                let uv = View.GetAsync <| u.View.Map List.ofSeq
                let lv = View.GetAsync <| l.View.Map List.ofSeq
                equalMsgAsync lv [1, "11", 1.; 2, "22", 2.] "initialization"
                u.UpdateBy (fun _ -> Some (1, "111")) 1
                equalMsgAsync lv [1, "111", 1.; 2, "22", 2.] "update underlying item"
                u.Add (3, "33")
                equalMsgAsync lv [1, "111", 1.; 2, "22", 2.; 3, "33", 3.] "insert into underlying"
                u.RemoveByKey 2
                equalMsgAsync lv [1, "111", 1.; 3, "33", 3.] "remove from underlying"
                l.UpdateBy (fun _ -> Some (1, "1111", 1.)) 1
                equalMsgAsync uv [1, "1111"; 3, "33"] "update contextual"
                l.Add (4, "44", 4.)
                equalMsgAsync uv [1, "1111"; 3, "33"; 4, "44"] "insert into contextual"
                l.RemoveByKey 3
                equalMsgAsync uv [1, "1111"; 4, "44"] "remove from contextual"
            }
        }

    let SerializerTest =
        TestCategory "Serializer" {
            Test "Typed" {
                let s = Serializer.Typed<int list>
                let x = s.Encode [1; 2; 3] |> Json.Stringify |> Json.Parse |> s.Decode
                equal (Seq.nth 2 x) 3
            }
        }

#if ZAFIR
    [<SPAEntryPoint>]
    let Main() =
        Runner.RunTests(
            [|
                VarTest
                ViewTest
                ListModelTest
            |]
        ).ReplaceInDom(JS.Document.QuerySelector "#main")

        Doc.LoadLocalTemplates "local"
        let var = Var.Create "init"
        Doc.NamedTemplate "local" (Some "TestTemplate") [
            TemplateHole.Elt ("Input", Doc.Input [] var)
            TemplateHole.Elt ("Value", textView var.View)
            TemplateHole.Text ("TValue", "Hi")
            TemplateHole.TextView ("TDyn", var.View)
        ]
        |> Doc.RunAppend JS.Document.Body
        Doc.NamedTemplate "local" (Some "TestTemplate") [
            TemplateHole.Elt ("Input", Doc.Input [] var)
            TemplateHole.Elt ("Value", textView var.View)
            TemplateHole.Elt ("Item",
                Doc.NamedTemplate "local" (Some "Item") [
                    TemplateHole.Text ("Text", "This is an item")
                ]
            )
        ]
        |> Doc.RunAppend JS.Document.Body
#endif
