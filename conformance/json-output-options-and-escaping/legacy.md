# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 16.9; Section 19.3; Section 24.
- Legacy observation: 2.4.0 emitted JSON, but had no output options at all. All three destinations
  came out byte-identical in layout — indented at two spaces, non-escaped — so `compact.json` and
  `escaped.json` were indistinguishable from `indented.json` apart from their extra key. Both
  `jsonoutputoptions` lines were unrecognized scheme directives, and an unrecognized directive was
  ignored rather than reported, so a user asking for compact or escaped output received neither and
  was told nothing. Every file also ended without a final newline and used `Environment.NewLine`
  for its breaks.
- Clean behavior: Section 19.3 "uses indented output by default", which Section 16.9 fixes at two
  ASCII spaces per nesting level. `Compact` emits no insignificant spaces or line breaks.
  `EscapeNonAscii` emits every scalar above U+007F as an uppercase hexadecimal `\uXXXX`
  escape, in keys as well as values; a supplementary-plane scalar has no single escape and is
  written as its surrogate pair, so the option leaves no byte above U+007F in the file.
- The difference is intentional: Section 24 makes the output byte-identical across platforms, so
  the escape spelling and its letter case are part of the contract rather than a writer detail.
