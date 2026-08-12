# Legacy differential

- namespace2xml 2.4.0: **agrees** on content and on order, modulo CRLF line endings under the
  Section 24 divergence.
- Contract: Section 10.4 for extraction, Section 5.4 for ordering-value provenance.
- Why the expectation is `red` then `blue`, with `green` gone. `data.yaml` is listed first, so its
  native item `green` receives the implicit ordering value `0` — Section 5.4 starts the high-water
  mark at `-1` and allocates `high-water + 1`. Section 10.4 extracts the template entry-by-entry
  through its items' ordering values, so `a.*.tags` becomes `a.*.tags.0=red` and `a.*.tags.1=blue`,
  whose items are canonical numeric mapping children and therefore carry **explicit** provenance.
  Section 5.4 then decides the collision: "reusing an explicit ordering value overrides the existing
  item at that value by ordinary source order", and the template is the later source, so `red`
  overrides `green` at `0`. `blue` supplies `1`, which is above the high-water mark and raises it.
  Rendering sorts by ordering value and emits dense indices, giving `0=red`, `1=blue`.
- **This is the only shape in which Section 10.4's provenance rule is observable.** Against an empty
  destination — which is what the sibling case
  `a-native-wildcard-template-over-a-sequence-extracts-by-ordering-value` uses — the two readings
  the specification could have taken produce identical bytes, because there is nothing at `0` to
  override. The rule was stated and, until this case, asserted nowhere: a fixture invariant under
  the question it appears to settle. Raised as
  [#75](https://github.com/stop-cran/namespace2xml/issues/75).
- The reading this case refuses is that extraction carries the sequence whole and its items keep
  implicit provenance, allocating above the destination's high-water mark. That reading produces
  `0=green`, `1=red`, `2=blue`. It is not an unreasonable reading of an enrichment idiom, which is
  why the distinction is worth a fixture rather than a remark.
- The companion case `a-namespace-sequence-template-overrides-the-same-destination-items` writes the
  same template in namespace form over the same destination and expects the same bytes. Together the
  two cases assert Section 10.4's claim that the native spelling "is the same rule as those two
  entries written in namespace form" — under the refused reading they would diverge, and which
  result a user got would depend on the file format the template happened to be written in.
- Legacy observation: 2.4.0 agrees here, but it has no wildcard ordering rule to agree with — see
  `a-yaml-wildcard-key-enriches-each-record-of-a-later-file`, where it emits the same bytes for both
  argument orders. The agreement is a coincidence of overwriting by key name.
- Mutation note, because it is not obvious and cost three inert mutants to find. **Two independent
  mechanisms implement this rule, and each alone is invariant here.** `WildcardEvaluator.Graft`
  addresses an existing sequence item by reading the generated part as an ordering value; if that
  lookup is broken the generated values arrive instead as numeric mapping children, and Section
  15.1 step 9 inference then patches them onto the same items, giving the same bytes. Break step 9's
  patch alone and `Graft` has already done the work. Only breaking **both** turns this case red, at
  `x.tags.0=green`, `x.tags.1=blue` — `verified-in-session`. The sibling ordering cases
  `a-yaml-wildcard-key-enriches-each-record-of-{a-later,an-earlier}-file` do fail on the `Graft`
  mutation alone, so the corpus is not blind to it; this case is simply downstream of a rule that
  two components agree on.
