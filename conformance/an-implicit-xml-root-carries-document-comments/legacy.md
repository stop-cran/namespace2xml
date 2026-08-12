# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `cfg.xml` as `<cfg only="1" />` and exits 0 — both
  comments gone, the scalar projected as an attribute, and the selector name kept as the document
  element. The case expects `<only>1</only>` bracketed by the two comments.
- Contract: Section 19.5 one document element; Section 20 comment placement; Section 11.5.
- Legacy observation: 2.4.0 discarded namespace-profile comments outright on the namespace-input
  path, so no comment could reach an XML document however it was rooted. The attribute projection
  and the retained selector name are separate divergences, stated here because they change the same
  file.
- Clean behavior: no `root` is declared and the selection has exactly one top-level member, so
  Section 19.5's rule that XML "emits one document element" is satisfied by promoting `only` to it.
  No element then stands for the view itself, and the comments Section 20 requires to "precede that
  source's first surviving contribution" and to "follow its final surviving contribution" have
  nowhere inside the tree to go. XML admits comments in the prolog and the epilogue, so that is
  where they are written, and the result is a well-formed document that still says what the source
  said.
- The alternative an implementation reaches for is to drop them, which is what this case exists to
  fail. Dropping is visible only by absence: the run exits 0 and reports nothing, so a person
  converting a commented profile to XML learns that the comments are gone by reading the output.
  JSON, which genuinely cannot represent a comment, warns with `WARN003`; a format that can
  represent one has less excuse to be silent than the format that cannot.
- The companion case `xml-output-places-a-document-trailing-comment-last` covers the same rule with
  an explicit `root`, where the root element holds the comments instead. Between them the two shapes
  fix the placement whether or not the view has an element of its own.
