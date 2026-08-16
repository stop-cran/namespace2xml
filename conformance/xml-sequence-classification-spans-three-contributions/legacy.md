# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `r.properties` containing three entries
  `r.c.d=`, `r.c.d.0=`, `r.c.d.1=` — a scalar and two indices, all of them empty — and exits 0
  with nothing on standard error. The case expects four values `r.c.d.0=1` through `r.c.d.3=4`.
  The baseline therefore loses every value, invents a scalar at a path the specification makes a
  sequence, and miscounts the sequence by two.
- Contract: Section 17.4, and Section 3.2 as a correction.
- Legacy observation: 2.4.0 decided whether repeated XML children were a sequence one input
  document at a time, as each was folded onto the tree. Three documents each contributing a single
  `<d>` child never presented a repetition to any one of those decisions, so each contribution was
  classified as a scalar, and each overwrote the last. The empty indices are the residue of that
  overwriting rather than a considered result.
- Clean behavior: Section 17.4 computes classification "over the complete destination-level
  contribution set" and does so *before* folding, so the grouping of contributions into batches
  cannot change the answer. Three separate documents contributing `r.c.d` once each are the same
  input as one document contributing it three times, and the fourth value from the third document
  extends the same sequence. `WARN004` is raised once for the sequence path, not once per
  contribution.
- The difference is intentional: a classification that depends on how inputs were divided across
  files makes the merge non-associative, so splitting a configuration in two — a routine, purely
  organizational act — silently changes the output. Section 3.2 lists this among the behaviors the
  rewrite corrects.
