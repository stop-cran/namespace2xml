# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 4.5; Section 8.5; Section 20.
- Legacy observation: a comment after the last entry of a namespace profile was read and then
  dropped, because nothing downstream consumed the document-trailing class.
- Clean behavior: Section 8.5 gives the namespace format its own association rule — "Consecutive
  comments are associated with the next entry, with one exception: comments preceding the first
  entry of a source are document-leading, as Section 20 classifies the first position for every
  format. Trailing comments with no following entry remain document-trailing comments." All three
  halves are asserted here. `# document leading` opens the source, so it is document-leading and
  binds to no path; `# leading of b` has a following entry, so it binds to `cfg.b`;
  `# document trailing` has none, so it stays document-trailing.

  The opening run and the generic Section 20 classification — "a comment before the first payload
  or item is document-leading" — therefore agree, which is what Section 8.5's exception is for: a
  profile converted between formats keeps its header. The distinction from binding to `cfg.a` is
  not visible in this output, where both readings emit the comment in the same place, but it
  decides whether the comment would survive an ignore mask on `cfg.a`. The companion case
  `a-namespace-header-comment-outlives-its-first-entry` asserts that it does.

  Section 4.5 gives a document-trailing comment "no value owner", so it cannot be emitted by
  following the entry it happened to sit after. Section 20 places it instead: "document-trailing
  comments follow its final surviving contribution", which for a single output instance is the end
  of the document. It therefore survives the loss of any one entry, and it is emitted after `b: 2`
  rather than being bound to `b`.
