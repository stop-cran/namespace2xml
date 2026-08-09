# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `cfg.xml` as two lines,
  `<?xml version="1.0" encoding="utf-8"?>` followed by `<cfg a="1" b="2" />`, and exits 0.
  Three independent divergences from the expected file: the XML declaration is present
  though the effective option set says `NoDeclaration`, no indentation is applied though
  the effective option set (with `NoIndent` gone) restores the default `Indent`, and both
  scalars are rendered as attributes on the single root element rather than as child
  elements.
- Contract: Section 16.9's replacement semantics for output-options directives. Section 3.2
  as a correction of behaviour caused by silently accepted directives — output options had
  no representation at all in 2.4.0.
- Legacy observation: `xmloutputoptions` did not exist as a directive in 2.4.0. Neither
  scheme line was recognized, so the run reached the XML writer with whatever the baseline
  chose by default. The baseline's default rendering emitted a declaration, produced no
  indentation, and — as with `contradictory-output-option-flags-are-scheme001` — placed
  scalar mapping children as attributes on the containing element. The correction here is
  not that any one of those defaults was wrong but that no scheme configuration could
  change them, so the two directives this fixture cares about were both inert and their
  ordering rule was invisible.
- Clean behavior: §16.9 states that "for every output-options directive, the later complete
  directive replaces the earlier complete flag set. Flags from separate declarations do not
  accumulate. When a replacement omits every flag from a mutually exclusive mode group,
  that group's documented default is reapplied." The second scheme line, `cfg.xmloutputoptions=NoDeclaration`,
  therefore replaces the first outright: `NoIndent` and `PreserveCData` from the earlier
  declaration are discarded, `NoDeclaration` stands, and the `Indent`/`NoIndent` and
  `PreserveCData`/`CDataAsText` groups fall back to their documented defaults
  `Indent` and `PreserveCData`. The XML writer therefore emits no `<?xml ... ?>`
  declaration, indents at two spaces per level, and — because a scalar mapping child is a
  child element under §19.5 — writes `<cfg>` with `<a>1</a>` and `<b>2</b>` on their own
  indented lines.
- The difference is intentional: an implementation that accumulates flags across
  declarations lets an earlier scheme file's choice silently survive into a later file's
  configuration, which is exactly the failure mode users configure a `NoDeclaration`
  scheme file to *end*. The 3.0 rule that "the later complete directive replaces the
  earlier complete flag set" makes an override total, and it makes the mode-group default
  restoration explicit so that dropping one flag never leaves a group in an undefined
  state.
