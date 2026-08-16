# Legacy differential

- namespace2xml 2.4.0: **differs**. It treats `*` as an ordinary key, so `'*': {}` is emitted
  verbatim as literal data at exit `0`. Read back through its own reader that key is a rule, which
  is the same round-trip break recorded in
  `a-backslash-asterisk-in-a-native-key-is-a-literal-asterisk`.
- Contract: Section 10.4. An empty mapping or an empty sequence beneath a wildcard key "has no
  entries for entry-by-entry extraction to find, so the template would contribute nothing at every
  path it matched. That is `PARSE001` against this section, once per failing source".
- Two sources fail, so two diagnostics are owed. Naming each document individually is the whole
  value of the cardinality: a single diagnostic would tell an operator that some template was inert
  without saying which file to edit.
- Both empty container shapes are covered, because they fail for one reason. What the author wrote
  is a **shape** rather than a value, and Section 10.4 gives a template no way to carry one:
  "carrier ancestors created only to contain an extracted template do not contribute
  mapping-presence marks".
- This case previously expected exit `70`. Section 6.3 defines `0` and `1` and nothing else, so a
  status outside that set was never something a caller could be asked to handle.
