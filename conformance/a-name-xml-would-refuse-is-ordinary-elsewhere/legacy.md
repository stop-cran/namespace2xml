# Legacy differential

- namespace2xml 2.4.0: **agrees**.
- Contract: Section 11.2's confinement of the `NCName` requirement to XML output — a
  non-conforming component "is `XML002` at the point the name would be written, and only there",
  and "the same component reaches a JSON, YAML, namespace, quoted-namespace or INI destination
  unchanged, because nothing in those formats constrains it".
- Legacy observation: the baseline writes the same three lines at `out.txt` and exits 0.
- Clean behavior: identical.
- Why the agreement is worth pinning: this fixture exists to hold the boundary of the companion
  case `an-xml-name-outside-ncname-is-refused`, where the same three components crash 2.4.0 the
  moment XML is the destination. Agreement here and a crash there, from one set of keys, is what
  shows the rule belongs to the writer rather than to the name — and it is the assertion that
  would fail first if a future implementation hoisted the check into parsing, where it would be
  cheaper to enforce and wrong.