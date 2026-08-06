# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 9.2, 9.3, 22, and 24.
- Legacy observation: the JSON reader accepted comments and trailing commas, and a duplicate object
  key silently kept whichever the parser visited last, so a typo that shadowed a real setting
  produced no message at all.
- Clean behavior: each nonstandard extension is `PARSE001` against the source that carries it, at
  the Section 22 one-based line and character column of the offending token. Every failing source
  reports, in the Section 7.3 command-line order, so one run names every bad file rather than
  stopping at the first.
- The difference is intentional: Section 9.3 states that rejecting duplicates "avoids
  parser-dependent behavior and accidental hidden overrides", which is exactly what the legacy
  reader did.
