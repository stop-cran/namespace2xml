# Legacy differential

- namespace2xml 2.4.0: **differs**, and silently. It exits 0 and resolves every reference --
  `raw=X`, `other=X`, `deep.nested=X` -- because it ignores a `substitute` directive written with
  no path at all. The directive is accepted, no message is emitted, and nothing it asks for
  happens. A profile relying on a pathless `substitute=None` to keep reference text literal is
  therefore silently substituted anyway.
- Contract: Section 16.7; Section 15.2; Section 14.4.
- Clean behavior: `substitute=None` carries no path, and Section 16.7 says "the pathless form
  matches every node", so `other` and `deep.nested` keep their reference text -- including
  `deep.nested`, which no path-scoped directive names. `cfg.raw.substitute=All` is declared later,
  and Section 15.2 gives the later matching directive the win "for the same effective setting", so
  `raw` alone interprets and resolves to `X`.
- The two halves are the assertion. A reading in which the pathless form governs the root node
  alone leaves `other` and `deep.nested` interpreting, and a reading in which a pathless directive
  outranks a path-scoped one leaves `raw` literal. The case is arranged so that each reading changes
  exactly one line, and the third key is nested two levels deep because a root-only reading and a
  subtree reading are otherwise indistinguishable.
- Section 15.2 also says "pattern specificity does not alter precedence", so the more specific
  `cfg.raw.substitute` wins here by being *later*, not by being narrower. Reversing the two scheme
  lines would leave every key literal, which is what makes this a source-order rule rather than a
  specificity rule.
- `lit` is not emitted. It is a support entry outside the selected subtree, which Section 14.4
  retains for evaluation and does not emit "unless independently selected".
- The baseline's agreement on `raw` alone is a coincidence of it resolving everything, not evidence
  about precedence: it reaches the same text for that one key by applying no directive at all.