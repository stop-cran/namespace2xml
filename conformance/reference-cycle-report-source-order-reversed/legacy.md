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
- Why this case exists: it is the second half of a permutation pair. This case passes
  `-i inputs/second.txt -i inputs/first.txt`; `reference-cycle-report-source-order-first` passes
  the same two files in the opposite order. Their `expected-diagnostics.json` files are
  byte-identical, and that identity *is* the assertion.
- How the case proves it: reading `second.txt` first makes `app.b` the earlier contribution and the
  first node the resolver reaches, so an implementation that reported the member it reached first
  would name `app.b` and locate the defect in `inputs/second.txt`. The expected stream names
  `app.a` at `inputs/first.txt` line 1 column 7 — identical to the sibling case, whose inputs
  arrived the other way round.

## Not asserted

- Cycles longer than two members, and cycles whose least member is not the first-written one.
  Rotation over longer chains is pinned by unit tests against the resolver.
