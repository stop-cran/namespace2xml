# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `"a": ""` and `"b": ""` — the values `1` and `2` are
  replaced by empty strings — drops the `cfg` root, writes CRLF, ends `cfg.json` without a final
  newline, and says nothing about any of it. Exit 0. **verified** — measured against the Appendix
  C.6 pinned 2.4.0 package.
- Contract: Section 4.4's exclusive-shape contest, which applies a destination's discard before
  determining the container contribution; Section 20, which discards comment nodes outside XML with
  one summarized warning.
- Legacy observation: a comment beside a value destroyed that value. The failure is silent and the
  output is well-formed, so nothing downstream can tell `"a": ""` from a value the author really
  wrote as empty. An XML comment is the most ordinary thing to find in a configuration file.
- Clean behavior: the comment is discarded because JSON and YAML render no comment nodes, and that
  discard is applied before the shape contest. The comment was the node's only member, so at these
  destinations there is no container to contest the scalar and the value renders. One summarized
  `WARN003` per output file reports the comments; no `TYPE002` is raised, because at these
  destinations no shape lost.
- The case is about ordering, not about comments. Resolving the contest first gives the node
  container shape, omits the scalar as the loser, and then discards the member the shape rested on,
  so the output carries an empty container and the run has lost both the comment and the value.
  Metadata about a value must not be able to delete the value.
- Two entries are used rather than one so that a fix which happens to preserve the first value by
  position cannot pass. The XML destination is deliberately absent: it renders comments, so it does
  not exercise this rule, and its comment placement is a separate open question.