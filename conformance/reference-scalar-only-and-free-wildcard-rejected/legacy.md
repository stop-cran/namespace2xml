# Legacy differential

- namespace2xml 2.4.0: **agrees** on the observable.
- Contract: Section 3.1 preservation of value references, including scalar-only resolution and the
  rejection of free wildcard references; Section 13.1 scalar-only resolution and `REFERENCE002`;
  Section 13.3 explicit capture binding and `REFERENCE001`; Section 26 item 46.
- Legacy observation: the baseline exits `1` with no output tree and no standard error beyond the
  banner. The measurement records no divergence.
- Clean behavior: `${app.subtree}` names a node that has a descendant and no scalar payload, which
  Section 13.1 makes a missing reference rather than a subtree copy or an empty string.
  `${app.*[n]}` binds no capture that the referring name defines, so Section 13.3 refuses it as a
  free wildcard reference. Both defects fire, and Section 24 orders them by source position, so
  `REFERENCE002` precedes `REFERENCE001` in the diagnostic stream and the run exits `1` with no
  output.
- Why the observable agreement is compatible-looking but not sufficient: 2.4.0 rejected both
  constructs too — legacy items 99, 91 and 92 describe scalar-only resolution and the rejection of
  free wildcard captures — so the baseline reaches the same tree and exit code the specification
  now requires. That is the Section 3.1 preservation half of what this fixture asserts, and to
  that extent the agreement *is* compatibility evidence. What the observable does not settle is the
  Section 24 ordering claim (`REFERENCE002` before `REFERENCE001` because of source position rather
  than code) and the specific codes and anchors the diagnostic stream carries; both live in
  `expected-diagnostics.json`, which the verdict does not score.

## Not asserted

- The distinction between `REFERENCE002` and `REFERENCE005` for a node carrying an explicit empty
  container mark. `app.subtree` here has a descendant, which Section 13.1 names outright; the
  empty-mapping case reads differently against the Section 22 registry row and is recorded in
  `KNOWN-LIMITS.md` rather than pinned here.
