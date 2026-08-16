# Legacy differential

- namespace2xml 2.4.0: **differs**. 2.4.0 has no structured diagnostic stream, so there is nothing
  to compare the encoding against.
- Contract: Sections 6.3, 6.4.3, 16.2, 16.3 and 19.5.
- Legacy observation: an invocation with no arguments produced CommandLineParser's own usage text
  on standard error and a nonzero status, with no machine-readable stream.
- Clean behavior: the ordinary path resolves the diagnostic encoding, writes the Section 6.4.3
  stream and nothing else to standard error, publishes the planned tree, and exits 0.
- Why this case exists: it reaches the ordinary transformation path with a stream requested. A
  dual-model review found that standard error was polluted with operational prose on exactly this
  path, and no fixture reached it, so the corpus, the comparer and the determinism script all
  reported success. This case closes the corpus half of that gap. An empty array is the assertion:
  a clean run says nothing beyond the framing.
- The command line is a valid minimal invocation because Section 6.2 makes `-i` and `-s` required.
- History: while the transformation pipeline was unimplemented this case expected the reserved
  preview status 70 and published nothing, and it was named `cli-preview-not-implemented`. The
  XML renderer retired that status, so the case now asserts the stream framing over a completed
  run. Section 14.1 denies XML the selector-name fallback for a document element, and the input
  has two top-level members, so the scheme supplies `root` explicitly.