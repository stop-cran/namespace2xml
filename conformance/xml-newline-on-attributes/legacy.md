# Legacy differential

- namespace2xml 2.4.0: **fails**. It reports `Writing output .../cfg.xml xml...`, then exits with
  an unhandled `System.Xml.XmlException: Name cannot begin with the '@' character, hexadecimal value
  0x40` thrown from `XmlFormatter.ToXml`, leaving a zero-byte `cfg.xml` behind. **verified** —
  measured against the Appendix C.6 pinned 2.4.0 package.
- Contract: Section 16.9's `NewLineOnAttributes` layout, and Section 11.4's `@` canonical address
  for an XML attribute. Section 3.2 as a correction of an unhandled exception on valid input.
- Legacy observation: 2.4.0 had no concept of an attribute marker and no `xmloutputoptions`
  directive. `cfg.e.@a` was therefore an ordinary name part whose text begins with `@`, which is not
  a legal XML name, and the formatter handed it to `XmlWriter` without checking. The failure is an
  unhandled exception rather than a diagnostic, and it happens *after* the output stream is opened,
  so the run also leaves a truncated file where a reader expects a document. There was no way to ask
  2.4.0 for an attribute at all, so the layout this case pins had nothing to apply to.
- Clean behavior: Section 11.4 makes `@a` the canonical address of the attribute `a`, so
  `cfg.e.@a=1` contributes an attribute rather than a child element. Section 16.9 states that
  `NewLineOnAttributes` "places every attribute on its own line, including the first, indented two
  spaces beyond the owning start tag; a start tag that carries attributes therefore ends after the
  element name and its `>` follows the last attribute", and that `Indent` "uses two ASCII spaces per
  element nesting level outside mixed content". `<e` and `<g` are one level inside `<cfg>` and so
  begin at two spaces; their attributes are two beyond that, at four; the element children `<t>` are
  also at four as the second nesting level. The omitted `Declaration` and `PreserveCData` groups
  fall back to their documented defaults, so the declaration is written.
- The difference is intentional: Section 6.3 forbids a user-caused error escaping "only as an
  unhandled exception", and an input naming an attribute is user-caused however the tool chooses to
  spell it. A crash that has already opened the destination is the worst available outcome, because
  the zero-byte file it leaves is a plausible-looking artifact that no later step can distinguish
  from a document the user asked for.