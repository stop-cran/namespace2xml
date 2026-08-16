# Legacy differential

- namespace2xml 2.4.0: **nondeterministic**. Across forty identical runs on Linux the baseline
  aborted thirty-nine times — exit `134`, standard error carrying `Unhandled exception.
  System.InvalidOperationException: Sequence contains no elements`, and an empty `app.ini` left
  behind that is not in the expected tree — and on one run exited `1` with no output at all, which
  is indistinguishable from this case's expected result. On Windows the aborting result reports
  exit `-532462766`, the same unhandled-exception disposition under a different convention. The
  dominant result is a crash, but a crash is not what the baseline reliably does, and Appendix C.6
  requires the verdict to say so: a migrating run cannot count on either outcome. That one run in
  forty is also why C.6 makes this verdict unrefutable by sampling rather than something the lane
  re-derives.
- Contract: Sections 9.1, 9.2, 9.3, 22, and 24. Section 3.2 lists "caused by unhandled user-input
  exceptions" as a defect the replacement must not preserve, and Section 3.2 lists silent
  duplicate-key acceptance as the "parser-dependent behavior and accidental hidden overrides"
  Section 9.3 was written to close.
- Legacy observation: the JSON reader accepted comments and trailing commas, and a duplicate
  object key silently kept whichever the parser visited last, so a typo that shadowed a real
  setting produced no message at all. The strict-parse strengthening in 3.0 additionally exposed
  a legacy code path that the 2.4.0 pipeline could not survive at all on this corpus of refusals.
- Clean behavior: each nonstandard extension is `PARSE001` against the source that carries it, at
  the Section 22 one-based line and character column of the offending token. Every failing source
  reports, in the Section 7.3 command-line order, so one run names every bad file rather than
  stopping at the first.
- The difference is intentional: Section 9.3 states that rejecting duplicates "avoids
  parser-dependent behavior and accidental hidden overrides", which is exactly what the legacy
  reader did, and Section 6.3 forbids letting a user-input condition leave the process as an
  unhandled exception.
- `surrogate-escape.json` carries a `\u` escape standing for an unpaired surrogate. Section 9.1
  admits strings, and Appendix A.2 excludes surrogates from every escape, so the document denotes
  no text and is refused rather than repaired into U+FFFD. The escape is also why this source is
  here rather than in a reader unit test alone: the condition reaches the host parser through a
  path that reports it as an ordinary state error, so nothing but an end-to-end run proves it does
  not leave the process as an unhandled exception — which, on the 2.4.0 baseline, it does.
