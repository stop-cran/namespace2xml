# Legacy differential

- namespace2xml 2.4.0: **fails**. It terminates with an unhandled
  `System.AggregateException` wrapping `System.ArgumentException: Requested value 'mapping'
  was not found.` — thrown from `Enum.Parse` inside
  `Namespace2Xml.Formatters.Extensions.ParseValueType` — and exits 134 on Linux (the
  runtime's SIGABRT convention). No output tree is written. The case expects exit 0, two
  files, and no diagnostics.
- Contract: Section 16.6 `type` recognized values (which lists `mapping` explicitly); §22's
  requirement that every diagnostic carry a stable code, phase, and specification anchor;
  Section 3.2 correction against behaviour "caused by unhandled user-input exceptions".
- Legacy observation: `type=mapping` did not exist in 2.4.0. The baseline recognized only
  a subset of the §16.6 vocabulary and reached its enum parser with the string `"mapping"`
  unvalidated, so an ordinary `Enum.Parse` failure — the same one an unknown flag would
  produce — became the process's termination reason. The CLI had no top-level handler for
  the resulting `AggregateException`, so the exception propagated through the pipeline
  entrypoint and the .NET runtime aborted the process. This is the same class of defect
  as `xml-sequence-attribute-projection-is-type001` in this corpus, which crashes on the
  string `"attribute"` for the same reason.
- Clean behavior: §16.6 lists `mapping` among the recognized values and describes it as
  "the explicit escape hatch for preserving numeric keys as mapping keys rather than
  projecting them as an array". The `first` selector's `data.type=mapping` directive
  therefore keeps `data.2=x` / `data.7=y` as-is, while `second`'s `data` — an inferred
  sequence under §8.7 — renders with fresh dense indices `0` and `1`. Both writers produce
  their expected files and exit 0. The fixture also covers §22's per-instance suppression
  of `WARN010`: the mapping-inferred sequence at `first.data` is projected as a mapping in
  its own output, so no `WARN010` fires for that instance, while `second.data`'s inferred
  sequence remains sequential in its own output and does raise it — the observation the
  fixture's title names.
- The difference is intentional: an unrecognized scheme value that ought to raise `SCHEME001`
  and abort planning cannot be allowed to abort the process, because an automated caller
  reading the exit code and standard error cannot tell an unhandled-exception exit apart
  from a runtime crash. §3.2 lists "caused by unhandled user-input exceptions" among the
  behaviours the replacement must not preserve, and this crash is exactly the shape that
  bullet names.
