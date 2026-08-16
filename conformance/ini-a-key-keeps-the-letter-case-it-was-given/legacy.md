# Legacy differential

- namespace2xml 2.4.0: **agrees** on content, modulo CRLF line endings under the Section 24
- Contract: Section 19.6 — "section and key names must match `[A-Za-z0-9_.:-]+` after delimiter
  joining", and "a key line is the key text, `=`, and the value text". The grammar admits upper
  case, and nothing in Section 19.6 folds it, so the key text is written as the path spells it.
- Legacy observation: no divergence. This case does not exist to record a 2.4.0 difference.
- It exists because Section 19.6 states the key text rule and no fixture exercised it with a
  letter that could be folded. Every other INI case in the corpus is lowercase ASCII, so an
  implementation that lowercased keys would have passed the entire corpus.
- It is also an input to `tools/check-ini-interop.py`. Python's `configparser` folds option names
  to lower case by default, so this file is the one that makes the documented `optionxform = str`
  setting a claim with teeth rather than a line of prose; removing that setting turns this case
  red. See `docs/format-ini.md`.