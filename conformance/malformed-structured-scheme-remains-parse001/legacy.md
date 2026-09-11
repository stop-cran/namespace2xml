# Legacy differential

- namespace2xml 2.4.0: **agrees**. It exits 1 and publishes no output, matching the Appendix C.6
  exit/tree oracle even though 2.4.0 did not distinguish the clean structured-syntax diagnostic.
- Contract: Sections 9.2 and 15 and Section 26 item 94.
- Clean behavior: malformed structured syntax remains `PARSE001`, distinct from semantic
  `SCHEME003`.
