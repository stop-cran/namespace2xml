# Legacy differential

- namespace2xml 2.4.0: **fails**. It terminates with the same `System.Xml.XmlException:
  Name cannot begin with the '0' character, hexadecimal value 0x30.` from
  `Namespace2Xml.Formatters.XmlFormatter.ToXmlValueSingle` that
  `xml-sequence-projection-covers-mapping-children-scalars-records-and-root` documents, and
  exits 134 on Linux. A zero-length `r.xml` is left behind. The case expects exit 1 with
  one `WARN004` naming the concatenation at `r.b` and one blocking `REFERENCE005` naming
  the reference at `r.ref`.
- Contract: Section 11.4 canonical XML addressing — "singleton-to-sequence promotion changes
  the canonical child address. A singleton `<b>` is addressed as `a.b`; after the merged
  model contains repeated `<b>` children, their canonical paths are `a.b.<ordering-value>`
  and the former singleton path no longer names a scalar or element"; §13.3 "non-scalar
  references"; §8.7 native implicit sequence concatenation. §3.2 corrections against
  behaviour "caused by unhandled user-input exceptions" and against silent retargeting
  of a promoted singleton.
- Legacy observation: this is the same underlying defect that
  `xml-sequence-projection-covers-mapping-children-scalars-records-and-root` crashes on.
  The overlay of `base.xml` and `overlay.xml` produces three `<b>` children under `<r>`
  — one contributing `ONE`, one `TWO`, one `THREE` — and 2.4.0's XML writer walked the
  resulting sequence directly into `ToXmlValueSingle` with the first item's ordering
  value `0` as the element name string. The reference `${r.b}` was never evaluated
  because the process aborted before reference resolution ran; whatever 2.4.0 might have
  done with the promoted-singleton reference is invisible under this crash.
- Clean behavior: §11.4's "singleton-to-sequence promotion changes the canonical child
  address" turns `r.b`'s three contributions into `r.b.0`, `r.b.1`, and `r.b.2`; the
  path `r.b` no longer names a scalar or element. §8.7 emits one `WARN004` for the
  implicit-sequence concatenation. §13.3 then resolves `${r.b}` and fails: the reference
  addresses a former singleton whose canonical spelling is now a sequence, and §13.3
  states that "mapping, sequence, XML element, comment, and other structured-node
  references are unsupported and are blocking reference errors". One `REFERENCE005` is
  emitted at path `r.ref` under §22's "once per reachable owning value" cardinality, and
  the run exits 1 with no output tree.
- The difference is intentional: an implementation that writes a sequence ordering value
  as an XML element name produces illegal XML and cannot even reach the reference-
  resolution defect this fixture pins. Once the sequence is spelled correctly with
  repeated `<b>` elements at the parent's ordinary positions, the reference-resolution
  rule §13.3 makes explicit — that a promoted singleton is no longer a scalar-payload
  target — becomes the observable, and the case's `REFERENCE005` at `r.ref` fails
  the run in the honest way §3.2 requires: with a diagnostic carrying a code, a phase,
  and a specification anchor rather than with a process termination.
