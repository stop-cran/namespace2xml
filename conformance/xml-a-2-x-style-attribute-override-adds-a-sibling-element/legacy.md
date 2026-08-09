# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 11.4 canonical XML addressing; Section 8.2 alias-index scope; Section 5.2
  mapping order after override; Section 19.5 XML rendering.
- Legacy observation: 2.4.0 exits `0` and writes
  `<r>\n  <a keep="" x="dev" />\n</r>`. Two independent things happen in that one file. The
  profile line `r.a.x=dev` **overrode the XML attribute** — 2.4.0 had no address for an
  attribute distinct from a child element, so `a.x` named the attribute and replaced it. At the
  same time the element child `<keep>1</keep>` was collapsed into an empty attribute `keep=""`
  and its text was lost, which is the scalar-children-become-attributes defect the neighbouring
  XML cases document. The baseline therefore gets the override the user intended and destroys
  the rest of the document doing it.
- Clean behavior: Section 11.4 gives an attribute the canonical address `@x`, and the last
  paragraph of that section makes an unmarked `x` the same component as `Q{}x` — the child
  element. Section 8.2 scopes the simple alias index to references (Section 13.1) and scheme
  paths (Section 15.2) and **not** to data contributions, so `r.a.x=dev` does not resolve
  through it. The contribution names the child element, the attribute `@x` keeps its value
  `base`, and the two coexist. Section 5.2 places the new `x` after `keep`, because `keep`
  carries the earlier position mark. `<keep>1</keep>` keeps its text.
- The difference is intentional, and it is the one migration hazard in this area that is
  **silent**: the run exits `0` with an empty diagnostic stream, and a 2.x profile that
  specialized an attribute this way now produces a document with both the original attribute
  and a new sibling element. Nothing reports it, because nothing is wrong — the contribution
  addressed exactly what Section 11.4 says it addresses. The remedy is the canonical spelling,
  pinned by `xml-an-attribute-from-xml-input-is-overridden-through-its-canonical-address`.
