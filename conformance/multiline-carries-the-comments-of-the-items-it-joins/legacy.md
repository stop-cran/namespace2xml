# Legacy differential

- namespace2xml 2.4.0: **differs**. It drops all four comments from both outputs, writes CRLF line
  endings, and exits 0 with nothing said about the loss. It also diverges here in two ways this
  case is not about: `cfg.properties` does not apply `multiline` at all and emits the unjoined
  items `cfg.a.0=one` and `cfg.a.1=two`, so the directive reached only one of the two outputs that
  asked for it; and `cfg.yaml` loses the `cfg` root.
- Contract: Section 4.5, which moves the comments of every consumed value onto the single result of
  a collapse and leaves placement to the accumulation rule already stated there; Section 16.6,
  which defines `multiline`; Section 19.1 and Section 19.4 for the comment bytes of each output.
- Legacy observation: joining a sequence discarded every comment bound to the items being joined.
  The clean implementation initially did the same: the join rebuilt the node from the sequence
  node's own comments and never read the items', so four comments the author wrote disappeared
  with no diagnostic and exit 0.
- Clean behavior: all four survive on the joined scalar. Section 4.5 needs no new placement rule
  for the collapse, because once the comments accumulate at one path its existing rule — "the
  latest remains inline and earlier inline comments become leading comments in source order" —
  already decides. `# second` was inline on the last joined item, so it is the one that stays
  inline; `# first` was inline on an earlier item and becomes leading in its source position.
- The case pairs YAML with namespace deliberately. Only YAML can represent an inline comment, so
  only YAML exercises the accumulation rule; namespace output flattens all four to full-line
  comments under Section 19.1 and therefore proves the *set* and the *order* survive independently
  of the placement decision. A fix that moved the comments but lost their order would pass neither.
- `a: |- # second` is a comment on a block scalar header. It was checked against an independent
  YAML parser, which reads the value back as `one\ntwo`, and the tool reproduces the file
  byte-for-byte when the output is fed back in.