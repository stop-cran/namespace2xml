# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 4.5; Section 8.5; Section 20.
- Legacy observation: a comment after the last entry of a namespace profile was read and then
  dropped, because nothing downstream consumed the document-trailing class.
- Clean behavior: Section 8.5 gives the namespace format its own association rule — "Consecutive
  comments are associated with the next entry. Trailing comments with no following entry remain
  document-trailing comments." Both halves are asserted here. `# document leading` and `# leading
  of b` each have a following entry, so they bind to `cfg.a` and `cfg.b`; `# document trailing` has
  none, so it stays document-trailing.

  Section 8.5's first sentence is unconditional, which is why `# document leading` binds to the
  first entry rather than becoming a document-leading comment. Section 20's generic classification
  — "a comment before the first payload or item is document-leading" — applies to a format that
  does not state its own rule; the namespace format states one. The distinction is not visible in
  this output, where both readings emit the comment in the same place, but it decides whether the
  comment would survive an ignore mask on `cfg.a`.

  Section 4.5 gives a document-trailing comment "no value owner", so it cannot be emitted by
  following the entry it happened to sit after. Section 20 places it instead: "document-trailing
  comments follow its final surviving contribution", which for a single output instance is the end
  of the document. It therefore survives the loss of any one entry, and it is emitted after `b: 2`
  rather than being bound to `b`.
