# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 10.1; Section 19.4; Section 16.9.
- Legacy observation: YAML was neither read nor written, so neither the restricted schema nor the
  output layout was a contract.
- Clean behavior: Section 19.4 indents two spaces per level, emits no `---`, and uses literal
  block scalars for multiline values, whose chomping indicator carries what the indentation cannot
  — no trailing line break, exactly one, or more than one. Section 19.4 single-quotes a string
  "whose plain spelling would resolve to a non-string kind under `RestrictedYaml1`", so `true`
  and `42` as strings are quoted while Section 10.1's deliberate non-resolutions `yes`, `+1`
  and `.5` stay plain.
- The difference is intentional: Section 10.1 is normative "rather than an underlying library's
  advertised YAML 1.1 or 1.2 mode", so the quoting a writer must apply follows from it and not
  from a library's own emitter.
