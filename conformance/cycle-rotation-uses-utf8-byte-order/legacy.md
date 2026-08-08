# Legacy differential

- namespace2xml 2.4.0: **unclassified**. Legacy cycle handling did not survive to a comparable
  report, so this case makes no claim about it.
- Contract: Section 22 — "Rotate the ring so its lexicographically smallest canonical path under
  unsigned UTF-8 byte order is first"; Section 13.1 `REFERENCE003`; Section 26 item 5.
- Clean behavior: the rotation is chosen under unsigned UTF-8 byte order, which is the order the
  specification names everywhere it orders text.
- Why this case exists: C#'s native string comparison is ordinal over UTF-16 code units, and the
  two orders disagree on exactly one region. An astral scalar is a surrogate pair beginning
  `U+D800` in UTF-16 but a byte sequence beginning `F0` in UTF-8, so it sorts *below* `U+F900`
  under UTF-16 and *above* it under UTF-8. Every other pair of scalars agrees, which is why a
  corpus of ASCII cycles cannot see the difference.
- How the case proves it: the ring is `m.a` + `U+10000` against `m.a` + `U+F900`. Under the
  specified order the least member is the `U+F900` one, so the report is located at line 2 column 6
  and its `path` is that member. An implementation comparing UTF-16 rotates the other way and
  produces line 1, column 6 and the other `path` — a different `source`/`line`/`path` triple, which
  Section 24 makes part of the output.

## Not asserted

- Column numbering in the presence of an astral scalar *before* the column point. Both column
  positions here are preceded only by BMP scalars, so the case does not depend on whether columns
  count scalars or UTF-16 units.