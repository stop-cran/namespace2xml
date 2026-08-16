# Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Section 8.2 name parts and the `\.` escape; Appendix A.2 name escapes and the
  `\u{HEX}` scalar escape; Section 11.4 canonical XML addressing; Section 17.1 overlay
  creation of an absent path.
- Legacy observation: under this case's own arguments 2.4.0 never reaches its XML writer. It
  cannot lex `\u{2E}` — Appendix A.2's scalar escape does not exist in 2.4.0's namespace grammar —
  so it reports `Error parsing input: unexpected 'r', file: inputs/over.txt, line: 2, column: 1`,
  exits `1`, and writes nothing. A reduced probe that removes the `\u{2E}` line gets one step
  further and then dies on the `@debug` address, with an unhandled
  `System.Xml.XmlException` — "Name cannot begin with the '@' character, hexadecimal value
  0x40" — from `XmlFormatter.ToXmlValueSingle`, after truncating the output file to zero bytes:
  2.4.0 has no address for an attribute, so `@debug` is an ordinary name part it then hands to
  `XName`. That is the same absence of attribute addressing that
  `xml-a-2-x-style-attribute-override-adds-a-sibling-element` records from the other direction,
  and it is not what this case is about.
- On the point this case *is* about, the baseline agrees. A reduced form that avoids the
  attribute — `<r><system.web><compilation>false</compilation></system.web></r>` with the two
  profile lines `r.system\.web.compilation=escaped` and `r.system.web.compilation=unescaped` —
  exits `0` on 2.4.0 and writes `<r>\n  <system.web compilation="escaped" />\n  <system>\n
  <web compilation="unescaped" />\n  </system>\n</r>`. The escaped address reaches the real
  element and the unescaped one builds a parallel `<system><web>` subtree beside it, which is
  exactly what this case asserts of 3.0. The dot hazard is therefore **not** a 3.0 regression;
  it is a property of a dotted namespace addressing a format whose names may contain dots.
  2.4.0 differs in that reduced form only through the neighbouring defect the other XML cases
  document: it collapsed the element child `<compilation>` into an attribute and lost its
  element.
- Clean behavior: Section 8.2 makes `.` the delimiter between name parts and `\.` a literal
  dot inside one part, and Appendix A.2 admits `\u{2E}` as a second spelling of the same
  character. `system\.web` and `system\u{2E}web` therefore denote one name part and one overlay
  node, which is why the two overrides land on the same element and the second adds its
  attribute beside the first. The unescaped `r.system.web.compilation.@debug` names four parts,
  none of which exists, so Section 17.1 creates them; nothing in the specification licenses
  guessing that the author meant the dotted element, and no Section 22 diagnostic covers it.
  Section 11.4's `WARN011` is explicitly confined to an attribute and a namespace-qualified
  element of the same simple alias, and a dotted name is neither. The remedy is the escape, and
  it is documented in `docs/format-xml.md` and `docs/usage-methodology.md`.