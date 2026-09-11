# Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Sections 9.1, 10.4, and 11.4; Section 26 item 71.
- Legacy observation: all ten Appendix C.6 samples exited 1 and published no
  `lit.properties`.
- Clean behavior: the leading backslash suppresses typed-component recognition, so the
  ordinary `@key` mapping key is emitted as `\@key=literalval`.
- Intentional correction: the explicit escaped-key rule keeps a literal marker-shaped mapping key
  representable instead of allowing parser-specific marker handling to reinterpret or reject it.
