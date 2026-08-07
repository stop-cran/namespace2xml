# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 9.1, 9.2, 9.3, 22, and 24.
- Legacy observation: the JSON reader accepted comments and trailing commas, and a duplicate object
  key silently kept whichever the parser visited last, so a typo that shadowed a real setting
  produced no message at all.
- Clean behavior: each nonstandard extension is `PARSE001` against the source that carries it, at
  the Section 22 one-based line and character column of the offending token. Every failing source
  reports, in the Section 7.3 command-line order, so one run names every bad file rather than
  stopping at the first.
- The difference is intentional: Section 9.3 states that rejecting duplicates "avoids
  parser-dependent behavior and accidental hidden overrides", which is exactly what the legacy
  reader did.
- `surrogate-escape.json` carries a `\u` escape standing for an unpaired surrogate. Section 9.1
  admits strings, and Appendix A.2 excludes surrogates from every escape, so the document denotes
  no text and is refused rather than repaired into U+FFFD. The escape is also why this source is
  here rather than in a reader unit test alone: the condition reaches the host parser through a
  path that reports it as an ordinary state error, so nothing but an end-to-end run proves it does
  not leave the process as an unhandled exception, which Section 6.3 forbids.
