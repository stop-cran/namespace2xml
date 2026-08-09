# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `cfg.yaml` as four lines — `- name: 42` /
  `  value: 1` / `- name: other` / `  value: 2` — and exits 0. The case expects the same
  record shape but with the first record's `name` **quoted** as `'42'` and with the two
  bound comments emitted between each `name` and its `value`. Two independent defects: the
  generated key `42` is unquoted, so a YAML reader round-trips it as an integer rather than
  the string it names; and the comment run bound to each `cfg.<key>.value` entry is
  discarded.
- Contract: Section 16.5 `key` transformation and Section 4.5 comment binding; Section 3.2
  as a correction of behaviour "caused by silent loss of multiline values in JSON or XML"
  only partially — the correction here is the neighbouring rule that comments bound to a
  logical path survive across the transformations that move that path.
- Legacy observation: 2.4.0 emitted the generated key field as an ordinary YAML scalar with
  no consideration for whether its plain spelling would resolve to a non-string kind, so the
  decimal name `42` produced the plain YAML scalar `42`, which is a YAML 1.1 integer. And
  2.4.0 discarded namespace-profile comments outright on the namespace-input path; the
  comment stream had no representation in the overlay, so a downstream `key` transformation
  had nothing to move. Two different mechanisms produce a single lost feature in the file.
- Clean behavior: §16.5 states that "the generated key field is inserted first as a string
  scalar containing the decoded mapping-key text; scalar inference is never applied to this
  generated field", so `42` reaches the YAML writer typed as a string and §19.4 emits it
  single-quoted under the rule that "a string whose plain spelling would resolve to a
  non-string kind under `RestrictedYaml1` is emitted single-quoted". Section 16.5 further
  states that "comments bound to the original child path move with the complete generated
  record", so the `# comment on 42` block moves onto the emitted record and §20 renders it
  in the record's normalized position.
- The difference is intentional: a mapping whose keys are all decimals is a legal way to
  spell an ordered sequence of *named* records — a version list, a numbered menu, an
  ordering-value carried into a normalized record set — and turning the name `42` into the
  integer 42 by omitting the quotes silently changes what the file *says* about those
  records. The comment loss is the same category: the namespace author writes a
  human-readable annotation next to a value and expects the tool to move it with that value,
  and an implementation that drops the annotation gives the reader less than they started
  with.
