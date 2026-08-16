# Legacy differential

- namespace2xml 2.4.0: **agrees** on the observable. Neither `--max-xml-attributes` nor
  `--max-nodes` was a 2.4.0 option, and the 2.4.0 CLI refuses unknown options with a nonzero exit
  and no output tree. That produces the case's expected exit 1 and empty tree, but for an
  unrelated reason — the run never reaches parsing, so no attribution rule is exercised. The
  agreement is therefore evidence that the options do not exist rather than of a bound the
  baseline honors.
- Contract: Sections 7.3, 11.1, 15.4, 22, 23, and 24. Section 3 does not enumerate these bounds;
  they are Section 23 additions rather than Section 3.1 preservations or Section 3.2 corrections.
- Legacy observation: an XML element could carry any number of attributes, and nothing bounded the
  parse. There was no option to bound it and no diagnostic when the cost became unreasonable —
  but that failure mode is unreachable here, because the tool rejects the option before the
  sources are opened.
- Clean behavior: Section 23 checks XML attributes "per element within each source under
  `--max-xml-attributes`, as specified in Section 11.1", and a crossing is `LIMIT001`.
- This case is the mirror of `limit-attribution-across-sources`, with the two sources exchanged in
  `-i` order. `inputs/wide.xml` now comes first and crosses the per-element attribute bound while
  it is being parsed; `inputs/many.xml` comes second and crosses the global `--max-nodes` total at
  the parse-phase join. Section 11.1 attributes the single reported occurrence to "the earliest
  under CLI source order", so the per-element crossing in the first source is what is reported,
  even though the global total is not decided until every source has been read.
- The pair is what makes either case load-bearing. Reported alone, the winning source could be
  explained by the order the two crossings were detected in rather than by command-line order,
  because in one arrangement those agree. Exchanging the sources makes them disagree in the other
  arrangement, and Section 11.1's answer is unchanged. This case additionally fails if
  `--max-xml-attributes` is not enforced at all: `inputs/wide.xml` would then contribute its nodes
  to the global total instead of being refused, and the reported source would be `inputs/many.xml`.
- Nothing is published and no output tree is expected, because Section 15.4 aborts before the next
  phase when a phase holds a blocking diagnostic.
- The difference is intentional: Section 23 requires the tool to "fail explicitly rather than
  degrade without bound".
