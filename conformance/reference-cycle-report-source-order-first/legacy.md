# Legacy differential

- namespace2xml 2.4.0: **differs**. A reference cycle was detected, but the report named whichever
  member the resolver happened to reach first, which depended on the order the inputs were given.
- Contract: Section 13.1 cycle detection and `REFERENCE003`; Section 24 diagnostic ordering; Section
  26 item 5.
- Legacy observation: legacy items 101 and 1 — recursive scalar reference chains with explicit
  cycle detection, and processing made independent of accidental ordering.
- Clean behavior: a cycle is a set, not a path, so its report is rotated to its lexicographically
  least member and located at that member's origin. `source`, `line`, `column` and `path` are then
  a pure function of the cycle itself.
- Why this case exists: it is the first half of a permutation pair. This case passes
  `-i inputs/first.txt -i inputs/second.txt`; `reference-cycle-report-source-order-reversed`
  passes the same two files in the opposite order. Their `expected-diagnostics.json` files are
  byte-identical, and that identity *is* the assertion.
- How the case proves it: `app.a` and `app.b` are defined in different files and refer to each
  other, so the two runs enter the cycle from opposite ends. An implementation that reported the
  member it reached first would name `app.a` here and `app.b` in the reversed case; both fixtures
  require `app.a` at `inputs/first.txt` line 1 column 7, because the origin travels with the
  payload rather than with the order the file was read.

## Not asserted

- Cycles longer than two members, and cycles whose least member is not the first-written one.
  Rotation over longer chains is pinned by unit tests against the resolver.
