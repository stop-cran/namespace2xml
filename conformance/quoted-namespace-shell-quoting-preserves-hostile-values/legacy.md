# Legacy differential

- namespace2xml 2.4.0: **fails**. It exits 1 rather than the expected 0, writes no output,
  and reports on standard output `Error reading input: Reference OutputRoot.b was not found
  at OutputRoot.cfg.dollar [file: inputs/values.txt, line: 3]`. The whole run fails on one
  value, `cfg.dollar=a\${b}c`, whose intent under §8.3 is a literal `${b}` that never
  reaches reference resolution.
- Contract: Section 8.3's value escape `\${` and Appendix A.3's ABNF, together with §19.2's
  single-quote shell escape rule. Section 3.2 as a correction of behaviour "caused by
  unhandled user-input exceptions".
- Legacy observation: 2.4.0's namespace value lexer did not recognize `\${` as an escape.
  The `\` was passed through as literal text and the `${b}` that followed it went to the
  reference-recognition pass as a live reference. `b` was never defined at any input path,
  so the reference machinery reported it as missing and blocked the run. Every other value
  in this fixture would have exercised §19.2's shell quoting — the apostrophe, the
  backtick, the double quote, the multiline `\n`, `hi!`, `a"b`, and so on — but the run
  never reached the writer, because one earlier value in source order was rejected.
- Clean behavior: §8.3 states that within an interpreted namespace-profile value "`\${`
  emits literal `${`", and Appendix A.3's ABNF lists `\${` among the six recognized
  `value-escape` alternatives. The value `a\${b}c` therefore reaches the common model as
  the six-character string `a${b}c`, no reference is present at that path, and §19.2
  emits it as `dollar='a${b}c'` — single-quoted, because "single-quote shell escaping ...
  preserves spaces, `$`, backticks, double quotes, backslashes, exclamation marks, and
  line breaks without expansion". The other seven values in the fixture cover every
  hostile scalar §19.2 lists.
- The difference is intentional: a shell template author writes `\${b}` to say "produce
  the exact bytes `${b}` and do not consult the tool's reference machinery", and an
  implementation that treats the sequence as a live reference has taken the escape
  away from the very use case shell quoting exists for. The 3.0 rule fails no run at all
  on this input; it emits a file the caller can `source` and get the seven hostile values
  back byte-identical.
