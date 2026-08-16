# Legacy differential

- namespace2xml 2.4.0: **agrees** on the observable.
- Contract: Section 6.2 `--max-reference-depth`, "Maximum reference recursion depth"; Section 23 —
  "wildcard, reference, output, and serialization budgets are consumed in their normative pipeline
  order"; Section 26 item 72. Section 3 does not enumerate this bound; the fixture pins Section 6.2
  and Section 23 rather than a Section 3.1 preservation or a Section 3.2 correction.
- Legacy observation: the baseline exits `1` with no output tree and no standard error beyond the
  banner. The measurement records no divergence.
- Clean behavior: the bound is real, and the crossing is attributed to the value that crossed it.
  At a bound of 2 the run reports `LIMIT001` at `r.c` and exits `1`, and no output tree is written.
- Why the observable agreement is not compatibility evidence: 2.4.0 has no `--max-reference-depth`
  option, so its CommandLineParser refuses the unknown flag before any input is read and returns
  nonzero without writing anything. That coincides with the case's expected exit `1` and empty
  tree, but this fixture exists to pin the bound as a threshold in both directions and to attribute
  the crossing to `r.c` on line 3. Neither the location nor the presence of a `LIMIT001` diagnostic
  is visible to the tree comparison, so the observable is silent on what the case actually asserts.
- Why this case exists: Section 6.2 declares the option, Section 23 lists the budget, and the CLI
  parsed the number into the limit record — but the 3.0 code path initially failed to consult it.
  An unenforced bound is worse than an absent one: it is documented, accepted on the command line,
  and silently ignored, so a caller who sets it believes a run is bounded when it is not.
- How the case proves it: `r.a` refers to `r.b` refers to `r.c` refers to `r.d`, which is a settled
  scalar. At a bound of 2 the run reports `LIMIT001` at `r.c` — line 3 — and exits 1. That location
  is the whole assertion. It pins the bound as a threshold in both directions at once: levels 1 and
  2 were entered without complaint, so the bound is not off by one downward, and level 3 was
  refused, so it is not off by one upward. It also pins what "depth" counts. Reaching `r.d` is a
  lookup of a value that is already settled, not another level of recursion, so the depth entered is
  the number of nested *unresolved* values and a wide model of a thousand independent one-deep
  references is not deep.

## Not asserted

- Interaction with a cycle. A cycle is caught by Section 13.1's own detection before any depth
  accumulates, so the two never compete.
