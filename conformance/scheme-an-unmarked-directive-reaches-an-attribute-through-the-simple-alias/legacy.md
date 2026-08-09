# Legacy differential

- namespace2xml 2.4.0: **crashes**. It exits `1` with
  `Error parsing input: unexpected 'r', file: schemes/scheme.txt, line: 5, column: 1`, writing no
  output. **verified** — measured against the Appendix C.6 pinned 2.4.0 package.
- Contract: Section 15.2's scheme-path alias — "an explicitly marked `Q{}`, `@`, or `#n` component
  selects only that XML component. An unmarked component uses the simple alias index for
  compatibility and convenience" — with Section 13.1 for the alias itself and Section 16.6 for
  `type=ignore`.
- Legacy observation: line 5 is `r.pick.Q{}w.type=ignore`, and 2.4.0 has no `Q{...}` production, so
  the run fails on the one line whose purpose is to name the element rather than the attribute.
  Deleting it makes the other two measurable: 2.4.0 exits `0` and writes
  `<r>\n  <both z="" />\n  <pick w="" />\n  <attr y="keep" />\n</r>` with CRLF endings. `attr` is
  the compatibility affordance itself — `r.attr.x.type=ignore` removed the attribute `x` and left
  `y="keep"`, so an unmarked scheme component **did** reach an attribute in 2.x, which is why
  Section 15.2 grants the alias. The other two elements show what it cost. `both` and `pick` each
  carried an attribute and a child element of one name, and both came back as an empty attribute:
  `z="3"` and `<z>4</z>` became `z=""`, `w="5"` and `<w>6</w>` became `w=""`. `pick` was not
  addressed by any surviving directive, so merely reading a document with that shape destroyed it.
  And the children were reordered, `attr` moving to the end.
- Clean behavior: `r.attr.x.type=ignore` reaches the attribute through the alias, as in 2.x.
  `r.both.@z.type=ignore` names the attribute outright and leaves the element `<z>4</z>` standing,
  and `r.pick.Q{}w.type=ignore` names the element outright and leaves the attribute `w="5"`
  standing. Section 11.4 keeps an attribute and a same-named child element distinct components, so
  every value in the document survives, and Section 5.2 keeps the children in document order.
- The difference is intentional, and the shape `both` and `pick` share is the reason. 2.4.0 offered
  the convenience of an unmarked spelling by having only one namespace, so a document carrying both
  an attribute and an element of one name had nowhere to put the second and lost it. 3.0 keeps the
  affordance and adds a way to say which one is meant, so the convenient spelling stays convenient
  exactly where it is unambiguous.