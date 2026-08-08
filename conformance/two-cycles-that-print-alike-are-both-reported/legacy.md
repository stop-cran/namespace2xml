# Legacy differential

- namespace2xml 2.4.0: **agrees** on the observable.
- Contract: Section 3.1 preservation of value references (cycle detection was a legacy behaviour);
  Section 13.1 cycle detection and `REFERENCE003`; Section 22 "once per canonically distinct
  reachable cycle" and its rotation rule; Section 26 item 5.
- Legacy observation: the baseline exits `1` with no output tree and no standard error beyond the
  banner. The measurement records no divergence.
- Clean behavior: two distinct cycles are two `REFERENCE003` diagnostics, whatever they look like
  when printed, each rotated to its own least member -- `p.a` at line 1 and `p.a -> q` at line 3.
  The run exits `1` with no output.
- Why the observable agreement is not compatibility evidence: 2.4.0 detects cycles too, and its
  cycle-detection error path always produced a nonzero exit and no output, so the baseline reaches
  the same tree and exit code the specification requires here. But the case exists to pin the
  *count* of diagnostics rather than their presence: Section 22 counts cycles that are canonically
  distinct, and a canonical path escapes the delimiter, `=`, `}`, `*` and the line terminators --
  but not a space, a hyphen or a greater-than sign. The two rings this fixture writes,
  `["p.a", "q -> r"]` and `["p.a -> q", "r"]`, print as the same string, so an implementation
  deduplicating on the printed chain reports the first and swallows the second and still exits `1`
  with no output. Nothing observable in the tree or exit code distinguishes reporting one cycle
  from reporting two; the discrimination lives in `expected-diagnostics.json`, which the verdict
  does not score.

## Not asserted

- The prose of the chain. This case pins the count and the locations; the wording is pinned by
  `reference-cycle-report-source-order-first`.
