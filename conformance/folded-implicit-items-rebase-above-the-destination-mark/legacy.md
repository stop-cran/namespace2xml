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