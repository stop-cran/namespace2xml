# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `*.c=XXX` where this case expects `\*.c=XXX`. The
  data is the same; the escape is missing. CRLF-terminated, under the Section 24 divergence.
- Contract: Section 10.4, "`\*` contributes a literal asterisk", and Section 21, which escapes a
  literal `*` in a namespace name part.
- The two clauses are one round trip. Section 10.4 lets a YAML key spell a literal asterisk, and
  Section 21 is what lets the resulting namespace output be read back as that same literal. 2.4.0
  honours the first and not the second, so its own output re-read through its own reader turns a
  literal key into a wildcard template — the file says `*.c=XXX`, which is a rule, not data.
- The case also fixes the negative half of extraction: `d` is a sibling record in a later file and
  must **not** acquire `c`, because `'\*'` is a name and not a template. A build that extracted it
  anyway would emit `d.c=XXX`, and the escape test alone would not catch that.
- Legacy observation: 2.4.0 did not enrich `d`, so it agreed that `\*` suppresses the template. The
  divergence is confined to how it then spelled the key on the way out.