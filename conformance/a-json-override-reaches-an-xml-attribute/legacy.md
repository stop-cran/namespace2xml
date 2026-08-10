# Legacy differential

- namespace2xml 2.4.0: **crashes**. `System.Xml.XmlException: Name cannot begin with the '@'
  character, hexadecimal value 0x40.` from
  `Namespace2Xml.Formatters.XmlFormatter.ToXmlValueSingle`, exit `-532462766` on Windows and
  134 on Linux, leaving a zero-length `a.xml`. The case expects exit 0 and `a.xml` containing
  `<a port="80" domain="example-dev.com"><b>keep</b></a>`.
- Contract: Section 9.1 marker-carrying native keys, Section 4.4 ordered override, and
  Section 5.2 mapping order after override ("Overriding a mapping key moves that exact key
  … to the winning contribution's position mark"). §3.2 correction against behaviour
  "caused by unhandled user-input exceptions".
- Legacy observation: 2.4.0 read `<a domain="…" port="…">` into paths `a.domain` and
  `a.port`, and read the JSON key `@domain` into a third, unrelated path `a.@domain`. The
  override therefore did not happen at all — the two never met — and the extra path went to
  the XML writer as an element name, where `XmlConvert.VerifyNCName` threw. Both halves of
  this case are invisible under that crash: neither the override nor the reordering it
  causes is observable, and no output file survives to inspect.
- Clean behavior: §9.1 makes the JSON key `@domain` the same attribute component the XML
  parser produced, so the two contributions address one logical path and §4.4's ordered
  override selects the later one. §5.2 then moves that key to the winning contribution's
  position mark, which is why `port` precedes `domain` in the output even though the base
  document declared `domain` first — the override does not edit in place, it re-places. This
  is the scenario the format exists for: a large XML base specialized by a short list of
  environment overrides written in whatever format is convenient.
- The difference is intentional: an attribute that no other format can name is an attribute
  that cannot be overridden, and §4.4's guarantee is that any value can be. Reaching an XML
  attribute from JSON is the point, not an incidental consequence.
