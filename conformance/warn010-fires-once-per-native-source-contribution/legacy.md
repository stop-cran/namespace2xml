# Legacy differential

- namespace2xml 2.4.0: **agrees**. It writes the same `cfg.json` and exits 0. The verdict is
  about the output tree and the exit code, and on both this case is indistinguishable between
  the baseline and the replacement.
- Contract: Section 8.7 numeric-mapping inference; Section 22's `WARN010` row, whose cardinality
  is "once per source contribution, canonical mapping path, and output instance"; Section 3.2's
  requirement that a silent shape change be reported rather than preserved silently.
- Legacy observation: 2.4.0 performed the same inference and said nothing about it. Two JSON
  documents each wrote `cfg.a` as a mapping — `{"0": "x"}` and `{"1": "y"}` — and both got a
  JSON array back, with the keys they had written gone from the result. The baseline had no
  diagnostic for this and no way to ask for one; the only evidence that a shape had changed was
  a reader noticing it in the output.
- Clean behavior: the tree is identical, and that is the point of the case. What the replacement
  adds is on standard error: one `WARN010` per *source contribution*, so `inputs/one.json` and
  `inputs/two.json` are each named. A single warning at `cfg.a` would tell an operator that some
  document had its keys discarded without saying which to go and edit, and the two documents here
  are indistinguishable in the output — both contribute one element to the same array.
  The case also pins two exclusions the cardinality implies but does not spell out:
  `inputs/three.properties` contributes `cfg.a.2=z` to the same inferred sequence and raises
  nothing, because namespace syntax makes no shape claim for a numeric path segment to
  contradict; and `cfg.b` is a native JSON array, which is a sequence because it was written as
  one, so no inference occurred at it.
- The difference is intentional: the exit code stays 0 and the tree is unchanged, because
  Section 8.7 inference is the specified behavior and `WARN010` is a warning about it, not a
  rejection of it. A migrating user sees the same files and gains the ability to find out, from
  a stable machine-readable code, which of their documents will lose keys on the way through.
