# Legacy differential

- namespace2xml 2.4.0: **agrees** on the expected tree and exit code, but only because it rejects
  the unknown option before reading inputs. Its diagnostics and execution semantics differ.
- Contract: Sections 7.2, 14.1, and 21.2; Section 26 item 92.
- Clean behavior: both warnings survive, exit code is 1, and the existing destination remains
  byte-identical because no publication operation occurs.
