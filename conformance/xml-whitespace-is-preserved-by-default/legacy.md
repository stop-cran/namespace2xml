# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 11.7's rule that "the default XML input mode is `PreserveWhitespace`" and
  that "the option `PreserveWhitespace` retains every text node". Section 3 does not
  enumerate this specifically; the substantive contract is Section 11.7.
- Legacy observation: the baseline writes different bytes at `r.xml`. The measurement records
  `content r.xml` at exit `0` with no standard error beyond the banner.
- Clean behavior: with no `xmlinputoptions` in the scheme, every text node in `main.xml`
  survives — including the whitespace-only text between element-only siblings and the four-
  space indentation levels — and the writer emits the file byte-for-byte with the whitespace
  it read.
- Why the difference is intentional: 2.4.0 has no stated default, so its default is whatever
  its XML reader and serializer between them produce. Either the reader discards whitespace-
  only formatting text or the serializer re-indents on output; either behavior is observable
  as different bytes, and either weakens the same-format round-trip guarantee Section 3.3
  states for normalized same-format round trips. Making preservation the default is what
  keeps the round trip byte-stable without an opt-in, and the fixture pins that the bytes
  match the input's whitespace under the default mode.
