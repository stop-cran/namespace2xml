# Legacy differential

- namespace2xml 2.4.0: **agrees** on the expected tree and exit code, but only because it rejects
  the unknown option before serialization. Its diagnostics and execution semantics differ.
- Contract: Sections 6.2, 19.5, and 21.2; Section 26 item 98.
- Clean behavior: serialization discovers `WARN015`, then the warning policy refuses both
  destinations, reports `Published = 0`, and leaves both existing files byte-identical.
