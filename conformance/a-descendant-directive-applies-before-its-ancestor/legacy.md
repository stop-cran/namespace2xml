# Legacy differential

- namespace2xml 2.4.0: **agrees** on content, modulo CRLF line endings under the Section 24
  divergence. It writes the same `{"a": [[1, 2], {"m": 3}]}` shape.
- Contract: Section 15.1, "within one pass a directive at a deeper path applies before a directive
  at a shallower path"; Section 26 item 54.
- Legacy observation: the baseline converts `cfg.a.x` to an array first and then converts `cfg.a`,
  so the already-converted `x` enters its parent as a sequence element and the unconverted `y`
  enters as a mapping. The measurement records agreement, which is the point of the case: this
  order was never written down anywhere, in either project, and the corpus is now what holds it.
- This is the one directive-ordering question with two defensible answers and no diagnostic to
  distinguish them. Outermost-first would consume `cfg.a`'s children before `cfg.a.x.type` was ever
  consulted, and the descendant directive would vanish without a word. A fixture is the only thing
  that keeps the answer from drifting, because nothing in the output says which order produced it.
