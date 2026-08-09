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
  escapes, but the baseline treated only `\\` and the C-style whitespace triple as escapes and
  passed every other backslash through as literal text with the following scalar. `\*` therefore
  reached the JSON writer as two characters, and JSON re-encoded the backslash as `\\`.
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
