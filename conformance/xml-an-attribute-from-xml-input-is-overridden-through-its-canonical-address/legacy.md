# Legacy differential

- namespace2xml 2.4.0: **fails**. It terminates with
  `System.Xml.XmlException: Name cannot begin with the '@' character, hexadecimal value 0x40.`
  from `Namespace2Xml.Formatters.XmlFormatter.ToXmlValueSingle`, and exits 134 on Linux. A
  zero-length `r.xml` is left behind. The case expects exit `0` and `r.xml` containing
  `<a x="dev">` with its `<keep>1</keep>` child intact.
- Contract: Section 11.4 canonical XML addressing (`@x` names the attribute); Section 5.2
  mapping order after override; Section 19.5 XML rendering.
- Legacy observation: 2.4.0 had no typed-component vocabulary, so `@x` was taken as an ordinary
  name part and reached the XML writer as an element name. `XmlConvert.VerifyNCName` rejects a
  name beginning with `@` and the process aborted before any value was written. The rule this
  case exists to pin — that a profile can override an attribute **that came from an XML input**
  by naming it canonically, leaving the element's children untouched — is invisible under that
  crash, because the baseline has no spelling for it at all.
- Clean behavior: Section 11.4's `@x` names the attribute component outright. The contribution
  overrides the attribute value read from `main.xml`, and the element child `<keep>1</keep>` is
  untouched, because a contribution to `r.a.@x` is not a contribution to `r.a.keep`. No
  diagnostic is owed: this is an ordinary override at an ordinary path.
- The difference is intentional: `@` is reserved by Section 11.4 as the attribute marker, and
  Section 8.2 makes an escaped `\@` the way to write a literal. 2.4.0 reserved nothing and
  crashed on the character. This is the counterpart of
  `xml-a-2-x-style-attribute-override-adds-a-sibling-element`, which pins what the old spelling
  does now; together they are the whole of the migration story for an attribute override.
