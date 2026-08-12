# Legacy differential

- namespace2xml 2.4.0: **differs**, and the difference is a document it cannot read back. Measured,
  exit 0, no warning: it writes the key **unquoted**, as

  ```yaml
  <<: plain
  j: 2
  ```

  A YAML reader takes that as a merge key whose value is the scalar `plain`, which is not a mapping,
  so the document is invalid YAML on its own terms. 2.4.0 read a quoted key and wrote an unquoted
  one, losing the only thing that distinguished data from syntax.
- Contract: Section 10.1; Section 19.4; Section 3.3.
- Clean behavior: `'<<': plain`, single-quoted. Section 19.4 spells a scalar plain only when that is
  unambiguous, and a plain `<<` is not: read back, it is the merge key Section 10.1 refuses. Single
  quoting is the first spelling that carries the two characters as data.
- The case pins the escape hatch Section 10.1 now makes normative. The refusal itself is already
  pinned by `yaml-restricted-schema-refusals`, but a refusal with no stated way through is a dead
  end, and nothing tested that the way through works or that its output survives a round trip.
- `j: 2` is present so the case also shows the quoted key does not disturb its siblings, and so the
  output is a mapping rather than a single member -- a one-key document would be consistent with a
  writer that quotes every key it emits.