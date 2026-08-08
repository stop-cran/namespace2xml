# Legacy differential

- namespace2xml 2.4.0: **unclassified**. Legacy had no configurable resource limits.
- Contract: Section 6.2 `--max-reference-depth`, "Maximum reference recursion depth"; Section 23 —
  "wildcard, reference, output, and serialization budgets are consumed in their normative pipeline
  order"; Section 26 item 72.
- Clean behavior: the bound is real, and the crossing is attributed to the value that crossed it.
- Why this case exists: Section 6.2 declares the option, Section 23 lists the budget, and the CLI
  parsed the number into the limit record — but nothing ever consulted it. An unenforced bound is
  worse than an absent one: it is documented, accepted on the command line, and silently ignored,
  so a caller who sets it believes a run is bounded when it is not.
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
