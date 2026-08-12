# Legacy differential

- namespace2xml 2.4.0: **agrees** on content, modulo CRLF line endings under the Section 24
  divergence. It writes the same three lines, including the normalized `# cfg.a=1`.
- Contract: Section 8.5; Section 20; Section 8.6 by contrast.
- Legacy observation: the baseline emits both comment lines of a source that contributes nothing,
  ahead of the contributing source's entry. The measurement records no divergence beyond line
  endings, and the agreement is not evidence about the rule: 2.4.0 has no notion of comment
  placement to get right, and this case's two comments are already first in source order, so an
  implementation that simply emitted comments where it met them produces the same bytes.
- Clean behavior: `disabled.properties` forms no entry at all, and Section 8.5 says a source in that
  state "has no contribution for a comment to trail, so its whole run is the opening run and is
  document-leading". Section 20 then places document-leading comments before "that source's first
  surviving contribution"; the source has none of its own, so the comments lead the merged document
  and precede `b=2`.
- The shape is the ordinary way a file gets switched off: a `#` in front of every line. Section 8.6
  suppresses "comments bound to suppressed paths", and a reading that treated the whole file as
  suppressed would delete the sentence saying why it was switched off — which is the one line whose
  survival the person who wrote it depends on. Commenting a line out is not the same act as deleting
  it, and the note explaining the decision outlives the setting it disabled.
- `#cfg.a=1` is a comment, not an entry: Section 8.1 rule 2 makes a leading unescaped `#` a comment
  marker whatever follows it. It is emitted as `# cfg.a=1` because Section 8.5 strips the marker from
  the text and Section 20 "prefixes every emitted physical line with `# `".
