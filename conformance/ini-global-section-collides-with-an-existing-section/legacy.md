# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 19.6 — a path that already projects to a section named `global` collides with
  the hoisted section, and the collision is blocking `FLAT001`.
- Legacy observation: 2.4.0 exits 0 and writes both the preamble and a `[global]` section, because
  it has no hoisting option and therefore no collision to detect. Its output is the file 3.0 would
  have written had the option been absent, plus its usual blank lines.
- The difference is intentional: the two origins are a set of one-part scalar paths and a path of
  two or more parts, and merging them would make the document's content depend on the name this
  specification chose rather than on the paths the author wrote.