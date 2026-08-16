# Legacy differential

- namespace2xml 2.4.0: **differs**. It exits 0 and writes `json.json` containing
  `{`, `  "b": "hello"`, `}` on three lines.
- Contract: Section 12.1's exclusion of `output` from capture substitution, Section 16.1's
  closed format list, and Section 22's `SCHEME001` cardinality of once per declaration.
- Legacy observation: 2.4.0 substituted the capture into the `output` value. The selector
  `cfg.*` matched `cfg.json`, the capture text `json` was spliced into the value, and
  `output=json` was the result — a valid format name reached entirely by accident of the input
  path's spelling. The instance was then named after the same capture, so the destination is
  `json.json`. Had the profile said `cfg.foo.b=hello` instead, the identical scheme line would
  have produced `output=foo`, which names no format at all.
- Clean behavior: capture recognition is disabled in an `output` value whatever the selector
  defines, so `*` is literal text. It falls to the ordinary Section 16.1 value check, which
  rejects it as `SCHEME001` in the scheme phase at the line the declaration was written on, and
  the run exits 1 having written nothing.
- The difference is intentional, and `output` is the sharper of the two cases in Section 12.1
  because `output` creates the output instance rather than binding to one. The Section 14.1
  expansion that supplies every other instance-scoped directive's captures runs *after* the
  instances exist, so there is no tuple to substitute from at the point the value is read. A
  build that substitutes anyway is reading captures that the pipeline has not bound yet, and the
  legacy result above shows what that produces: a destination and a format both chosen by the
  data rather than by the scheme.
- Section 12.1 also fixes that `cfg.*.output=*` and `cfg.output=*` are the same error, because
  the exclusion belongs to the directive and not to the declaration.