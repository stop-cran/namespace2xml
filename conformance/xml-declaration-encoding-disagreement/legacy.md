# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 7.4, 11.2, 22, and Appendix B.
- Legacy observation: the XML declaration's `encoding` pseudo-attribute was not checked against the
  encoding the file was actually decoded with, so a file saved as UTF-8 while still declaring a
  single-byte code page was read as UTF-8 and its non-ASCII text silently became different
  characters from the ones the declaration claimed.
- Clean behavior: Section 7.4 selects the encoding from the byte-order mark, and Section 11.2 makes
  a declared name that disagrees with that selection a blocking error rather than advice. A
  declaration describing a different document from the one on disk is refused instead of guessed
  at.
- The code is `PARSE002`, not `XML002`. Section 11.2 calls the condition "a blocking XML error",
  which reads like the latter, but Appendix B assigns "XML declaration encoding inconsistent with
  decoded input" to `PARSE002`, excludes "byte-encoding disagreement" from `XML002` in the same
  table, and then separates the two a third time: "encoding disagreement is `PARSE002`, while an
  otherwise invalid XML declaration is `XML002`". This fixture is the guard on that reading.
- The position is line 1, column 1 in both sources. Section 22 fixes how a column is measured but
  not which construct anchors a diagnostic; the corpus convention is the first scalar of the
  offending construct, as the `XML001` for a document type declaration reports the `<` of
  `<!DOCTYPE`. An XML declaration may be preceded by nothing, so once Section 7.4 has removed any
  byte-order mark its first scalar is always the first scalar of the source.
- `named-utf16.xml` is the case a reader that only compares names would miss: `UTF-16` is a real
  encoding this tool supports, and it is wrong here only because this particular file carries no
  UTF-16 byte-order mark and was therefore decoded as UTF-8. Both sources report, in the Section
  7.3 command-line order, so one run names every disagreeing file.
