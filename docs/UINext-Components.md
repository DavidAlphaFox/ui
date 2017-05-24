# Components
> [UI.Next Documentation](UINext.md) ▸ **Components**

Basic guidelines are:

* Describe state using several [Var](UINext-Var.md) cells

* Hide your state, so that your component can locally enforce invariants on it

* Just as in normal F# and JavaScript, expose methods and callbacks for
  clients to send and receive event notifications for your component

* Accept and expose [View](UINext-View.md) values to express time-varying quantities 

* Accept and expose [Doc](UINext-Doc.md) values for UI that can be embedded into a document tree

* Make components higher order, so clients can create as many instances as needed

The strategy is fairly similar to creating reusable
components in F# or JavaScript.  One advantage is having new vocabulary
for [Var](UINext-Var.md), [View](UINext-View.md) and [Doc](UINext-Doc.md).  Making a distinction
between time-varying quantities and event occurences makes your code easier
to understand and easier to get right.

