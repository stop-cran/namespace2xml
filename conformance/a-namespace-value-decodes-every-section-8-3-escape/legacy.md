# Legacy differential

- namespace2xml 2.4.0: **fails**. It does not write `cfg.json` at all. The run fails with
  `Reference OutputRoot.b was not found at OutputRoot.cfg.dollar [file: values.txt, line: 2]` and
  exits 1, because `\${` did not suppress the reference the way Section 8.3 requires — the `${`
  still opened one, and `b` is not a defined path.
- Contract: Section 8.3's six value escapes and Appendix A.3's `value-escape` production, together
  with A.3's rule that "Emitted text is never rescanned", which is what makes `\${` effective.
- Legacy observation: 2.4.0 had no value-escape layer. Removing `cfg.dollar` so the baseline can
  complete, the same profile renders as
  `{"backslash":"a\\\\b","newline":"a\\nb","carriage":"a\\rb","tab":"a\\tb","unknown":"a\\qb"}` —
  every backslash survived into the model as ordinary text and the JSON writer re-encoded it.
  The instructive part is the last member: 2.4.0 and 3.0 **agree** on `cfg.unknown`, and they agree
  by accident. Section 8.3's final clause says other backslash sequences "preserve the backslash
  and following character", and 2.4.0 treated every backslash that way, so the one case it gets
  right is the one where the rule is to do nothing.
- Clean behavior: Section 8.3 states that `\\` emits `\`, `\${` emits literal `${`, `\n` emits LF,
  `\r` emits CR, and `\t` emits tab, while "other backslash sequences preserve the backslash and
  following character". The JSON writer then re-encodes what it receives: Section 6.4.3 has strings
  "escape `"` and `\` with a backslash, use `\b`, `\f`, `\n`, `\r`, and `\t` for those five
  controls", so a single backslash in the model becomes `\\` and a real LF becomes `\n`. The
  expected file is that composition, not a capture.
- The difference is intentional, and it is the quiet kind. A 2.x profile carrying `a\nb` delivered
  the four characters `a`, `\`, `n`, `b`; the same line now delivers a line feed. Nothing in the
  text of the profile changes, no diagnostic fires, and the output is valid in both versions — the
  value simply means something else. That is why this case exists rather than being left to the
  `\*` fixture: `\*` is the escape an author reaches for deliberately, and these five are the ones
  they may already have written without knowing they were escapes.
