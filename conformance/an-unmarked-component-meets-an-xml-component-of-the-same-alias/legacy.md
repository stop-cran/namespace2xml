# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `ns.p:x=`, `xmlns:p=urn:p`, `nons.x=2`, `att.x=2`
  and `ns.x=2` — the attribute's value `1` and the namespaced element's value `1` are both gone,
  the element is named by the source document's prefix rather than by its namespace, and the
  namespace declaration is emitted as an ordinary data key. Exit 0, nothing reported.
  **verified** — measured three times against the Appendix C.6 pinned 2.4.0 package, identical
  each time.
- Contract: Section 11.4's component identity — `Q{}x` and `x` are the same component, while an
  attribute `@x` and a namespaced element `Q{urn:p}x` are components distinct from `x` that merely
  share its simple alias — together with the `WARN011` those two owe.
- Legacy observation: 2.4.0 had one kind of name component, so an attribute, a prefixed element
  and a mapping key of the same spelling were the same slot. The later namespace contribution
  therefore overwrote both XML values silently, and the prefix binding survived only as a key
  named `xmlns:p`, which is data the document never contained.
- Clean behavior: `nons.x=2` overrides, because a no-namespace element and an unmarked component
  are one node and that is what makes cross-format overlay work at all. `att.x=2` and `ns.x=2` do
  not override; each adds an ordinary component beside the XML one and warns, naming the canonical
  spelling that would have overridden it.
- The three cases are in one fixture on purpose. They differ only in the kind of XML component
  already present, and the rule is that exactly one of the three merges; a fixture carrying any
  one of them alone cannot distinguish "the alias rule fired" from "the identity rule fired".
- The two warnings differ in their prose as well, which this fixture does *not* pin: Appendix C.4
  never compares `message`, deliberately, so that specification renumbering and wording changes do
  not invalidate the corpus. The attribute case names an attribute and an element, the namespaced
  case names an element in a namespace and an unmarked component, and a single message covering
  both told the reader about a component their run did not contain. That distinction is asserted in
  `AliasedComponentWarningTests`, which is where prose belongs.