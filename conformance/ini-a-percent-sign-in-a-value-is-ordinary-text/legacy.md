# Legacy differential

- namespace2xml 2.4.0: **agrees** on content, modulo CRLF line endings under the Section 24
- Contract: Section 19.6 — "default values are unquoted single-line UTF-8 text". Section 19.6
  defines no interpolation, no variable syntax inside a value, and no meaning for `%`, so `%`,
  `%%` and `%(ratio)s` are ordinary characters and are emitted as they stand.
- Legacy observation: no divergence.
- It exists because "the value text is written as it stands" is easy to state and easy to break in
  exactly one direction — a writer that escaped or doubled `%` for a consumer's benefit would look
  helpful and would corrupt the value. No corpus file contained a `%` before this one.
- The third value is chosen to be the shape a parser with interpolation enabled would rewrite
  rather than reject, which is the failure that leaves no trace. It is an input to
  `tools/check-ini-interop.py`: with `interpolation=None` removed, `50%` is rejected outright and
  `100%%` silently becomes `100%`.