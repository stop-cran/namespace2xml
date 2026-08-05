# Legacy differential

- namespace2xml 2.4.0: **differs**. 2.4.0 has no notion of a preview build and no structured
  diagnostic stream, so there is nothing to compare the encoding against.
- Contract: Sections 6.3 and 6.4.3.
- Legacy observation: an invocation with no arguments produced CommandLineParser's own usage text
  on standard error and a nonzero status, with no machine-readable stream.
- Clean behavior: the ordinary path resolves the diagnostic encoding, writes the Section 6.4.3
  stream and nothing else, and exits with the reserved preview status 70.
- Why this case exists: it is the only case that reaches the ordinary path rather than an
  informational mode or a command-line error. A dual-model review found that standard error was
  polluted with operational prose on exactly this path, and no fixture reached it, so the corpus,
  the comparer and the determinism script all reported success. This case closes the corpus half
  of that gap.
- Preview scope: the expected exit code is 70 only while the transformation pipeline is
  unimplemented. When the pipeline lands, this case becomes an ordinary missing-input case and
  its expected exit code and stream must be updated with it.