# Legacy differential

- namespace2xml 2.4.0: **agrees**.
- Contract: Section 3.1 preserves command-line variables as the highest-precedence input source.
  Section 8.7 gives files, physical records, and command-line variables their stable source order.
- Legacy observation: the baseline exits 0 and reproduces `cfg.properties`, including
  `file=second`, `line=second-line`, and `cli=second-variable`.
- Clean behavior: later files override earlier files, later records override earlier records, and
  command-line variables are applied after every file in their own token order even when `-v`
  precedes `-i` in the argument vector.
- The agreement is intentional compatibility evidence: each precedence edge remains observable on
  a different key, so the final command-line winner cannot conceal a broken file or line edge.
