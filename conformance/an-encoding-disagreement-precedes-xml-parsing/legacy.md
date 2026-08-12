# Legacy differential

- namespace2xml 2.4.0: **differs**. It reports the *other* fault. Measured: it emits
  `System.Xml.XmlException: The 'a' start tag on line 2 position 8 does not match the end tag of
  'b'`, as a raw .NET stack trace naming `ProfileReader.cs:line 143`, and never mentions the encoding
  disagreement at all. Exit 1, matching only by coincidence of severity.
- Contract: Section 11.2; Section 7.4; Appendix B.
- Clean behavior: `PARSE002` at line 1, column 1, once for the source, and nothing else. The
  document is never parsed, so its well-formedness is not observed and cannot be reported.
- The case exists because the two faults are ordered, and only an input carrying both can show it.
  `xml-declaration-encoding-disagreement` pins the code on well-formed documents, which says nothing
  about which fault wins when a source has both. Under the reading Section 11.2 used to invite --
  that this is an "XML error" -- the condition would belong to the parser and would naturally be
  found second, which is exactly what 2.4.0 does.
- The order is not arbitrary. A source whose bytes were decoded under the wrong encoding has no
  reliable syntax to report on: every position, every name and every quoted string in an
  `XmlException` is derived from characters the decoder may have produced incorrectly. Reporting a
  tag mismatch from such a document tells the reader to go and fix a line that may not be wrong.
  Diagnosing the decode first means every later message is about text that was read correctly.
- 2.4.0's message also demonstrates the cost of the other order concretely: it names line 2 position
  8 of a file it decoded as UTF-8 while the file claims windows-1252, so the position is only
  trustworthy because this particular input happens to be ASCII.