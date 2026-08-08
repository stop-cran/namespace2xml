# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 11.6's requirement that "CDATA is retained as a distinct XML node kind",
  that "XML output must preserve imported CDATA as CDATA unless an output option requests
  conversion to ordinary text", and that "if CDATA content contains `]]>`, the writer must
  split it into a valid sequence of CDATA and/or text nodes without changing the logical
  text". Section 3 does not enumerate this specifically; the substantive contract is
  Section 11.6.
- Legacy observation: the baseline writes different bytes at `cfg.xml`. The measurement records
  `content cfg.xml` at exit `0` with no standard error beyond the banner.
- Clean behavior: the three CDATA fixtures survive the round trip as CDATA. `<plain>` retains
  its single CDATA child unchanged; `<mixed>` retains its mid-string CDATA between the two
  text runs; `<split>` retains the logical text `a]]>b` by splitting into
  `<![CDATA[a]]]]><![CDATA[>b]]>`, which is the safe two-segment sequence whose lexical
  concatenation contains no lexical `]]>`. Adjacent CDATA created purely for safe splitting is
  coalesced into one logical run on the next input, so a further round trip is stable.
- Why the difference is intentional: 2.4.0 has no stated typed-XML model for CDATA — Section
  11.4 records the CDATA node kind and Section 11.6 its round-trip contract as new material —
  so a CDATA input either survives as CDATA by whatever the host writer's default is, arrives
  as plain text, has its `]]>` embedding rejected, or is silently mangled at the split. Any of
  those four outcomes produces different bytes than the expected safe-split output, and this
  fixture cannot tell them apart from one divergent file. The fixture pins only that the bytes
  are wrong.
