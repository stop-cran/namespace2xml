# Legacy differential

- namespace2xml 2.4.0: **agrees** on the observable — exit 1 and no output tree — but the
  agreement is weak, and this fixture is written to say so plainly.
- Contract: Section 8.1 record classification (rule 5, unclassified records) and Section 8.2
  qualified-name grammar (unterminated `\u{HEX}` escape), together with Section 22's diagnostic
  registry. Section 3.2 lists behaviour "caused by unhandled user-input exceptions" as one of
  the corrections; this case pins the diagnostic version of that correction rather than the
  raw refusal.
- Legacy observation: 2.4.0 reads both failing files and reports each on standard output as
  a free-form log line — `Error parsing input: unexpected '<newline>', file: inputs/malformed.txt,
  line: 1, column: 13` for the record with no `=`, and `Error parsing input: unexpected '.', file:
  inputs/bad-escape.txt, line: 1, column: 4` for the malformed `\u{D800}` escape. The exit
  code is 1 and no output tree is written, which is what the case expects and what makes
  the harness's tree/exit comparison green. But the two column numbers 13 and 4 do not match
  the case's `column: 1` and `column: 5` (Appendix A.4 anchors the `\u{HEX}` column at the
  opening `\`, not at the byte the parser first refused). Neither log line carries a
  diagnostic code, a severity, a phase, or a specification anchor; both are printed to the
  wrong stream; and 2.4.0 has no structured `--diagnostics-format json` mode a machine could
  consume in place of the text. The Appendix C.6 harness compares only exit code and output
  tree, so it sees none of that.
- Clean behavior: §8.1 rule 5 states that "any remaining record without a separating `=` is
  `PARSE001`" and §8.2 says "values above U+10FFFF, values in the surrogate range U+D800
  through U+DFFF, empty digit strings, and malformed or unterminated forms are `PARSE001`".
  Two `PARSE001` diagnostics are therefore emitted, one per failing source under §22's
  "once per failing source" cardinality, each carrying the `phase`, `source`, `line`,
  `column`, and `spec` anchor §22 requires. §15.4's blocking-error recovery aborts the run
  and §21.2's validation gate publishes nothing.
- The difference is intentional: an implementation whose diagnostics have no stable code and
  no specification anchor cannot be consumed by an automated caller, and a text log stream
  that mixes with informational output is byte-unstable across runs. `agrees` is honest for
  the observable, and the honest reading is that both versions rejected the input rather
  than that they agree about what is wrong with it or where it is wrong. This fixture pins
  the correct behaviour and, in particular, that two malformed sources produce two diagnostics
  under §22's once-per-failing-source cardinality — but it cannot, against this baseline,
  evidence anything about diagnostic layout, because the baseline has nothing structured to
  compare.
