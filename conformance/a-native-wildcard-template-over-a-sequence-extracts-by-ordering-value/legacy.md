# Legacy differential

- namespace2xml 2.4.0: **differs**. It treats `*` as an ordinary key, so the template leaks into the
  output as literal data instead of generating anything.
- Contract: Section 10.4. "Extraction is entry-by-entry", and a sequence beneath a wildcard key is
  extracted "through its items' ordering values, which Section 5.4 exposes as decimal name parts".
- The template declares `a.*.tags.0=red` and `a.*.tags.1=blue`, so it is the same rule as those two
  entries written in namespace form. That equivalence is the point of the case: a native template
  and its namespace spelling are one entry written two ways, and a divergence between them would be
  its own defect.
- The items become canonical numeric mapping children, so Section 5.4 gives them explicit ordering
  provenance even though the source spelled a native sequence. Extraction flattens native shape into
  namespace-shaped entries here exactly as Section 10.4 already has it do for the mapping ancestors
  above, which "do not contribute mapping-presence marks".
- This case previously expected exit `70`, on the reading that the shape was under-determined
  because a native sequence item takes its ordering value from the destination's high-water mark and
  the destination is unknown at extraction time. Section 12.4 answers that directly — "a generated
  contribution reserves or allocates ordering values only when it is generated" — so the timing was
  never the obstacle it was recorded as.
