# Legacy differential

- namespace2xml 2.4.0: **differs**. It renders both values, then diverges on bytes: `cfg.json` ends
  at `}` with no final newline. It also writes `Environment.NewLine` rather than LF, so the same
  input produces different bytes on Windows and on Linux; the missing final newline is the
  divergence that survives on every platform. Neither the discarded comments nor the shape question
  is mentioned. Exit 0. **verified** — measured against the Appendix C.6 pinned 2.4.0 package.
- Contract: Section 4.4's exclusive-shape contest, which applies the discard before step 2; Section
  20, which keeps comment nodes in XML and discards them everywhere else with one summarized
  warning per output file; Section 24's trailing newline.
- Legacy observation: 2.4.0 kept the overridden values here, so the shape was never in question for
  it. What it did not do is say that two XML comments had been dropped on the way to JSON and YAML.
  A comment is the part of a configuration file a reader is most likely to have written for another
  reader, and a converter that discards it silently gives no one a chance to notice.
- Clean behavior: the comments are the only members these two nodes have, and no non-XML
  destination renders a comment node, so at `cfg.json` and `cfg.yaml` each node "is not a container
  contribution at that destination". The later scalar therefore has nothing to contest and renders.
  "No shape-conflict warning arises, because at that destination no shape lost; the summarized
  discard warning still reports the comments" — one `WARN003` per output file, and no `TYPE002`.
- Why this case exists alongside `a-discarded-comment-does-not-delete-the-value-it-annotates`: that
  case gives the comment container the later shape-mark, so the container would have won the
  contest and the discard has to be applied to stop it. Here the scalar is later, supplied by a
  namespace profile read after the XML, so the container would have *lost*. Section 4.4 settles
  both the same way, because a container whose members are all discarded is not a container
  contribution at all — it can no more lose a contest than win one. An implementation that applies
  the discard only when the container would have won passes the sibling case, emits `TYPE002` here
  for a shape that never contested, and loses the comment count with it.
- Two entries are used rather than one so that a fix which happens to handle the first node by
  position cannot pass. Both JSON and YAML are rendered because Section 20 discards comments in
  each, and the warning is summarized per output file, so a count that leaked across outputs would
  show up as a wrong number of warnings rather than a wrong value.
