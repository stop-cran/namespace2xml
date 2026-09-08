# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 11.4 canonical XML addressing and `WARN011`; Section 11.7
  `PreserveWhitespace`; Section 19.5 XML rendering.
- Legacy observation: 2.4.0 exits `0`, retains the original content-wrapped `<host>` and appends
  the ordinary overlay as a sibling `<host>`, then writes the same logical tree with CRLF line
  endings rather than the expected LF bytes. Its log does not identify the missed override.
- Clean behavior: the sibling-producing model is unchanged, but `WARN011` names
  `server.#1.host`, explains that `server.host` did not override it, and offers the exact path or
  conditional formatting-whitespace normalization as remedies.
- The difference is intentional: the run must expose this plausible data-loss trap without
  silently changing either contribution.
