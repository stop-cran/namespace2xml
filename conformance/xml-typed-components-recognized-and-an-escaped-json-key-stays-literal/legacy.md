# Legacy differential

- namespace2xml 2.4.0: **crashes**. It terminates with the same `System.Xml.XmlException:
  Name cannot begin with the '0' character, hexadecimal value 0x30.` from
  `Namespace2Xml.Formatters.XmlFormatter.ToXmlValueSingle` that the two neighbouring XML
  crash fixtures document, and exits 134 on Linux. A zero-length `r.xml` is left behind.
  The case expects exit 0, `r.xml` containing the merged `<r>` with both attributes
  (`nsattr="ATTRVAL"` and `varattr="VAR_VAL"`) and three child elements (`<nsattr>`,
  `<body>`, `<ref>` with the resolved reference `ATTRVAL`), and `lit.properties` containing
  the escaped-marker key `\@key=literalval`.
- Contract: Section 11.4 canonical XML addressing (typed marker components `@`, `#n`, and
  `Q{...}` recognized in namespace input) and Section 8.2 marker recognition (an escaped
  `\@` is an ordinary literal name part). Section 19.1 record-leading escape rules for
  ordinary components with typed-marker text. §3.2 correction against behaviour "caused
  by unhandled user-input exceptions".
- Legacy observation: this is the same underlying defect the two preceding XML crash
  fixtures document. 2.4.0's sequence path through the XML writer reached `ToXmlValueSingle`
  with an integer element-name string and threw. The specific rules the case exists to pin —
  that `r.@nsattr` and `r.varattr` (the CLI variable) are typed attribute components
  reaching the XML writer as one attribute-name space, that `r.nsattr` (no `@`) is a
  child element sharing the local name with the attribute rather than colliding with it
  under the alias index, that `r.ref` resolves through the alias index to the attribute's
  scalar `ATTRVAL`, and that on the JSON side the escaped key `\@key` reaches the namespace
  writer as an ordinary component — are all invisible under this crash. The process aborted
  before any of the four values reached a writer.
- Clean behavior: §11.4's marker components govern the XML side. `r.@nsattr` and the CLI
  variable `r.@varattr` are canonical attribute components under `<r>`; §11.4's
  scalarization rule ("an attribute owns its string scalar at its attribute path") makes
  them each scalar-payload-bearing at their attribute paths, so `${r.@nsattr}` resolves
  canonically to `ATTRVAL`. `r.nsattr` (no `@`) is a child-element component sharing a
  local name with the attribute; the two are distinct canonical paths but share a simple
  alias, and the alias-competition rule §11.4 states — "an XML attribute and unqualified
  child element both named `x` make `${a.x}` ambiguous; `${a.@x}` selects the attribute"
  — is what makes this reference unambiguously canonical. `root=Q{}r` is the explicit
  spelling that pins the child-element resolution rather than the alias — the acceptance
  clause the fixture's title names. On the JSON side, §9.1's rule that "a backslash at the
  start of the key escapes a following `@`, `#`, `Q`, or `\`, contributing that character
  literally and suppressing marker recognition for the whole part" makes the written key
  `\@key` an ordinary component whose namespace-output spelling under §19.1 is `\@key`
  again — the escape survives a format crossing in both directions.
- The difference is intentional: an implementation that crashes on the sequence numeric
  index cannot be run at all, so no other §11.4 or §9.1 rule is observable against it —
  including the four independent rules this fixture pins together. Fixing the XML writer
  to emit repeated named elements is necessary before the marker vocabulary this case
  exercises can be evidenced. As with the two neighbouring crashes, §3.2 lists the
  unhandled-exception class among the corrections and this case is the shape it names.
