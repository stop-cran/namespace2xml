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