# Wildcard-then-specific and specific-then-wildcard both follow source order

Acceptance item 19. Section 15.2.

## What the inputs ask for

Two independent subtrees, each with a wildcard `output` and a competing pair of `root` directives,
arranged so the two subtrees disagree about which spelling comes last:

1. `a.*.output=namespace`
2. `a.*.root=W`
3. `a.x.root=X`
4. `b.*.output=namespace`
5. `b.p.root=P`
6. `b.*.root=Z`

Section 15.2 supplies both the stream and the tie-break it refuses to make:

> exact and wildcard declarations that literalize to the same concrete selector participate in one
> source-ordered override stream;

and, above it:

> A later matching directive overrides an earlier matching directive for the same effective setting.
>
> Pattern specificity does not alter precedence.

So each concrete selector has one stream, ordered by position in the scheme, and the last entry
wins regardless of how specifically it was written.

| Concrete selector | Stream in source order | Winner |
|---|---|---|
| `a.x` | `W` (line 2), `X` (line 3) | `X` |
| `a.y` | `W` (line 2) | `W` |
| `b.p` | `P` (line 5), `Z` (line 6) | `Z` |
| `b.q` | `Z` (line 6) | `Z` |

## What the expected tree asserts

Four files. Section 16.3 prefixes namespace keys with the `root`, after the selector prefix is
removed, so the payloads are `a.x.properties` = `X.k=1`, `a.y.properties` = `W.k=2`,
`b.p.properties` = `Z.k=3`, `b.q.properties` = `Z.k=4`.

The two subtrees fail differently and neither alone is sufficient.

`a` catches an implementation that drops exact declarations landing on a wildcard-created instance:
it writes `W.k=1`, the wildcard's value, because `a.x.root` was never consulted. `a` alone would
still pass under a rule that ranked exact declarations above wildcards.

`b` is the control for exactly that. Here the wildcard is written **last**, so specificity and
source order disagree. An implementation that preferred the more specific pattern writes `P.k=3`.
Only source order yields `Z.k=3`, and `b.q` holds the ranking rule to account by taking `Z` from the
same declaration.

`a.y` and `b.q` additionally pin that neither exact directive leaked onto the sibling instance its
own wildcard produced.

## Not asserted

The diagnostic stream is `[]`, pinning the absence of `WARN009` for `a.x.root` and `b.p.root`.
The case says nothing about `filename`; every instance here keeps its defaulted name so that the
`root` values are the only thing under test.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 3.2 correction against a synthetic internal root leaking into user-visible file
  names. The Section 15.2 ordering rule under test is not a Section 3 divergence.
- Legacy observation: the baseline writes four files whose contents are `X.k=1`, `W.k=2`, `Z.k=3`
  and `Z.k=4` -- byte-identical to what this case expects -- but names them `x.properties`,
  `y.properties`, `p.properties` and `q.properties`. It exits `0` with nothing on standard error
  beyond the banner. The measurement records four missing and four extra paths.
- Clean behavior: identical payloads, with each defaulted filename composed from the whole concrete
  selector under Section 16.2.
- What the agreement means here, unusually: every payload matches, in both directions, including the
  specific-then-wildcard direction that discriminates source order from pattern specificity. The
  baseline implements this clause correctly. The divergence is confined to the defaulted file names,
  which this case did not set out to test. Like its companion case, this is a regression test
  against a defect introduced by the 3.0 rewrite rather than a correction of legacy behavior.
