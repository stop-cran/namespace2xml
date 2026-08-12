# Legacy differential

- namespace2xml 2.4.0: **differs**. It has no `inioutputoptions` directive and its INI writer is
  handed values alone, with no comments in the model at all, so the comment lines this case
  expects cannot be produced. It also writes through a third-party formatter using
  `Environment.NewLine`, so on Windows its line terminators are CRLF rather than the LF Section 24
  requires.
- Contract: Section 19.6's "INI output bytes".
- Clean behavior: no blank line is written anywhere — not between the global preamble and the
  first section, not between one section's last key and the next section header, and not around a
  comment. A comment line is the marker, one space, and the text, immediately above the line it
  belongs to.
- Why a presentational rule is in the contract: blank lines are the decision an INI writer is most
  likely to make for readability and least likely to make identically to another writer, and
  Section 24 asks two conforming implementations for identical bytes. Every INI parser ignores a
  blank line, so nothing but a stated rule can settle it.
- Why the case carries a preamble, two sections, a nested section and comments in both regions: a
  rule that writes no blank lines has no edge cases, and this case is what demonstrates that — each
  of the four boundaries where a writer might insert one is present, and the expected file shows
  none.
- Why the nested section is here: `[s2:n]` follows `[s2]`'s own key, so the case also pins that the
  nesting delimiter does not introduce spacing of its own.
