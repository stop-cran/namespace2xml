# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 10.2 and 22.
- Legacy observation: 2.4.0 expanded anchors silently, so none of these four documents produced any
  diagnostic and there was no reported position to be right or wrong about.
- Clean behavior: Section 10.2 makes an anchor a blocking input error, and Section 22 fixes what
  the line it is reported at counts: "A line is terminated by LF, CRLF, or a lone CR, and by
  nothing else; consistently with Section 8.1, U+0085, U+2028, and U+2029 do not terminate a line."
  The four inputs carry the same anchor after the same two mapping entries and differ only in what
  separates the first entry from the second. `control.yaml` uses a real LF and reports line 3; the
  other three use U+0085, U+2028 and U+2029 and report line 2, because those characters are data on
  line 1 rather than the end of it. All four report column 4, the anchor's column within its own
  line being unaffected.
- The difference is intentional: the YAML scanner underneath this reader implements YAML 1.1, in
  which all three characters *are* line breaks. Passing its line number through would make the
  position of every diagnostic after such a character depend on which library read the file, which
  is the dependence Section 10.1 exists to remove. `control.yaml` is in the fixture so that a
  reader which counts the excluded characters as breaks reports line 3 four times and is caught by
  three mismatches, rather than passing on a coincidence.
