# A wildcard output selector expands per capture tuple, not per descendant

Acceptance item 49. Section 14.1.

## What the inputs ask for

`a.*.output=namespace` is written once. The data has two subtrees under `a` — `a.x` with two
descendants, `a.y` with one — plus an unrelated `b` subtree that no selector names.

Section 14.1's worked example is exactly this shape, and it fixes both the depth and the breadth of
the expansion:

> Expansion stops at the last wildcard-containing selector part. Descendants below the captured part
> do not create deeper output instances.

and

> There is exactly one instance per unique capture tuple and literalized selector, regardless of how
> many descendants matched beneath it.

The last wildcard-containing part of `a.*` is at index 1, so candidates are the distinct paths of
length 2. Those are `a.x` and `a.y`. Two instances, and `b.kept` contributes nothing because `b` is
not selected by `a.*` under Section 14.2.

## What the expected tree asserts

Two files, `a.x.properties` and `a.y.properties`, named by Section 16.2's default: the dot-joined
concrete selector as one filename segment plus the format's extension.

The **absence** of a third file is the substance of the case. `a.x` has two descendants, `y` and
`z`; if expansion enumerated at depth 3 instead of 2 the tree would contain `a.x.y.properties`,
`a.x.z.properties` and `a.y.q.properties` and no `a.x.properties` at all. Because the harness
compares the whole produced tree rather than only the files the fixture lists, an extra file is a
failure, so this fixture pins the depth in both directions.

Each file's content has the concrete selector prefix removed, which Section 14.1 does
"unconditionally … before `type`, `key`, `root`, rendering, and `filemerge`". `a.x.y=1` is therefore
`y=1` and not `a.x.y=1`.

## Not asserted

The order the two instances were created in. It is observable only through the Section 17.5 fold
key, and nothing here collides.
