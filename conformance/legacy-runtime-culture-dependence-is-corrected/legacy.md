# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 3.2 removes behavior dependent on runtime culture; Section 18 permits only
  locale-independent scalar spellings and therefore classifies `1,5` as a string.
- Legacy observation: under the ordinary C differential environment the baseline's
  culture-sensitive numeric parsing exits 0 but writes a non-string JSON value.
- Clean behavior: the replacement emits the invariant string `"1,5"` and the same bytes under every
  runtime culture.
- The difference is intentional: input type must be a property of its characters, not of the
  process locale on the machine running the transformation.
