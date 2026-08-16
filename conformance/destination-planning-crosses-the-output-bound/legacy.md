# Legacy differential

- namespace2xml 2.4.0: **agrees** on the observable. Legacy had no configurable resource limits and
  no `--max-outputs` option; the 2.4.0 CLI refuses the unknown option and exits nonzero with no
  output tree, which coincides with the case's expected exit 1 and empty tree. The observable
  agreement is therefore evidence of an option 2.4.0 does not have rather than of a planned-
  destinations bound it honors.
- Contract: Section 6.2 `--max-outputs`, "Maximum planned destination files"; Section 23 — budgets
  "consumed in their normative pipeline order" and accounted "before allocation or expansion
  whenever possible"; Section 15.1 steps 17 and 18; Section 26 item 72. Section 3 does not
  enumerate this bound; the fixture pins Section 23 rather than a Section 3.1 preservation or a
  Section 3.2 correction.
- Clean behavior: the bound is real, and the crossing is attributed to the destination that crossed
  it rather than to the run as a whole.
- Why this case exists: as with `--max-reference-depth`, the option was accepted and never consulted.
  It also has to be charged in the right place. A destination file becomes *planned* at step 18,
  which is the last point at which one destination can still absorb another under `filemerge` or a
  cross-format override; counting output instances earlier would charge for files that never exist,
  and counting after serialization would allocate the buffers the bound is there to prevent.
- How the case proves it: three sibling roots each declare one namespace output, so three distinct
  destinations are planned in declaration order. At a bound of 2 the run reports `LIMIT001` naming
  `z.properties` and exits 1, and no file is written. Naming the third destination rather than the
  first or the run pins both halves: two destinations were planned without complaint, and the third
  is the one refused.
- The diagnostic carries no `source` or `line`. The condition is a property of the planned set
  rather than of any one line of any input, which is the same reason Section 26 item 25 gives for
  `merge=error`.

## Not asserted

- Whether a destination that step 18 folds away is charged. This case has no collisions; the
  interaction of `filemerge` with the bound is not exercised.
