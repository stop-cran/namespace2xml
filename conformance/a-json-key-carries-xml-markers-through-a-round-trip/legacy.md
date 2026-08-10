# Legacy differential

- namespace2xml 2.4.0: **crashes**. `System.Xml.XmlException: Name cannot begin with the '@'
  character, hexadecimal value 0x40.` from
  `Namespace2Xml.Formatters.XmlFormatter.ToXmlValueSingle`, exit `-532462766` on Windows and
  134 on Linux. A zero-length `a.xml` is left behind and `c.json` is never written. The case
  expects exit 0, `a.xml` containing `<a x="1"><b>t</b></a>`, and `c.json` containing the
  members `"@y"` and `"d"`.
- Contract: Section 9.1 marker-carrying native keys (a JSON key beginning with an unescaped
  `@` is the attribute component that marker introduces) and Section 19.3 mapping-key
  spelling (an attribute component is written back as `@y`). §3.2 correction against
  behaviour "caused by unhandled user-input exceptions".
- Legacy observation: 2.4.0 has one name model with no typed components, so the JSON key
  `@x` became an ordinary path part named `@x`. The XML writer then asked
  `System.Xml.Linq.XName` for an element called `@x` and `XmlConvert.VerifyNCName` threw,
  because `@` is not a valid NCName start character. The crash is unconditional: there is no
  input under which 2.4.0 turns a JSON key into an XML attribute, so the round trip this
  case pins is not merely wrong there — it is unreachable. The same run also never reaches
  the `c.json` destination, so 2.4.0's spelling of an attribute on the JSON side is
  unobservable here too.
- Clean behavior: §9.1 gives a native key "the Section 11.4 markers", so `@x` from
  `read.json` is the attribute component `x` under `a` and reaches the XML writer as an
  attribute rather than as an element name. §19.3 is the mirror: the attribute component
  `y` contributed by `write.properties` as `c.@y` is written to `c.json` as the key `@y`.
  The two halves are one rule read in each direction, which is what makes a document this
  tool writes readable by it.
- The difference is intentional: 2.4.0's JSON reader could not express an XML attribute at
  all, so its JSON output was a lossy projection that its own XML writer rejected. Section
  9.1 closes that by giving the reader the same vocabulary the writer already used.
