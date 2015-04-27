namespace WebSharper.UI.Next.Tests

open WebSharper
open WebSharper.JavaScript

open WebSharper.UI.Next
open WebSharper.UI.Next.Notation
open WebSharper.UI.Next.Templating

[<JavaScript>]
module Client =    
    let [<Literal>] TemplateHtmlPath = __SOURCE_DIRECTORY__ + "/template.html"

    type MyTemplate = Template<TemplateHtmlPath> 

    type Item =
        { name: string; description: string }

        static member Key i = i.name

    let Main =
        let myItems =
            ListModel.Create Item.Key [
                { name = "John"; description = "He's a cool guy." }
                { name = "Jack"; description = "I like him too." }
            ]
        let newItemName = Var.Create ""
        let newItemDescription = Var.Create ""

        MyTemplate.Doc(
            Title = View.Const "A bunch of people",
            ListContainer =
                (myItems.View |> Doc.ConvertSeqBy Item.Key (fun key item ->
                    let rvIsEditVisible = Var.Create false
                    let rvSaveEdit = Var.Create ()
                    MyTemplate.ListItem.Doc(
                        Name = (item |> View.Map (fun i -> i.name)),
                        Description = (item |> View.Map (fun i -> i.description)),
                        ToggleEdit = (fun _ -> Var.Update rvIsEditVisible not),
                        EditVisible = (rvIsEditVisible.View |> View.Map (fun v ->
                            if v then "block" else "none")),
                        EditButtonText = (rvIsEditVisible.View |> View.Map (fun v ->
                            if v then "Done" else "Edit")),
                        EditBox = (key |> myItems.GetItemPartRef
                            (fun i -> i.description)
                            (fun i v -> { i with description = v })),
                        RemoveItem = (fun _ -> myItems.RemoveByKey key)
                    )
                )),
            NewItemName = newItemName,
            NewItemDescription = newItemDescription,
            AddNewItem = (fun _ ->
                myItems.Add({ name = newItemName.Value; description = newItemDescription.Value })
                newItemName.Value <- ""
                newItemDescription.Value <- "")
        )
        |> Doc.RunById "main"
