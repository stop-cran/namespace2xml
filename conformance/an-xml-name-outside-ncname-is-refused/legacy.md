# Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Section 11.2's requirement that an element or attribute name emitted as XML "must
  match the `NCName` production of Namespaces in XML 1.0, Third Edition", that a component which
  does not match "is `XML002` at the point the name would be written, and only there", and the
  reason the specification selects `NCName` over `Name`: a component written `a:b` "would be
  emitted as `<a:b>` and read back as the local name `b` in whatever namespace the prefix `a` was
  bound to".
- Legacy observation: the baseline exits `-532462766` with an unhandled
  `KeyNotFoundException` from `XmlFormatter.ToXmlValueSingle` — "The given key 'a' was not present
  in the dictionary" — and leaves a zero-byte `cfg.xml` behind.
- Clean behavior: the run reports `XML002` at `a:b`, exits 1, and writes nothing.
- Why the difference is intentional: the exception is the ambiguity Section 11.2 legislates,
  reached from the other side. 2.4.0 splits the component on the colon and looks the prefix `a`
  up among the declared namespaces; this model declares none, so the lookup throws. A component
  whose text merely happens to contain a colon is therefore reinterpreted as a prefixed name
  without being asked, and the run ends on a dictionary miss naming a key the user never wrote.
  The crash is not incidental to the divergence — it is evidence that the two readings of `a:b`
  are both live in the baseline, which is precisely why 3.0 refuses to write the name at all.