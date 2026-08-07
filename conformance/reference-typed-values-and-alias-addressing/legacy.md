# Legacy differential

- namespace2xml 2.4.0: **partly matches**. One-level scalar references existed and were not subtree
  copies, but the resolution rule was never stated, the scalar kind of a forwarded value was not
  defined, and XML attributes had no reference spelling at all.
- Contract: Section 13.1 resolution and aliases; Section 13.2 kind preservation and concatenation;
  Section 26 item 9.
- Legacy observation: legacy items 100, 105, 106, 109 and 110 — exact canonical resolution rather
  than hierarchical or prefix lookup, the referenced scalar kind preserved when the whole value is
  one reference, mixed literal/reference values as strings, canonical XML components, and simple
  format-agnostic aliases.
- Clean behavior: `${app.database.port}` forwards the referent's settled kind, so an integer stays
  an integer rather than becoming the text of one. `${app.database.@host}` addresses an XML
  attribute canonically. `${app.database.host}` addresses the same attribute through its
  format-agnostic alias, which is admissible only because `host` is unique at that node. A value
  mixing literals and references is a string by Section 13.2 regardless of what it concatenates.
- Why this case exists: the four addressing forms and the kind rule are the whole of the reference
  contract on correct input, and none of them was observable in 2.4.0 output.
- How the case proves it: `port` and `endpoint` both derive from `app.database.port`, so an
  implementation that stringified the referent early would still render `8443` in both and pass —
  which is why the fixture also pins `attribute`, reachable *only* through the alias index, and
  `endpoint`, whose concatenation must not re-infer a kind from `https://example.org:8443`.

## Not asserted

- Which kind a forwarded value carries in a format that renders kinds distinguishably. The two flat
  serializers this fixture can use render a forwarded integer and its text identically; Section
  13.2 kind preservation is pinned by unit tests against the resolver instead.
