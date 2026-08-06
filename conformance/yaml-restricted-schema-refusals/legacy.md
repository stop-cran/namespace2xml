# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 10.1, 10.2, 10.3, 22, and 24.
- Legacy observation: the YAML reader accepted whatever the host library accepted. Anchors and
  aliases were expanded silently, so one edit to an anchored value changed every alias that
  referenced it without saying so; explicit tags were honored; a merge key silently folded another
  mapping into the current one; and a duplicate mapping key kept whichever the parser visited last,
  so a typo that shadowed a real setting produced no message at all.
- Clean behavior: Section 10.2 makes anchors, aliases and "every explicit tag token" blocking input
  errors, and adds that "no tag is accepted implicitly". Section 10.3 refuses every explicit
  document marker. Section 10.1 requires every mapping key to be a scalar and excludes both merge
  keys and duplicate keys. Each is `PARSE001` against the source that carries it, at the Section 22
  one-based line and character column of the offending token, and every failing source reports in
  the Section 7.3 command-line order so one run names every bad file.
- The difference is intentional: each construct either hides an override behind syntax or makes the
  meaning of a document depend on which library read it, and Section 10.1 exists to remove exactly
  that dependence.
