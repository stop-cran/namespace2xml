# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes
  `<cfg p:b="" xmlns:p="urn:p" xmlns:q="urn:q" w="0" v="9">` and then `<z p:k="1" x="2" q:m="3" />`,
  `<d xmlns="urn:d">` and `<plain k="1" />` — the element `p:b` becomes an empty attribute on its
  parent and its text `t` is gone, `plain` keeps its attribute and loses its text `p`, and the
  author's prefixes are copied through rather than generated — with CRLF and no final newline.
  Exit 0, nothing reported. **verified** — measured against the Appendix C.6 pinned 2.4.0 package.
- Contract: Section 19.5's "XML output bytes" — the start-tag separator, namespace declaration
  placement, the unprefixed element rule, and the empty-element spelling.
- Legacy observation: an element holding text was not representable, so it was demoted to an empty
  attribute and its content dropped in silence. Prefixes came from whatever the input happened to
  write, which makes the output bytes a function of the source document's spelling rather than of
  the model.
- Clean behavior: attributes keep their recorded order and are separated by one space; a namespace
  declaration follows every ordinary attribute of the element carrying it, so the document element
  reads `w="0" v="9" xmlns:n1="urn:p" xmlns:n2="urn:q"`; and `plain`, which is in no namespace
  inside `urn:d`, undeclares with `xmlns=""` in that same position.
- The case this fixture exists for is `b`. It is in `urn:p`, and `n1` is bound to `urn:p` and in
  scope on the document element, so a writer that reused an in-scope prefix would spell it `<n1:b>`.
  Section 19.5 gives an element no prefix, so it is `<b xmlns="urn:p">` — and it stays that spelling
  whether or not `z` carries `p:k` at all. Without that rule an attribute added in one subtree
  silently rewrites element tags in another, which is a diff nobody asked for and a byte-identity
  guarantee that depends on distant edits.
- Why `z` carries three attributes in that order: `p:k` needs `n1` and `q:m` needs `n2`, so the
  numbering is pinned to first need in document order, and `x` between them proves an unnamespaced
  attribute stays unprefixed and keeps its place. `z` is also empty, so it pins `… n2:m="3" />`
  against `…"3"/>` and against `<z …></z>`.
