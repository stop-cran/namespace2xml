# Legacy differential

- namespace2xml 2.4.0: **differs**. The format-agnostic alias did not exist, so neither did the
  possibility of two XML components sharing one. There was nothing to be ambiguous about, and no
  code to report if there had been.
- Contract: Section 13.1 alias uniqueness and `REFERENCE004`; Section 26 item 9.
- Legacy observation: legacy item 110 — simple format-agnostic XML aliases were added for
  convenience *while rejecting ambiguous aliases*. The convenience and the rejection are one
  feature; an alias index that resolved silently would be worse than none.
- Clean behavior: `@x` and `Q{urn:example}x` both reduce to the simple alias `x` under Section
  13.1, so `${app.t.x}` names two canonical paths and is a blocking error. The error is attributed
  to the *referring* value, not to either candidate, because the candidates are individually legal.
- Why this case exists: an alias index is a lookup that can succeed wrongly. The failure mode it
  must not have is picking one.
- How the case proves it: an implementation that resolved to the first, the last, or the
  lexicographically least candidate would produce a successful run and an `app.properties`. This
  fixture asserts exit code `1`, no output tree, and `REFERENCE004` located at line 3 column 7 —
  the `$` of the reference, not the start of the record and not either definition.
