# Legacy differential

- namespace2xml 2.4.0: **unclassified**. Legacy cycle handling did not survive to a comparable
  report, so this case makes no claim about it.
- Contract: Section 13.1 cycle detection and `REFERENCE003`; Section 22 "once per canonically
  distinct reachable cycle" and its rotation rule; Section 26 item 5.
- Clean behavior: two distinct cycles are two diagnostics, whatever they look like when printed.
- Why this case exists: Section 22 counts cycles that are *canonically distinct*, and a canonical
  path escapes the delimiter, `=`, `}`, `*` and the line terminators — but not a space, a hyphen or
  a greater-than sign. A member may therefore contain the exact text an implementation is likely to
  join a chain on. `["p.a", "q -> r"]` and `["p.a -> q", "r"]` are different rings that print as the
  same string, so an implementation deduplicating on the printed chain reports the first and
  silently swallows the second. Nothing else in the corpus would notice: the exit code is 1 either
  way, and no output file is written either way.
- How the case proves it: the input defines both rings over names that collide only when joined.
  Two `REFERENCE003` diagnostics are required, each rotated to its own least member — `p.a` at line
  1, `p.a -> q` at line 3 — so a run that emits one is short, not merely differently worded.

## Not asserted

- The prose of the chain. This case pins the count and the locations; the wording is pinned by
  `reference-cycle-report-source-order-first`.