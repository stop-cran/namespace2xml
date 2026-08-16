# Legacy differential

- namespace2xml 2.4.0: **differs**. It substitutes the capture identically — `v-db` and `v-web` —
  and then emits every generated entry before every concrete one: `db.c`, `web.c`, `db.b`, `web.b`,
  where this case expects the four lines grouped by record. CRLF-terminated, under the Section 24
  divergence.
- Contract: Section 12.1 for the substitution, Section 10.4 for extraction from a native key,
  Section 5.2 and Section 5.3 for the order.
- Section 12.1 decides a value's capture form "from the owning name's captures", and the owning
  name of a template extracted from a YAML key is the template's own. A bare `*` in `v-*` therefore
  substitutes what the key's `*` matched. This is the one place a native value carries wildcard
  syntax at all — everywhere else a native scalar is `WildcardSyntax.None` — which is why it is
  fixed by a case of its own rather than left to the namespace-input equivalents.
- The order follows from the tree rather than from a list. Under `db`, the generated `c` carries
  the rule's Section 4.7 source ordinal of 1 and the concrete `b` carries 2, so Section 5.2 puts
  `c` first; `db` precedes `web` by match order. 2.4.0's grouping is what a flat list of entries
  looks like when generated entries are appended to it, and it is visible here only because the
  case has two matches rather than one.
- Legacy observation: the substitution itself is a Section 3.1 preservation. What 2.4.0 did not
  preserve is any relationship between a generated entry and the record it enriches, which is the
  same defect recorded in `a-yaml-wildcard-key-enriches-each-record-of-a-later-file` seen from a
  second angle.