# A generated contribution is appended beside the concrete ones, not in place of them

Section 12.4 states the property this case exists for: "Every generated `(rule,match)` result
remains a **separate contribution for every merge strategy** and is merged at its deterministic
rule/match position using the effective input-path strategy of its target: `deep`, `replace`,
`append`, or `error`."

`append` is the only one of the four strategies under which "remains a separate contribution" has
observable content. Under `deep` a generated scalar simply overrides, under `replace` the earlier
value is discarded by design, and under `error` the run stops. Only `append` requires every
contribution at the path to survive into the result, which is why the sibling case
`wildcard-generation-merges-at-rule-position` -- which uses `replace` for both of its roots -- cannot
see the difference between an implementation that keeps the contributions apart and one that folds
them into a single earlier-or-later value.

Section 16.10 fixes what the result must contain: "every item in the later sequence contribution,
including explicitly indexed items, is rebased in ascending original ordering value onto fresh
implicit ordering values above the current high-water mark", and Section 15.1 step 8 rebases only
"when a strictly earlier surviving sequence-eligible contribution exists", so "the earliest or sole
contribution retains its supplied ordering values".

Three sources contribute at `a.p`, and all three must appear. The template `a.*.0=from-template` is
the Section 4.7 CLI source ordinal 1, so it is the earliest contribution and keeps its supplied
ordering value `0`. `first` is ordinal 2 and rebases to `1`; `second` is ordinal 3 and rebases to
`2`. The template is listed first deliberately: it is the argument order under which the rule mark
and the moment generation runs cannot be confused, so the case pins the surviving-contribution
property without also depending on where a rule that falls *between* two concrete sources sorts.
That second question is [#59](https://github.com/stop-cran/namespace2xml/issues/59) and is not what
this case is about.

No diagnostic arises. `append` is declared at `a.p`, so Section 8.7's implicit-concatenation
compatibility warning does not apply, and no source contributes a native sequence.

## Legacy differential

- namespace2xml 2.4.0: **differs**. It exits `0` and writes `a.json` containing `{ "p": [ "second"
  ] }` -- one item where the contract requires three.
- Contract: Section 12.4 for the generated contribution remaining separate, Section 16.10 for what
  `append` must contain, Section 15.1 step 8 for the earliest contribution retaining its supplied
  values.
- Legacy observation, with controls. Four baseline runs produce byte-identical output:

  | Run | Sources | Scheme | Output |
  |---|---|---|---|
  | as specified | template, first, second | `merge=append` | `{ "p": [ "second" ] }` |
  | template removed | first, second | `merge=append` | `{ "p": [ "second" ] }` |
  | merge removed | template, first, second | none | `{ "p": [ "second" ] }` |
  | both removed | first, second | none | `{ "p": [ "second" ] }` |

  Removing the template changes nothing, and removing the merge directive changes nothing. The
  baseline therefore neither expanded the template nor applied `append`: it overrode at `a.p.0` and
  kept the last value written. The controls are what distinguish that from an implementation that
  did something different with the directives it was given.
- Why the difference is intentional: 2.4.0 has no `append` strategy for an input path, so the
  divergence is a capability this version adds rather than a behaviour it changes. Section 3 covers
  it under the input merge strategies.