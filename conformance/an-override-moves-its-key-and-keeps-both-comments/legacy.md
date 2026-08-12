# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `app.yaml` containing `name: second`, `only: kept`
  and `tail: end`, in that order, and exits 0. Both comments are gone and the overridden key has
  not moved. The case expects `only`, then `tail`, then both comments, then `name: second`.
- Contract: Sections 4.5 and 5.2, and Section 3.2 as a correction.
- Legacy observation: 2.4.0 kept a mapping key at the position of its *first* contribution and
  discarded comments outright on the namespace-input path, so neither half of the rule was
  observable. `name` stays where `first.txt` put it even though `second.txt` is what supplied the
  surviving value, which means the output orders keys by a contribution that no longer exists in
  the result.
- Clean behavior: Section 5.2 states that "overriding a mapping key moves that exact key,
  together with comments bound to it, to the winning contribution's position mark". `app.name` is
  overridden by `second.txt`, so it takes that later position and falls below both `app.only`,
  which no later source touched, and `app.tail`, which `second.txt` contributed ahead of the
  override. Section 4.5 adds that "overriding a payload or container contribution does not detach
  comments already bound to that logical path", so `# original` is not lost when `first.txt`'s
  value is; both comments end up bound to the same winning path and are emitted in source order
  above it.
- Neither source opens with its comment. Section 8.5 excepts the opening run — "comments preceding
  the first entry of a source are document-leading" — so a comment on line 1 would bind to no path
  and could not demonstrate movement at all. `app.only` and `app.tail` open their sources so that
  `# original` and `# renamed` are ordinary bound comments. The companion case
  `an-opening-comment-does-not-move-with-its-entry` asserts the excepted shape.
- The case is shaped to fail three different wrong implementations. Leaving the key at its first
  position puts `name` above `only`. Detaching the loser's comment drops `# original`. Binding the
  comment to the value rather than to the logical path drops `# original` as well but keeps the
  ordering, so the two defects are told apart by the key order rather than by the comment alone.
- The difference is intentional: a configuration file's comments are written by people to explain
  the values next to them, and an override that silently deletes the explanation while keeping the
  value leaves the reader with less than they started with.
