# Legacy differential

- namespace2xml 2.4.0: **differs**. It exits 0 rather than the expected 1 and writes `cfg.xml`
  containing `<?xml version="1.0" encoding="utf-8"?>` on one line then `<cfg a="1" />` on the
  next. Two independent divergences: the run succeeds where the case says it must be rejected,
  and the scalar `cfg.a=1` is rendered as an XML *attribute* on the root element rather than as
  a child element.
- Contract: Section 16.9 output-options rules and the `SCHEME001` cardinality of Section 22.
  Section 3.2 lists behaviour "caused by unhandled user-input exceptions" among the corrections;
  the correction here is the narrower one that a contradictory flag set is a reportable scheme
  error rather than silently accepted or silently ignored.
- Legacy observation: the entire output-options concept — the `xmloutputoptions`,
  `jsonoutputoptions`, `yamloutputoptions`, and `inioutputoptions` directives from §16.9 —
  did not exist in 2.4.0. An unrecognized scheme directive was ignored rather than reported, so
  the `cfg.xmloutputoptions=Indent,NoIndent` line contributed nothing at all and its
  contradictory content was never inspected. The default XML rendering the baseline then chose
  spelled a scalar mapping child as an attribute of the root element, which is a shape choice
  the 3.0 XML writer does not repeat.
- Clean behavior: §16.9 states that "naming both flags of a contradictory pair in one
  declaration is `SCHEME001`", and lists `Indent` and `NoIndent` as one of the three XML
  contradictory pairs. Section 22 counts `SCHEME001` "once per declaration", so exactly one
  error is emitted and the run exits 1 with no output tree.
- The difference is intentional: a directive whose value contradicts itself is one of two things
  and cannot be both at once, and an implementation that silently accepts one of the readings
  hides an authoring mistake in production. The 3.0 refusal fails at scheme-loading time before
  any input is opened, so no output is written at all — which is also what §21.2's global
  validation gate requires when a blocking scheme error stands.
