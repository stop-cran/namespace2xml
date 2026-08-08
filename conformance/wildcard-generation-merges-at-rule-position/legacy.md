# A generated contribution merges at its rule's position, and at every path it reaches

Section 12.4 settles when a template may match: "Every template must be matched against every
eligible concrete or generated entry present in the current fixed-point evaluation, regardless of
whether the matched entry originated before or after the template. **Source order controls
precedence, not visibility.**" It then settles how the result is folded in -- "merged at its
deterministic rule/match position using the effective input-path strategy of its target" -- and
repeats the point once more: "The rule mark still controls conflict precedence."

Generation necessarily runs after every concrete source has been read, and both roots here are
cases where that implementation fact must not become an observable one.

`precedence` is about which contribution is later. The template `precedence.*=template` is in the
first source and the concrete `precedence.x=concrete` is in the second, so at `precedence.x` the
generated value is the *earlier* contribution. Section 16.10 `replace` says "the later complete
value replaces the earlier value", and the later value is `concrete`. Treating the generated value
as later because it was produced last inverts the sources and publishes the template.

`whole` is about where the strategy is consulted. The template `whole.*.z=two` generates
`whole.x.z=two`, whose *name* is deeper than the path carrying the directive. Section 16.10 defines
the scope that matters: "A contribution is **at path `P`** when it contributes a payload, explicit
container presence, sequence projection, **or any descendant under `P`**." The generated entry is
therefore a contribution at `whole.x`, where `replace` is in force, and `replace` acts on the
complete value -- "payload, container presence, children, and sequence projection". The earlier
payload `one` is part of that value and does not survive. An implementation that walks down to the
generated leaf and consults the strategy only there deep-merges instead, and keeps a payload the
scheme asked it to replace.

The two roots also cover the two directions of the same comparison: in `precedence` the generated
contribution loses, and in `whole` it wins.

Neither root emits a diagnostic. `replace` is declared, so Section 8.7's implicit-concatenation
compatibility warning does not arise, and nothing here contributes a sequence for it to be about.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 12.4 requires that "the rule mark still controls conflict precedence" for a
  generated contribution, and Section 16.10 defines the `replace` scope as "payload, container
  presence, children, and sequence projection" at the directive's path. Section 3 does not
  enumerate this correction as a named item; the substantive contract is Sections 12.4 and 16.10.
- Legacy observation: the baseline writes `precedence.properties` byte-identically with the
  expected content, but writes different bytes at `whole.properties`. The measurement records a
  `content whole.properties` divergence with exit `0` and no standard error beyond the banner.
- Clean behavior: the generated contribution `whole.*.z=two` is a contribution at `whole.x`, so
  the effective `whole.x.merge=replace` discards the earlier `whole.x=one` payload together with
  the rest of the value at `whole.x`. Only `x.z=two` survives. On the `precedence` root the two
  tools happen to agree because both retain the concrete `precedence.x=concrete` value.
- Why the difference is intentional: 2.4.0 has no stated model for where a generated contribution
  merges relative to the concrete inputs it aligns with, and no rule that consults an input merge
  strategy at an ancestor of a generated leaf. Reading the strategy only at the generated leaf,
  or treating the generation as later because it was produced last, both keep a payload the
  scheme asked to replace, which is the source-order and merge-scope contract Section 12.4 and
  Section 16.10 make explicit. Whether the baseline's specific bytes come from either reading is
  not observable from one divergent file, so the case pins only that they are wrong.

