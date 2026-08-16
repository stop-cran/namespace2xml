# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `r.xml` as
  `<?xml version="1.0" encoding="utf-8"?>` on one line, then `<r>` on the next, then a
  single `<item child="" a="1" other="" b="2" />` collapsed onto one line, then `</r>`,
  and exits 0. The case expects the later `<item>` — attributes `b="2"` and child
  `<other>o1</other>` — to *replace* the earlier one outright. Two independent defects:
  the two `<item>` elements are merged rather than the later one replacing the earlier,
  and the child elements `<child>` and `<other>` have been attribute-flattened onto the
  merged element as empty attributes with their text lost.
- Contract: Section 17.4 XML `merge=replace` semantics; §3.2 correction against "relying
  on `merge` to control collisions between output instances", where the neighbouring
  half of the same correction is that input-scope `merge` remains recognized and must
  apply to input-time element merging under §17.
- Legacy observation: 2.4.0's `merge` directive was recognized but its input-time XML
  behaviour did not implement `replace` at the element level. The two `<item>` occurrences
  under `<r>` were folded by the same attribute-flattening path
  `xml-comments-are-invisible-to-alias-resolution` documents: every child element became
  an empty attribute on the containing element, and the two contributions' attributes
  were unioned. The result is a single element carrying every original element's identity
  as a same-named empty attribute, which is not any of the three well-defined merge
  behaviours §17 lists (`deep`, `replace`, `append`) — it is the baseline's XML reader
  and merger sharing a shape assumption the specification does not carry.
- Clean behavior: §17.4 states that "when the effective destination `filemerge` strategy
  is `replace`, the later element's complete value — attributes, content tokens, comments,
  and children — replaces the earlier element. Singleton/sequence classification and
  recursive child merging are not applied to the replaced earlier element." §16.10's
  input-time `merge=replace` applies the same principle to two contributions at one input
  path: "the later complete value replaces the earlier value. 'Value' here means payload,
  container presence, children, and sequence projection". `r.item.merge=replace` therefore
  discards `earlier.xml`'s `<item a="1"><child>c1</child></item>` outright, and the emitted
  document is the later `<r><item b="2"><other>o1</other></item></r>` with indentation.
- The difference is intentional: `replace` is the vocabulary an author uses to say "I want
  the later document's `<item>` and none of the earlier one, at this exact path". An
  implementation that merges the two attribute sets does not honour that intent, and one
  that flattens child elements into attributes changes the shape of both contributions in
  a way no `merge` value describes. The 3.0 rule leaves the semantics of the four `merge`
  values in the author's hands rather than in the reader's.
