# Legacy differential

- namespace2xml 2.4.0: **differs**. It reads `.json` inputs, so the case can be posed to it, but it
  has no `Q{uri}local` component syntax at all. Its only way to name a namespace is an `xmlns:p`
  key plus a `p:local` name, so the URI has to be written into the document by hand and the prefix
  is whatever the author chose — there is no prefix for the writer to generate.
- Contract: Section 19.5's "XML output bytes", and Section 11.4's `@` and `Q{...}` addresses.
- Clean behavior: an element carrying a namespace URI is emitted unprefixed with that URI declared
  as the default namespace. An attribute cannot do that, because an unprefixed attribute is in no
  namespace rather than in the default one, so a namespaced attribute takes a generated prefix.
  The prefixes are `n1`, `n2`, … numbered in the order their namespaces are first needed in
  document order, and all of them are declared on the document element.
- Why the numbering is specified at all: left alone, `XmlWriter` invents a prefix from its own
  scope counter and produced `p2` here — deterministic for that library, and unguessable for any
  other implementation of the same specification. Section 24 asks two conforming implementations
  to produce identical bytes, which an internal counter of one XML library cannot deliver.
- Why two namespaces and two elements: one namespace would not show the numbering order, and one
  element would not show that a prefix assigned for the first element is reused rather than
  reassigned. `e2` needs `http://ex/b` first and `http://ex/a` second, so an implementation that
  numbered by order of use *within an element* rather than across the document would emit `n1` and
  `n2` swapped on that element and fail here.
- Why `plain` is present: an attribute with no namespace must stay unprefixed, and must not acquire
  the element's default declaration.
- `e2` also pins the empty-element spelling `<e2 ... />` against `<e2 ...></e2>`: it has attributes
  and no content.
