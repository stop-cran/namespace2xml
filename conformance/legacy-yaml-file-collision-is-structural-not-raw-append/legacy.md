# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 3.2 removes raw appending of independently serialized YAML documents;
  Sections 17.3 and 17.5 require structural same-format folding before serialization.
- Legacy observation: the baseline exits 0 and appends two serialized mappings, producing two
  top-level `obj` keys instead of one merged mapping.
- Clean behavior: one structurally merged `obj` mapping contains `x: 1` followed by `'y': 2`
  (quoted because `y` is a portable YAML 1.1 Boolean spelling), and
  one `WARN005` reports the accepted same-destination fold.
- The difference is intentional: byte appending can produce duplicate-key YAML whose meaning
  depends on the downstream parser, while structural folding produces one deterministic document.
