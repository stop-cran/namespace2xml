# A later empty sequence does not move a generated contribution before the items

Section 12.4 merges a generated `(rule,match)` result "at its deterministic rule/match position".
The sibling case `a-generated-contribution-sorts-between-the-sources-that-straddle-it` covers the
shape where concrete contributions sit on both sides of the rule. This case covers the shape where
they do not, and the node's own latest mark says otherwise anyway.

`empty.json` contributes `a.p` as an explicitly empty native sequence. Section 15.1 step 8 and
Section 16.10 make that a sequence contribution with nothing to rebase, so it appends no item and is
not an error -- but it is still a contribution at `a.p`, and Section 4.4 refreshes the node's
container shape-mark with it. The node's latest mark therefore belongs to source 3 while every
**item** at the path belongs to source 1.

A generated contribution from source 2 is later than the only item and earlier than the node. The
two questions have different answers, and Section 12.4 asks the first one: the position is among the
contributions that supply the sequence, not against the mark the node happens to carry.

## What the result must be

Section 4.7 fixes the source ordinals from the command line: `first` is 1, the template is 2, and
`empty` is 3.

| Contribution | Ordinal | Effect |
|---|---|---|
| `first` | 1 | earliest contribution, retains its supplied `0` |
| the generated `from-template` | 2 | rebased above the high-water mark, to `1` |
| the empty native sequence | 3 | a sequence contribution with no items to rebase |

so the published sequence is `first`, `from-template`, and appending an empty list leaves it alone.

## Why this shape is worth its own case

Judging the position against the node rather than against the items publishes
`from-template`, `first` -- the generated value first, because the node's latest mark is source 3's
and the rule is earlier than that. Both orders contain the same two items, so only their sequence
distinguishes them, and no case in which every contribution supplies an item can produce the
disagreement: it takes a later contribution that moves the mark without supplying anything to
compare against.

## Not asserted

That an empty native sequence is accepted rather than refused -- `merge-strategies-over-native-
sequence-shapes` pins that, and this case would still be meaningful if the acceptance rule were
argued elsewhere. This case assumes it and asserts only where the generated item lands.

## Legacy differential

- namespace2xml 2.4.0: **differs**. It exits `0` and writes `a.json` containing
  `{ "p": [ "from-template" ] }` -- one item where the contract requires two, because the generated
  entry overrode `a.p.0` instead of being appended beside it.
- Contract: Section 12.4 for the generated contribution merging at its rule/match position,
  Section 16.10 for an empty sequence contribution having nothing to rebase, Section 15.1 step 8 for
  the earliest contribution retaining its supplied values, Section 4.4 for a contribution refreshing
  the container shape-mark, Section 4.7 for the source ordinals.
- Legacy observation, with controls:

  | Run | Sources | Scheme | Output |
  |---|---|---|---|
  | as specified | first, template, empty | `merge=append` | `{ "p": [ "from-template" ] }` |
  | template removed | first, empty | `merge=append` | `{ "p": [ "first" ] }` |
  | merge removed | first, template, empty | none | `{ "p": [ "from-template" ] }` |
  | empty removed | first, template | `merge=append` | `{ "p": [ "from-template" ] }` |

  Unlike the sibling case, the baseline here **does** expand the template: removing it changes the
  output to `{ "p": [ "first" ] }`, so the generated entry is what replaced `first`. Removing the
  merge directive changes nothing, so `append` was ignored, and removing the empty document changes
  nothing, so it contributed nothing either way. The baseline overrode at `a.p.0` and kept the last
  value written, which for these three sources is the generated one.
- Why the difference is intentional: 2.4.0 has no `append` strategy for an input path, so the
  divergence is a capability this version adds rather than a behaviour it changes. Section 3 covers
  it under the input merge strategies.
