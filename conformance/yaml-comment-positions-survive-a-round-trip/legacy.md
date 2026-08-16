# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 4.5; Section 19.4; Section 20.
- Legacy observation: YAML was neither read nor written, so no comment in a YAML source ever
  reached an output and no association rule was ever exercised.
- Clean behavior: Section 20 classifies a comment by the content around it, and Section 4.5 says
  what each class binds to. The three document-level classes are decided first: "a comment before
  the first payload or item is document-leading", "a comment between two payloads or items becomes
  a leading comment of the following payload or item", and "a comment after the final payload or
  item is document-trailing". Section 4.5 gives the two document classes "no value owner", so they
  are bound to neither the first nor the last entry; Section 20 places them instead, saying
  "document-leading comments precede that source's first surviving contribution and
  document-trailing comments follow its final surviving contribution".

  The remaining classes bind to a value. Section 4.5: "an inline comment belongs to the entry or
  item on the same logical line", so `# inline on alpha` and `# inline on one` stay on their own
  line of output, the second showing that a sequence item is an owner as much as a mapping entry.
  "A trailing comment belongs to the immediately preceding entry or item", so `# trailing of delta`
  stays inside `gamma` next to `delta` rather than escaping to the document, and `# trailing of
  zeta` follows `zeta`.

  `# leading of epsilon` is the discriminating case. It sits at the column of `cfg`'s children,
  after `gamma`'s child `delta` and before `epsilon`. Section 20's middle bullet binds it to "the
  following payload or item", which is `epsilon`, not to the preceding `delta` — a comment is
  trailing only when nothing follows it at its own level. Reading it as trailing would place it
  inside `gamma`, one level too deep and attached to the wrong value.

  Ordering is Section 4.5's: comments "accumulate in source order", so `# document leading`
  precedes `# leading of alpha` even though the first has no owner and the second binds to `alpha`,
  and `# trailing of zeta` precedes `# document trailing` for the same reason.

  Section 19.4's fixed two-space indentation applies to the comments as well as the values: a
  comment is written at the indentation of the value it belongs to.
