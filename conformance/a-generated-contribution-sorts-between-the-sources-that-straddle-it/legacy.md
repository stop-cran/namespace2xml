# A generated contribution sorts between the sources that straddle it

Section 12.4 gives a generated contribution a **position**, not a side: "Every generated
`(rule,match)` result remains a separate contribution for every merge strategy and is merged at
**its deterministic rule/match position** using the effective input-path strategy of its target:
`deep`, `replace`, `append`, or `error`."

The sibling case `wildcard-generation-appends-beside-every-contribution` pins the first half of that
sentence -- every contribution survives -- and deliberately lists the template first so that it does
not also depend on the second half. This case is the second half. The template sits **between** two
concrete sources that both write the same path, which is the only shape in which "its rule/match
position" and "earlier or later than what is there" can disagree.

## What the result must be

Section 4.7 fixes the source ordinals from the command line: `first` is 1, the template is 2, and
`second` is 3. All three contribute at `a.p`, and `a.p.merge=append` is the effective strategy
there.

Section 15.1 step 8 rebases "when a strictly earlier surviving sequence-eligible contribution
exists", and otherwise "the earliest or sole contribution retains its supplied ordering values".
Section 16.10 then says what a rebase does: "every item in the later sequence contribution,
including explicitly indexed items, is rebased in ascending original ordering value onto fresh
implicit ordering values above the current high-water mark."

Applying those in source order:

| Contribution | Ordinal | Ordering value |
|---|---|---|
| `first` | 1 | retains its supplied `0` |
| the generated `from-template` | 2 | rebased to `1` |
| `second` | 3 | rebased to `2` |

so the published sequence is `first`, `from-template`, `second`, at ordering values `0`, `1` and
`2`.

The case asserts the values as well as the order, and needs a second instrument to do it. The JSON
document fixes the order. It cannot fix the values, and neither can a namespace rendering of the
same path: Section 5.4 ordering values are exposed as literal path components densely from zero, so
a rebase that allocated `0`, `2`, `3` -- the same three items in the same sequence, from arithmetic
Section 16.10 does not license, because it measured the high-water mark against the merged node
rather than against the contribution the generated value is being appended to -- prints
`p.0`, `p.1`, `p.2` exactly as the correct one does.

Section 5.4 makes the values addressable, so a reference is the instrument that sees them.
`b.pick=${a.p.1}` resolves to `from-template` only if the generated contribution really did land at
ordering value `1`. Under the sparse allocation it is `REFERENCE002` against a value nothing
occupies, which is a different observable rather than a quieter one.

Only `append` can show this. `replace` keeps the last contribution and `deep` resolves a payload on
the Section 4.4 payload mark, so both are decided by a maximum and cannot distinguish a
contribution merged at position 2 from one merged before position 1; `error` stops the run. `append`
is the one strategy under which every contribution survives into the result, so it is the one under
which a wrong position is visible rather than absorbed.

## The control that makes this a position test

Listing the template first instead of second must produce a **different** file --
`from-template`, `first`, `second`. Two orderings that published the same bytes would leave this
case asserting only that three items exist, which the sibling already asserts. The two argument
orders differ in exactly one thing, where the rule sits among the sources, and the specification
requires the output to differ with it.

## Not asserted

Where `a.p.0` itself would sort if the two concrete sources had merged at that deeper path rather
than at `a.p`. The `append` strategy is declared at `a.p`, so Section 16.10 applies there and each
source's `a.p` subtree is a sequence contribution; the deeper question belongs to a `deep` case.

No diagnostic arises. `append` is declared at `a.p`, so Section 8.7's implicit-concatenation
compatibility warning does not apply, and no source contributes a native sequence.

## Legacy differential

- namespace2xml 2.4.0: **fails**. It exits `1` where the contract requires `0`, reporting
  `Reference OutputRoot.a.p.1 was not found at OutputRoot.b.pick`, and writes `a.json` containing
  `{ "p": [ "second" ] }` and no `b.properties` at all. The baseline's own diagnostic names the
  address the contract requires to exist: `a.p.1` is missing there because only one of the three
  contributions survived, so there is no position to be right or wrong about. That it published
  `a.json` while failing is a partial write on an unsuccessful run, and is incidental to this case.
- Contract: Section 12.4 for the generated contribution merging at its rule/match position,
  Section 16.10 for what `append` rebases, Section 15.1 step 8 for the earliest contribution
  retaining its supplied values, Section 5.4 for the values being addressable, Section 4.7 for the
  source ordinals.
- Legacy observation, with controls. Four baseline runs behave identically:

  | Run | Sources | Scheme | Exit | Output |
  |---|---|---|---|---|
  | as specified | first, template, second, probe | `merge=append` | `1` | `{ "p": [ "second" ] }`, no `b.properties` |
  | template removed | first, second, probe | `merge=append` | `1` | `{ "p": [ "second" ] }`, no `b.properties` |
  | merge removed | first, template, second, probe | none | `1` | `{ "p": [ "second" ] }`, no `b.properties` |
  | both removed | first, second, probe | none | `1` | `{ "p": [ "second" ] }`, no `b.properties` |

  Removing the template changes nothing, and removing the merge directive changes nothing. The
  baseline therefore neither expanded the template nor applied `append`: it overrode at `a.p.0` and
  kept the last value written. The controls are what distinguish that from an implementation that
  did something different with the directives it was given.
- Why the difference is intentional: 2.4.0 has no `append` strategy for an input path, so the
  divergence is a capability this version adds rather than a behaviour it changes. Section 3 covers
  it under the input merge strategies.
