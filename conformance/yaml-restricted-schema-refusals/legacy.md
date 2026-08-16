# Legacy differential

- namespace2xml 2.4.0: **agrees**.
- Contract: Section 10.1's `RestrictedYaml1` schema (mapping-key restriction, no duplicate keys,
  no merge keys, no complex keys, no implicit tags), Section 10.2's rejection of anchors and
  explicit tags, and Section 10.3's rejection of explicit document markers. Section 22 for
  `PARSE001`. Section 3 does not enumerate `RestrictedYaml1` individually.
- Legacy observation: the baseline exits `1` and produces no output tree, so the observed result
  matches this case's expected result (exit code `1`, empty tree). The measurement records no
  divergence and no standard error beyond the banner. An earlier draft of this note asserted
  that 2.4.0 silently accepted anchors, aliases, merge keys, and duplicate keys; that
  assertion was written from reasoning rather than observation and does not match what the
  baseline does on this input set.
- Clean behavior: each construct is `PARSE001` against the source that carries it, at the
  Section 22 one-based line and character column of the offending token, and every failing
  source reports in Section 7.3 command-line order so one run names every bad file.
- Why the observable agreement is not compatibility evidence: this case exists to pin the
  per-source classification and the per-source position of six distinct refusals, and the
  observable — one exit code and one empty tree — is silent about both. The baseline's YAML
  reader raises an exception on at least one of the six inputs (the batch's stderr is empty
  after banner stripping, which is consistent with the exception being caught and mapped to
  exit `1`), and that is enough to end the run before any output is written; whether the
  reader would have refused the other five, silently accepted them, or produced surprising
  output is not readable from this observation. Two of the six inputs — duplicate-key and
  complex-key — are refused by the host YamlDotNet library for its own reasons, so a baseline
  that exits `1` after refusing one of them for a library-internal reason is doing something
  quite different from the clean tool refusing all six under `RestrictedYaml1`. Diagnostic
  members belong to `expected-diagnostics.json` for exactly this reason.
