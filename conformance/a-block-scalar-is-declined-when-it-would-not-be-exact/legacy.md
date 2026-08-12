# Legacy differential

- namespace2xml 2.4.0: **differs**, and loses data on four of the six values. Measured, exit 0, no
  warning:

  ```yaml
  blankend: one
  carriagereturn: |-
    one
    two
  control: "one\x01two"
  exact: one
  indentedfirst: '  one'
  trailingspace: 'one '
  ```

  Everything after the first line break is **discarded**. `exact` becomes `one`, `trailingspace`
  becomes `'one '`, `indentedfirst` becomes `'  one'`, and `blankend` becomes `one`. The value that
  survives is the one carrying a CR, and it survives wrongly: 2.4.0 treats the CR as a line break
  and writes a block scalar, which every YAML reader normalizes back to LF, so `one\rtwo` reads back
  as `one\ntwo`. `control` is written `"one\x01two"`, which is not a YAML escape at all -- YAML has
  no `\x` form -- so the document does not parse. Keys are also emitted in alphabetical order rather
  than input order.
- Contract: Section 19.4; Section 3.3; Section 24.
- Clean behavior: `exact` is a `|-` block, because a block scalar carries it exactly and the block
  can end the document. The other five are double-quoted, each for a distinct reason Section 19.4
  now names -- trailing whitespace is invisible in block source and some readers strip it, a CR is
  normalized to LF on read, U+0001 is outside YAML's `c-printable`, an indented first line is taken
  as the block's own indentation and stripped, and a value ending in a blank line needs `|+`, whose
  block ends with two line breaks and so cannot satisfy Section 24's single trailing LF.
- `exact` is in the fixture as the control. Without it the case would be consistent with a writer
  that never emits a block scalar at all, which would satisfy every assertion here while abandoning
  the rule the section is about.
- The two existing YAML fixtures pin the blank-line row and the indented row; the trailing-space, CR
  and control rows had no coverage, so three of the five conditions Section 19.4 now states were
  being relied on without a test.
- The blank-line refusal applies in every position, not only where the value would sort last. That
  is the choice worth pinning rather than assuming: declining only in final position also never
  produces an illegal document, but it makes a value's spelling depend on its neighbours, so adding
  an unrelated key silently rewrites an untouched value. `blankend` sits fourth of six here for
  exactly that reason -- placed last, the case would pass equally against a writer that declines the
  block only when it would end the file, and would pin nothing.