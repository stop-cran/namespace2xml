# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes the same JSON text and exits 0, but ends `cfg.json`
  at `}` with no final newline, and uses `Environment.NewLine` for the line breaks, so the file is
  CRLF on Windows and LF elsewhere. Section 24 requires LF and a final newline on every platform,
  so the tree differs on every run and on every operating system.
- Contract: Section 8.7 numeric-mapping inference; Section 22's `WARN010` row, whose cardinality
  is "once per source contribution, canonical mapping path, and output instance"; Section 3.2's
  requirement that a silent shape change be reported rather than left silent; Section 24's byte
  identity across platforms.
- Legacy observation: 2.4.0 performed the same inference and said nothing about it. Two JSON
  documents each wrote `cfg.a` as a mapping — `{"0": "x"}` and `{"1": "y"}` — and both got a JSON
  array back, with the keys they had written gone from the result. The baseline had no diagnostic
  for this and no way to ask for one; the only evidence that a shape had changed was a reader
  noticing it in the output. The byte-level divergence is the same `Environment.NewLine` and
  missing-final-newline pair that `json-output-options-and-escaping` and
  `json-and-yaml-render-one-exclusive-shape` record, and it is unrelated to what this case is for.
- Clean behavior: the JSON text is unchanged, and that is the point of the case. What the
  replacement adds is on standard error: one `WARN010` per *source contribution*, so
  `inputs/one.json` and `inputs/two.json` are each named. A single warning at `cfg.a` would tell an
  operator that some document had its keys discarded without saying which to go and edit, and the
  two documents here are indistinguishable in the output — both contribute one element to the same
  array. The case also pins two exclusions the cardinality implies but does not spell out:
  `inputs/three.properties` contributes `cfg.a.2=z` to the same inferred sequence and raises
  nothing, because namespace syntax makes no shape claim for a numeric path segment to contradict;
  and `cfg.b` is a native JSON array, which is a sequence because it was written as one, so no
  inference occurred at it.
- The difference is intentional: the exit code stays 0 and the JSON text is unchanged, because
  Section 8.7 inference is the specified behavior and `WARN010` is a warning about it, not a
  rejection of it. A migrating user sees the same content and gains the ability to find out, from
  a stable machine-readable code, which of their documents will lose keys on the way through.
- This verdict was first written as `agrees`, on a Windows comparison made by reading the two
  files as text rather than as bytes. Rendering hides both a `\r` and a missing final newline, so
  the check could not have failed. The differential lane caught it on Linux, where the same
  divergence is one byte. Recorded because the mistake is cheap to repeat and invisible to the
  eye: a legacy verdict is a claim about bytes and has to be measured as one.