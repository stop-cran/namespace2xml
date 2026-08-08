# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 4.5; Section 20.
- Legacy observation: YAML was neither read nor written, so no comment had an owner to be wrong
  about.
- Clean behavior: this is the case that separates Section 20's document classes from Section 4.5's
  binding rule, which the round-trip fixture cannot: there, the first entry is the output root, so
  a comment bound to it and a comment owned by nothing are emitted in the same place.

  Here the source has two top-level keys and only `cfg` is published. `# document leading` is
  "before the first payload or item", which Section 20 makes document-leading, and Section 4.5
  gives that class "no value owner". Binding it to the immediately following entry instead would
  attach it to `other`, which no output selects, and the comment would be lost from every file the
  run produces. Section 20 says where an ownerless comment goes: "document-leading comments precede
  that source's first surviving contribution", and in `cfg.yaml` that contribution is `kept`.

  `# document trailing` is "after the final payload or item". It follows the final surviving
  contribution of the same output for the same reason.

  The pair also fixes what happens as the selection changes: because neither comment is owned by a
  value, adding or removing an output, or ignoring a path, moves neither of them off the document.