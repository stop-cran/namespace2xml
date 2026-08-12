# Legacy differential

- namespace2xml 2.4.0: **differs**. It reads `.json` inputs, so the case can be posed to it, but it
  has no Section 11.4 marker syntax: the `@v` key is not an attribute address to it, and its XML
  writer makes every leaf an attribute *unless* the scheme names it an element, which is the
  opposite default. The document this case describes is not expressible.
- Contract: Section 19.5's "XML output bytes", and Section 3.3's requirement that a round trip
  preserve content.
- Clean behavior: a CR U+000D is emitted as `&#xD;` in element text content and in an attribute
  value alike. A LF U+000A is emitted literally in text content and as `&#xA;` in an attribute
  value.
- Why CR cannot be written literally: XML 1.0 line-end normalization requires *every* parser to
  turn a literal CR — and a literal CRLF — into a single LF before the application sees it. A
  conforming writer that emits the byte therefore loses the character no matter how careful the
  reader is. `&#xD;` is not an optimization or a style; it is the only spelling of a CR that
  survives being read back.
- Why the case carries a LF as well: the two characters must be distinguishable in the output, and
  a rule that escaped both, or neither, would be simpler and wrong. The `nl` element shows the LF
  emitted as itself, so the fixture fails if an implementation over-escapes as readily as if it
  under-escapes.
- Why the attribute is present: this build already escaped CR correctly inside attribute values
  while normalizing it to LF in element text, so the two positions did not agree. A case covering
  only one position would have passed against that defect. The defect reached the corpus because
  no fixture carried a CR into an XML output at all.
