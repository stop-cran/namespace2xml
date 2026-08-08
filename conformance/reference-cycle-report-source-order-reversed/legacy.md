# Legacy differential

- namespace2xml 2.4.0: **agrees** on the observable.
- Contract: Section 3.1 preservation of value references and cycle detection; Section 13.1 cycle
  detection and `REFERENCE003`; Section 24 diagnostic ordering; Section 26 item 5.
- Legacy observation: the baseline exits `1` with no output tree and no standard error beyond the
  banner. The measurement records no divergence.
- Clean behavior: a cycle is a set, not a path, so its report is rotated to its lexicographically
  least member and located at that member's origin. `source`, `line`, `column` and `path` are then
  a pure function of the cycle itself. Exit is `1` with no output.
- Why this case exists: it is the second half of a permutation pair. This case passes
  `-i inputs/second.txt -i inputs/first.txt`; `reference-cycle-report-source-order-first` passes
  the same two files in the opposite order. Their `expected-diagnostics.json` files are
  byte-identical, and that identity *is* the assertion.
- Why the observable agreement is not compatibility evidence: 2.4.0 detected recursive scalar
  reference cycles (legacy items 101 and 1) and its cycle-detection error path always exited
  nonzero without emitting a file, so the baseline reaches the same tree and exit code the
  specification requires here. But this fixture's discrimination lives entirely in the diagnostic
  stream: the pair asserts that `app.a` at `inputs/first.txt` line 1 column 7 is reported in both
  input orders. Reading `second.txt` first makes `app.b` the earlier contribution and the first
  node the resolver reaches, so an implementation that reported the member it reached first would
  name `app.b` and locate the defect in `inputs/second.txt`; it would still exit `1` with no
  output. Only the sibling case's byte-identical `expected-diagnostics.json` can tell that
  mishandling apart from the rotation-to-least-member rule the specification pins, and the verdict
  does not score that stream.

## Not asserted

- Cycles longer than two members, and cycles whose least member is not the first-written one.
  Rotation over longer chains is pinned by unit tests against the resolver.
