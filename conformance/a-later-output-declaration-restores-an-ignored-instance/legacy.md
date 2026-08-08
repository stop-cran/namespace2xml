# A later `output` declaration restores an ignored instance

Acceptance item 50. Section 15.2.

## What the inputs ask for

Three `output` declarations, in source order:

1. `a.x.output=namespace`
2. `a.*.output=ignore`
3. `a.y.output=namespace`

Section 15.2's ignore-mechanism table gives `output=ignore` the scope "one concrete output
instance" and the later restoration "later non-ignore `output` declaration". A restoration is only
expressible if the declarations that literalize to one concrete selector are one stream, which is
what the clause above the table states:

> exact and wildcard declarations that literalize to the same concrete selector participate in one
> source-ordered override stream

So there are exactly two streams here:

| Concrete selector | Stream in source order | Winner |
|---|---|---|
| `a.x` | `namespace`, `ignore` | `ignore` |
| `a.y` | `ignore`, `namespace` | `namespace` |

## What the expected tree asserts

One file, `a.y.properties`.

Both directions are load-bearing and they fail differently. `a.x` proves a wildcard `ignore`
suppresses an instance an **earlier** exact declaration created -- ordering by specificity rather
than by source position would keep `a.x.properties`, and Section 15.2 says plainly that "pattern
specificity does not alter precedence". `a.y` proves the restoration: a stream that let the first
non-ignore `output` win, or that treated `ignore` as terminal for the selector, would emit
nothing at all and leave the run with an empty output plan.

An implementation with no stream at all writes **both** files, because each declaration is then its
own instance and the ignored ones simply produce nothing next to the ones that produce something.

## Not asserted

Which declaration supplies the surviving instance's Section 21.3 declaration order. One destination
cannot observe it.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 3.2 correction against a synthetic internal root leaking into user-visible file
  names; Section 15.2 `output=ignore` restoration; Section 16.2 default filename composition.
- Legacy observation: the baseline writes `y.properties` where this case expects `a.y.properties`,
  and it writes no `x.properties`, so exactly one file lands but its name has lost the `a.` prefix.
  The measurement records this as `missing a.y.properties; extra y.properties`, with exit `0` and
  no standard error beyond the banner.
- Clean behavior: the default filename is composed from the whole concrete selector under
  Section 16.2, so `a.y` writes to `a.y.properties`. `output=ignore` restoration then keeps the one
  surviving instance's name intact.
- Why the difference is intentional: dropping a leading namespace segment from a filename is the
  synthetic-root leak Section 3.2 names, and the same defect would make two distinct sibling
  instances collide on a shorter shared name on any platform. The single-file observation says
  nothing about whether 2.4.0's stream logic reached the same restoration decision or arrived at
  one surviving instance by another route; only the filename is pinned here.