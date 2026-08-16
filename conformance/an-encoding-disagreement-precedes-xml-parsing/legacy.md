# Legacy differential

- namespace2xml 2.4.0: **agrees**, on the only two things this lane can compare -- it writes no file
  and exits 1 -- and for an entirely different reason. It reports the *other* fault. Measured: it
  emits `System.Xml.XmlException: The 'a' start tag on line 2 position 8 does not match the end tag
  of 'b'` as a raw .NET stack trace naming `ProfileReader.cs:line 143`, and never mentions the
  encoding at all.
- The agreement is therefore a coincidence of severity, and this file records it rather than a
  divergence because a verdict names what the differential lane observes. That lane compares the
  output tree and the exit code; 2.4.0 has no diagnostic stream to compare, so its message cannot
  enter the verdict even though the message is the entire subject of the case. Claiming **differs**
  here fails the lane's own check -- "a divergence nobody can observe is not a correction" -- which
  is the right answer to the right question, and worth leaving written down: a case can be about
  something the differential lane structurally cannot see.
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
- 2.4.0's message demonstrates that cost concretely: it names line 2 position 8 of a file it decoded
  as UTF-8 while the file claims windows-1252, so the position is trustworthy only because this
  particular input happens to be ASCII.