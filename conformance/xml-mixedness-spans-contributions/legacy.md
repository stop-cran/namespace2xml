# Mixedness is a property of the merged element

Section 11.4 evaluates mixedness "at concrete merge time across all input contributions to that
element", and requires that "if the merged element is mixed, every content node uses its `#n`
wrapper even when it originated in an element-only source document".

Two documents contribute to `a`. The first writes it as mixed content; the second writes it as an
element-only element, which on its own would address its child as `a.b`. The merged element is
mixed, so that child is addressed as a content node instead, and one element is no longer reachable
at two addresses.

The converted content is allocated above the content tokens the mixed document already occupies,
because Section 17.4 states that "child elements in mixed content do not deep-merge with elements
from another contribution". Reusing the ordinals the element-only document assigned for sibling
ordering alone would have landed its child on top of the other document`s first text node.

Legacy 2.4.0 does read XML input, contrary to an earlier draft of this note, so a comparison is
available.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 11.4's rule that "mixedness and repeated-child classification are properties
  of the merged common-model element and are evaluated at concrete merge time across all input
  contributions to that element", together with the follow-up "if the merged element is mixed,
  every content node uses its `#n` wrapper even when it originated in an element-only source
  document". Section 17.4 forbids deep-merging mixed-content child elements across
  contributions. Section 3 does not enumerate this specifically.
- Legacy observation: the baseline writes different bytes at both `r.xml` and `r.properties`.
  The measurement records `content r.properties; content r.xml` at exit `0` with no standard
  error beyond the banner.
- Clean behavior: the merged `a` is mixed because one contribution is mixed, so every content
  node under `a` is a `#n` node — `r.a.#0=t`, `r.a.#1.b=1`, `r.a.#2.b=9` — and the XML view is
  `<a>t<b>1</b><b>9</b></a>` with the converted content allocated above the mixed document's
  own tokens.
- Why the difference is intentional: 2.4.0 has no stated model for evaluating mixedness across
  contributions, so an element-only contribution and a mixed one at the same path can only
  agree by coincidence. Reusing the ordinals the element-only document assigned to sibling
  ordering alone would land its child on top of the mixed document's first text node, and
  reaching the same child at two addresses (`a.b` and one of `a.#n.b`) would make one node
  reachable at two paths. Whether the baseline's specific bytes come from either reading is not
  observable from two divergent files, so the case pins only that both are wrong.

