# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 16.9; Section 19.3; Section 24.
- Legacy observation: there was no JSON output and therefore no layout or escaping options.
- Clean behavior: Section 19.3 "uses indented output by default", which Section 16.9 fixes at two
  ASCII spaces per nesting level. `Compact` emits no insignificant spaces or line breaks.
  `EscapeNonAscii` emits every scalar above U+007F as an uppercase hexadecimal `\uXXXX`
  escape, in keys as well as values; a supplementary-plane scalar has no single escape and is
  written as its surrogate pair, so the option leaves no byte above U+007F in the file.
- The difference is intentional: Section 24 makes the output byte-identical across platforms, so
  the escape spelling and its letter case are part of the contract rather than a writer detail.
