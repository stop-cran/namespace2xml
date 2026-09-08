# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 11.4 canonical XML addressing and `WARN011`; Section 11.7
  `PreserveWhitespace`; Section 19.5 XML rendering.
- Legacy observation: 2.4.0 exits `0`, retains both `<b>` elements, and writes the same logical
  tree with CRLF line endings rather than the expected LF bytes. Its log contains no warning that
  `a.b` did not override the content-wrapped `a.#1.b` element.
- Clean behavior: the run still retains both elements because diagnostics do not rewrite the
  merged model, but `WARN011` names `a.#1.b` and recommends that exact path. The meaningful text
  around the element is preserved, so normalization is not offered as an unconditional remedy.
- The difference is intentional: the warning makes the plausible sibling-producing result visible
  without changing XML content or discarding meaningful whitespace.
