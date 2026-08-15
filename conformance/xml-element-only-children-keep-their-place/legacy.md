# Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Section 3.2 correction against unhandled user-input exceptions, together with
  Section 11.4's rules that "for element-only repeated children ... the canonical child paths
  are `a.b.0`, `a.b.1` ... using the `a.b` sequence path's own high-water allocator" and that
  "implementations must not silently retarget `a.b` to the first repeated child".
- Legacy observation: the baseline exits with an unhandled-exception status
  (`System.Xml.XmlException: Name cannot begin with the '1' character, hexadecimal value 0x31.`,
  exit `-532462766`) and writes different bytes at `r.xml` before failing. The measurement
  records `exit -532462766 (expected 0); content r.xml` with that stderr.
- Clean behavior: the two `<b>` element-only siblings form a sequence at `r.b` with ordering
  values `0` and `1`; `<c>` sits between them at its own content-token position. The
  namespace-supplied `r.b.1=30` patches the sequence item at ordering value `1`, so
  `<r><b>1</b><c>2</c><b>30</b></r>` is emitted with `<c>` retaining its place between the
  two `<b>` children.
- Why the difference is intentional: 2.4.0 has no separate high-water allocator for the
  repeated-child sequence and no rule preventing a numeric qualified-name part from being
  written straight into an XML element name, so the ordering-value component `1` reaches the
  XML writer as an element name and the writer refuses it. Producing a partial `r.xml`
  before crashing also violates Section 15.4's rule that transformation and planning errors
  never produce a partial output instance for a later phase. Both symptoms are the
  unhandled-exception class Section 3.2 removes.
