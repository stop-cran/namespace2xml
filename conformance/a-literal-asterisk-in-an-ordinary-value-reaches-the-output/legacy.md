# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `cfg.json` as
  `{"bare":"star-*","escaped":"star-\\*"}` and exits 0. `bare` matches the expected file, but
  `escaped` carries the literal backslash `\` followed by the asterisk `*` — one Unicode character
  where the case requires the asterisk alone.
- Contract: Section 8.3 value escapes and Appendix A.3's ABNF; Section 3.2 as a correction of
  behavior "caused by unhandled user-input exceptions" only obliquely — this is more precisely a
  substantive rule of §8.3 that 2.4.0 did not implement.
- Legacy observation: 2.4.0's namespace value lexer did not recognize `\*` as an escape at all.
  Section 8.3 lists `\*` alongside `\\`, `\${`, `\n`, `\r`, and `\t` as one of the six value
  escapes, and the baseline decoded none of the six. Measured against 2.4.0, a profile carrying
  `a\\b`, `a\nb` and `a\tb` reaches a JSON destination as `"a\\\\b"`, `"a\\nb"` and `"a\\tb"`, so
  in each case the backslash survived into the model as ordinary text and the JSON writer
  re-encoded it. `\${` was not passed through either: the sequence still opened a reference, and
  an undefined one failed the run. `\*` therefore reached the JSON writer as two characters, and
  JSON re-encoded the backslash as `\\`.
- Clean behavior: §8.3 states that within an interpreted namespace-profile value "`\*` emits
  literal `*`", and Appendix A.3 lists `\*` among the `value-escape` alternatives with the note
  that "the `*` emitted by `\*` is never a wildcard token". Both values must therefore reach the
  common model as `star-*`, and the JSON writer's own escape rules leave `*` untouched.
- The difference is intentional: the whole reason `\*` exists in §8.3 is to give a namespace-value
  author a way to write a literal asterisk that a wildcard-active context will not interpret. An
  implementation that leaves the backslash in the value silently changes a shell pattern, a
  glob, or an `strftime`-adjacent format string every time somebody quotes one, and the
  difference between `*` and `\*` in the emitted file is precisely what such a value cannot
  survive.
