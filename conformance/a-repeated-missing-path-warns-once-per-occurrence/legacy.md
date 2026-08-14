# Legacy differential

- namespace2xml 2.4.0: **agrees**.
- Contract: Section 7.2's rule that "each `-i` or `-s` token that names a path which does not exist
  emits its own warning, so a path written twice warns twice", and Section 22's `WARN001`
  cardinality of "once per missing-file occurrence on the command line". Section 22 adds that where
  a cardinality is stated per source "the unit is one `-i`, `-s`, or `-v` occurrence and not one
  distinct path", because "the displaced occurrence can differ in `phase`".
- Legacy observation: the baseline emits `File ... not found` four times -- twice for the repeated
  input occurrence, once for the shared path as an input and once for it as a scheme -- writes
  `k=1`, and exits 0.
- Clean behavior: the run reports four `WARN001` occurrences -- one scheme-phase and three
  input-phase -- and exits 0, writing `k=1`.
- Why the difference is intentional: there is none to justify here, and that is worth recording.
  The baseline counts these warnings per occurrence, which is what Section 7.2 now requires; it was
  the clean implementation that collapsed them, keying the cardinality slot on the path text alone.
  The collapse was invisible in the ordinary case, where two identical occurrences produce two
  identical warnings, and damaging in the case this fixture also pins: the same path supplied once
  as an input and once as a scheme fails in both phases, and a path-keyed slot reported only the
  scheme, leaving a missing input file unmentioned while appearing to have reported it. The
  baseline reaching the right count is not evidence that it reasoned about phases: it warns from
  the read site with no cardinality mechanism at all, so it cannot collapse anything. Here that
  costs it nothing.
