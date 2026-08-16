# Legacy differential

- namespace2xml 2.4.0: **agrees** on content, modulo CRLF line endings under the Section 24
- Contract: Section 19.6 — "section and key names must match `[A-Za-z0-9_.:-]+` after delimiter
  joining". A colon is admitted in a key name, and the section/key split is by path part rather
  than by scanning the text, so a colon inside the final path part stays inside the key.
- Legacy observation: no divergence.
- It exists because the colon is doing two jobs in Section 19.6 — it is the default nested-section
  delimiter and a permitted key character — and nothing pinned the second. A writer that formed
  the key by splitting on the delimiter, or that escaped the colon, would pass every other case.
- It is an input to `tools/check-ini-interop.py`, and the reason the documented reader
  configuration fixes `delimiters=('=',)`: `configparser` treats `:` as a key/value separator by
  default, which turns this line into the key `a` with the value `b=1` and reports success.