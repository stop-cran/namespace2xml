# Folded implicit items rebase above the destination mark

Acceptance item 26. Section 15.1 step 18, Section 17.5, Section 5.4.

## What the inputs ask for

One YAML source gives `a.x.list` and `a.y.list` two native sequence items each. `a.*.output`
sends both to `out.properties`, so after selector-prefix removal both contribute a sequence at the
same destination path, `list`.

Native reader items are implicit: a YAML sequence supplies no ordering values, so each contribution
allocated `0` and `1` from its own mark, which began at -1.

## What Section 17.5 requires

> Each sequence path in a destination accumulator has its own high-water mark:
>
> - an implicit item from a later output contribution is rebased onto the next fresh destination
>   ordering value;

The accumulator's mark after `a.x` is `1`. `a.y`'s two implicit items are therefore rebased to
`2` and `3`, in ascending original order, and four items survive.

## The discrimination

This is sharp because the two contributions allocated the *same* values independently. An
implementation that folded sequences by ordering value without consulting provenance would find
`a.y`'s items already occupied at `0` and `1` and patch them, publishing two lines holding
`gamma` and `delta`. The expected file has four lines, so the item count alone separates the two
readings, and the order of the four separates "rebased in ascending original order" from any other
arrangement.

## Why the rendered indices are 0..3

Section 5.4:

> Namespace and INI output must display fresh dense indices where their projection requires indices,
> but matching and precedence continue to use stable ordering values.

So `list.2` and `list.3` in the file are dense positions that happen to coincide with the stable
values `2` and `3`. The fixture does not depend on that coincidence: it asserts four values in
one order, which dense rendering preserves whatever the stable values are.

## Not asserted

Explicit ordering values meeting the fold, which patch rather than rebase, and the `append` and
`replace` strategies. Those are Section 17.1 and Section 16.11 behaviours reached through the same
merger, and are pinned by the input-merge fixtures for those clauses.

## Legacy differential

- namespace2xml 2.4.0: **differs**. The baseline writes `out.properties` with different content
  than the case expects (the harness records `content out.properties`); the exit code matches.
- Contract: Section 3.2 lists as a corrected defect legacy behavior "dependent on shared mutable
  array-index state". Section 17.5 defines the per-destination high-water mark that carries the
  correction, and Section 5.4 fixes the dense rendering of the surviving indices.
- Legacy observation: 2.4.0 had no per-destination high-water mark. The two contributions each
  allocated implicit ordering values `0` and `1` from their own local marks, and the later
  contribution's items therefore collided with the earlier ones at the destination. The rendered
  bytes are not the four values the case expects; the exact reduction 2.4.0 produces at this
  destination is not stated in the specification because 2.4.0 had no defined model to state, but
  the observable divergence is a shorter file than the expected four-line one.
- Clean behavior: each sequence path in the destination accumulator keeps its own high-water
  mark. The first contribution advances the mark to `1`, so the second contribution's implicit
  items rebase to `2` and `3`, and Section 5.4 renders the four surviving stable values as
  dense indices `0..3`.
- The difference is intentional: shared array-index state made a fold's result depend on the
  order and shape of contributions rather than on their addressed positions, which is the class
  of defect Section 3.2 exists to remove.
