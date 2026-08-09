# `substitute=Key` preserves a native string exactly

Acceptance item 18. Section 13.4, Section 8.3, Section 16.7.

## What the inputs ask for

One JSON document carries the same string twice. Both members are written `"a\\*b"`, which the JSON
reader decodes to the four characters `a`, `\`, `*`, `b`. `cfg.kept.substitute=Key` names one of
them and nothing names the other.

## What Section 13.4 requires

> Native JSON, YAML, and XML strings matched by `Key` or `None` are preserved exactly after native
> format decoding; no transformer escape decoding is applied.

Section 8.3 gives the pass that is being suppressed. Within an interpreted native string "a
backslash immediately followed by `*` emits `*` and consumes both scalars", so the unmatched member
becomes the three characters `a`, `*`, `b`.

## The discrimination

The two members differ in the output: `kept` keeps its backslash and `decoded` loses it. JSON output
is chosen because Section 6.4.3's string escaping renders one literal backslash as `\\`, so the
count is unambiguous in the fixture file; a namespace destination would re-encode both values and
hide the distinction.

Three wrong implementations are caught. One that ignored the directive would emit `a*b` twice. One
that applied it to the whole document would emit `a\*b` twice. One that lexed the matched value with
interpretation merely switched off — rather than not lexing it at all — would still consume the
Appendix A.5 escape and emit `a*b` for `kept`, because Section 8.3's transducer decodes `\*`
independently of whether any wildcard is being substituted into.

## Why `Key` rather than `None`

Section 16.7 gives `Key` and `None` the same value column and different name columns, and Section
13.4 names both in the sentence under test. Using `Key` shows that the preservation rule follows the
value column alone: this fixture would read identically under `None`, and choosing the mode that
still interprets names proves the two columns are independent rather than one switch.

## Not asserted

That a native *name* is affected, which needs a native key defining a capture and is declined by
this version. Nor the namespace-profile half of Section 13.4, whose escapes are decoded regardless
of mode and which the sibling `substitute-none-…` fixture exercises.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 13.4 and 8.3; Appendix A.5. Section 3.2 as a correction.
- Legacy observation: the baseline exits 0 and writes `cfg.json` with `"kept": "a\\*b"` and
  `"decoded": "a\\*b"` — the same text for both members, with the backslash retained in each. It
  therefore agrees with the case about the matched member and differs about the unmatched one.
  2.4.0 had no separate escape pass for a string a native reader had already decoded, so there was
  nothing for `substitute=Key` to suppress and nothing to apply to `decoded` either.
- Clean behavior: Section 8.3's second table is a real pass with two escapes, `\*` and `\${`, and
  Section 13.4 makes `Key` and `None` the way to switch it off. `kept` is preserved and `decoded` is
  decoded.
- The difference is intentional: without the pass there is no way to write a literal `${` into a
  JSON-sourced value that reaches a namespace destination, and without the Section 13.4 exemption
  there is no way to keep a backslash that a JSON document meant literally. The sibling fixture
  `native-strings-do-not-get-a-second-escape-pass` records the same baseline gap from the other
  direction.
