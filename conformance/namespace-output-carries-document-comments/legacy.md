# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 4.5; Section 20.
- Legacy observation: every comment was dropped. The output is `a=1` and `b=2`, carrying neither
  the document-leading comment, nor the document-trailing comment, nor the leading comment of `b`.
- Clean behavior: Section 20 tells a namespace destination to "Emit normalized leading `#`
  comments", and the association each comment carries decides where each one lands. `# leading of
  b` sits between two payloads, so it "becomes a leading comment of the following payload or item".
  The other two own no entry at all: Section 4.5 says "document-leading and document-trailing
  comments have no value owner", and Section 20 places them without one — "document-leading
  comments precede that source's first surviving contribution and document-trailing comments
  follow its final surviving contribution". For a single output instance that is the top and the
  bottom of the file.

  The destination is deliberately namespace rather than YAML, which is where the same rule was
  already asserted. Reading "Emit normalized leading `#` comments" as a filter admitting only
  entry-bound comments would delete the other two silently, and the INI row of that same table
  settles which reading is meant: INI comments are "emitted only as full-line leading comments",
  and the paragraph below the table then states that "Document-leading comments precede the first
  global key or section". The wording fixes the form a comment takes, not which comments survive.
