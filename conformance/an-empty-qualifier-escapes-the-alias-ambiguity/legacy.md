# An empty qualifier escapes the alias ambiguity

Acceptance item 9. Sections 13.1 and 11.4.

## What the inputs ask for

`a.t` carries an attribute `@x` and a child element `x`. Section 13.1 makes the unmarked spelling
ambiguous between them, because "an XML simple alias … replaces every `@local` part with `local`"
and an ordinary path "aliases to itself", so both scalars have the simple alias `a.t.x`. Referring to
`${a.t.x}` is therefore a blocking `REFERENCE004`, which the fixture
`reference-alias-ambiguity-lists-candidates` already pins.

This case is about the way out. Section 13.1's worked example ends:

> `${a.@x}` selects the attribute and `${a.Q{}x}` selects the child element

and Section 11.4 says why that spelling exists at all:

> The marker does not narrow the component — it narrows the *addressing*. An unmarked component
> resolves through the simple alias index …; a marked component bypasses that index and names one
> canonical component outright.

## What the expected tree asserts

`marked=element` and `attribute=attribute` are the two halves of that sentence: each marker selects
one of the two competing scalars, and neither reference is an error.

`t.x2=element` pins the scope of the marker. It is written `${a.Q{}t.x}`, marking `t` rather than the
ambiguous final component. Section 13.1 decides canonical addressing per *reference*, not per
component — "a reference **containing** an unescaped XML `Q{...}`, `@`, or `#n` typed address
component is canonical and resolves one exact canonical path" — so one marker anywhere takes the
whole reference off the index, and the exact path `a.t.x` is the element. An implementation that
applied the marker only to the component carrying it would leave the final `x` on the index and
fail with `REFERENCE004`.

`same=element-element` shows the two addressing modes side by side in one value. `${a.t.Q{}x}` is
canonical; `${a.t.x2}` is format-agnostic and resolves through the index, which is unambiguous for
`x2` because no attribute competes for it. Section 11.4: "Where no such alias competes, `a.b` and
`a.Q{}b` name the same thing and behave identically."

`t.@x` and `t.x` are rendered because they are ordinary data in the selected view, and `t.x2` renders
inside `t`'s block rather than at the end: `t` takes its Section 5.4 ordering value when it is first
seen, and a container renders its whole subtree at its own position.

## Why no file records a marker

Nothing in the output is spelled `Q{}`. Section 11.4's first clause -- "A `Q{}local` component and
an unmarked `local` component are the same component and address the same overlay node" -- means the
marker is an addressing annotation on the reference that was *written*, not a distinct component. It
cannot change the identity of a node, so it cannot appear in a canonical path. `a.t.Q{}x=1` and
`a.t.x=2` are one node, and the second overrides the first.

Both halves of that sentence have to hold at once, and an implementation naturally satisfies only
one: representing `Q{}x` as an ordinary component satisfies the identity half and silently discards
the addressing, which leaves the ambiguity above with no in-band answer at all; representing it as a
distinct component satisfies the addressing half and splits one overlay node in two.

## Not asserted

`Q{}` in a scheme selector. Section 15.2 grants the same escape to output-view directives, and
`KNOWN-LIMITS.md` section 1.10 records that scheme paths do not consult the alias index in the first
place, so there is nothing there for the marker to escape yet.