# Legacy differential

- namespace2xml 2.4.0: **agrees** on the observable.
- Contract: Section 13.1 alias uniqueness and `REFERENCE004`; Section 26 item 9. Section 3.1
  preserves value references themselves, but does not enumerate the format-agnostic simple-alias
  index or the ambiguity diagnostic; both are new specification requirements introduced in Section
  13.1 rather than a Section 3.2 correction.
- Legacy observation: the baseline exits `1` with no output tree and no standard error beyond the
  banner. The measurement records no divergence.
- Clean behavior: `@x` and `Q{urn:example}x` both reduce to the simple alias `x` under Section
  13.1, so `${app.t.x}` names two canonical paths and is a blocking error. The error is attributed
  to the *referring* value, not to either candidate, because the candidates are individually legal.
  Exit is `1`, no output tree is written.
- Why the observable agreement is not compatibility evidence: 2.4.0 never had the format-agnostic
  simple-alias index, so `${app.t.x}` in that model resolves against the canonical path `app.t.x`,
  which does not exist in the data -- both scalar payloads live at typed sibling names
  (`app.t.@x` and `app.t.Q{urn:example}x`). The baseline therefore fails to resolve the reference
  and exits nonzero with no output. That coincides with the case's expected exit `1` and empty
  tree, but the case exists to pin an *ambiguity* diagnostic (`REFERENCE004`) that lists two
  canonical candidates and is attributed to the referring value at line 3 column 7. An
  implementation resolving to the first, last, or lexicographically least candidate would exit
  `0` and write an `app.properties`; only the `REFERENCE004` diagnostic in
  `expected-diagnostics.json` distinguishes the specified rule from either mishandling, and the
  verdict is not scored on that stream.

## Not asserted

- The precedence of `REFERENCE004` against a genuine missing-reference diagnostic at the same site.
  The two conditions coexist in the general case; this fixture is written so no candidate is missing
  from the data.
