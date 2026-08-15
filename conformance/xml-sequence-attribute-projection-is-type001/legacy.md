# Legacy differential

- namespace2xml 2.4.0: **fails**. It terminates with an unhandled
  `System.ArgumentException: Requested value 'attribute' was not found.` from `Enum.Parse`
  inside `Namespace2Xml.Formatters.Extensions.ParseValueType`, and exits 134 on Linux (the
  runtime's SIGABRT convention). No output tree is written. The case expects exit 1 with
  one `TYPE001` diagnostic naming the path `tag` and the destination `cfg.xml`.
- Contract: Section 16.6 `type` recognized values (which lists `attribute` explicitly, but
  did not in 2.4.0); §19.5's rule that `attribute` "is `TYPE001` because one XML attribute
  cannot represent repeated values"; §22's requirement that every diagnostic carry a
  stable code, phase, and specification anchor; §3.2 correction against behaviour "caused
  by unhandled user-input exceptions".
- Legacy observation: this is the same defect the `type-mapping-suppresses-warn010-per-output-instance`
  fixture crashes on, with a different string. `type=attribute` was not a value 2.4.0's
  enum parser recognized; the string reached `Enum.Parse` unvalidated and threw
  `ArgumentException`, which the CLI did not catch. `type=attribute` **is** an XML-specific
  value in 3.0, and it is legal on a scalar. What this case exercises is applying it to a
  *sequence* — the JSON `["v1","v2"]` at `cfg.tag` — which §19.5 refuses with `TYPE001`.
  The baseline never gets that far: the enum parser fails before the sequence is even
  inspected, so the correction here is layered. The unhandled-exception defect must be
  fixed first before the specific `TYPE001` refusal can be observed at all.
- Clean behavior: §19.5 states that "at a sequence path ... `attribute` is `TYPE001`
  because one XML attribute cannot represent repeated values". §22 counts `TYPE001` once
  per path and applicable output instance, so exactly one diagnostic is emitted at path
  `tag` for destination `cfg.xml` with anchor `§19.5`. §21.2's global validation gate
  aborts publication, no `cfg.xml` is written, and the run exits 1.
- The difference is intentional: an unrecognized scheme value that ought to raise
  `SCHEME001`, and a legal scheme value applied to a shape it does not support that ought
  to raise `TYPE001`, are both blocking scheme conditions the 3.0 tool must catch
  structurally. Neither can be allowed to abort the process, because an automated caller
  reading the exit code and standard error cannot tell an unhandled-exception exit apart
  from a runtime crash, and the specific defect the fixture pins is invisible in a run
  that never reaches the check.
