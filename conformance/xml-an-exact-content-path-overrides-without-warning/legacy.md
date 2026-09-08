# Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Section 11.4 canonical XML addressing; Section 11.7 `PreserveWhitespace`; Section
  19.5 XML rendering.
- Legacy observation: 2.4.0 accepts the inputs, then exits nonzero with an unhandled
  `System.Xml.XmlException` while rendering: `Name cannot begin with the '#' character`. It writes
  no completed `server.xml`.
- Clean behavior: `server.#1.host` is the canonical address of the element at content position
  `#1`; the override replaces that element, preserves the surrounding formatting nodes, emits no
  `WARN011`, and writes `server.xml`.
- The difference is intentional: content-token paths must be usable as model addresses rather than
  leaking into the XML writer as literal element names.
