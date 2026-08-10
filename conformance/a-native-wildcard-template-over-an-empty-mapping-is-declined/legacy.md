# Legacy differential

- namespace2xml 2.4.0: **differs**. It exits `0` and writes `a.yaml` containing `'*': {}` — the
  wildcard key emitted verbatim as literal data. This case expects exit `70` and no output.
- Contract: Section 10.4. "Extraction is entry-by-entry", and an extracted entry names one scalar.
- An empty mapping has no scalar leaf, so entry-by-entry extraction yields nothing. What the author
  wrote is not a value but a **shape**: "every match gains an empty mapping here". Section 10.4's
  own carrier rule points the other way — "carrier ancestors created only to contain an extracted
  template do not contribute mapping-presence marks" — so a template is not a vehicle for mapping
  presence, and there is no entry to carry this one. Refusing is narrower than inventing a
  mapping-presence template that Section 10.4 does not describe.
- 2.4.0's answer is a third thing again: it treated `*` as an ordinary key, so the template leaked
  into the output as data. Read back through its own reader that key is a rule, which is the same
  round-trip break recorded in
  `a-backslash-asterisk-in-a-native-key-is-a-literal-asterisk`.
- Once Section 10.4 settles the shape, this fixture changes at that commit. Recorded in
  `KNOWN-LIMITS.md` section 1.2.