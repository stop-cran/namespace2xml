# Legacy differential

- namespace2xml 2.4.0: **agrees** on content and on order, modulo CRLF line endings under the
  Section 24 divergence.
- Contract: Section 10.4 for extraction, Section 5.3 for where the generated entry sorts.
- This is Section 10.4's enrichment with the data file listed first, so the wildcard rule carries
  the later Section 4.7 CLI source ordinal and the generated `c` sorts after the concrete `b` under
  Section 5.3. Both readings of the Section 10.4 / Section 5.3 disagreement filed as
  [#73](https://github.com/stop-cran/namespace2xml/issues/73) produce this result, which is what
  makes the case worth pinning separately: it holds the enrichment capability steady no matter how
  #73 is decided.
- Legacy observation: 2.4.0 emitted the same bytes for this argument order and for the reversed one
  recorded in `a-yaml-wildcard-key-enriches-each-record-of-a-later-file`, so the agreement here is
  a coincidence of a rule it did not implement rather than evidence that it ordered anything.