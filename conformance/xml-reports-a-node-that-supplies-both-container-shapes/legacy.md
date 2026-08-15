# Legacy differential

- namespace2xml 2.4.0: **fails**. It aborts with an unhandled `System.Xml.XmlException`, "Name
  cannot begin with the '0' character", leaves a zero-byte `cfg.xml` behind, and exits
  `-532462766`. **verified** — measured three times against the Appendix C.6 pinned 2.4.0 package,
  identical each time.
- Contract: Section 17.1's "a destination requiring one container shape uses the later container
  contribution and warns", and Section 19.5's statement that XML is such a destination.
- Legacy observation: 2.4.0 has no sequence model. A JSON array became a mapping whose keys are the
  decimal indices, so the shape conflict this case is about could not arise — and the index `0`
  then reached `XName` as an element name, which XML does not admit. The failure is a crash rather
  than a diagnostic, and the empty file it leaves is indistinguishable from a successful run that
  wrote nothing.
- Clean behavior: the node keeps both container projections in the overlay, XML renders the later
  one, and the loss is reported as `TYPE002` naming the path and the destination. Exit is 0,
  because Section 17.1 makes this a defined resolution rather than an error.
- Both directions are pinned here on purpose. `cfg.a` receives the mapping first and the sequence
  second, so the sequence wins and the mapping children are dropped; `cfg.b` receives them the
  other way round, so the mapping wins and the sequence items are dropped. A fixture carrying only
  one of the two exercises one branch of the resolution and reports nothing about the other, and
  the two diagnostics differ in exactly the clause naming what was lost.
- Why XML needed saying at all: Section 16.4 named namespace, quoted-namespace and INI, and Section
  4.4 covered the JSON and YAML payload contest. XML was named nowhere, and it was the one
  destination that dropped a container silently.