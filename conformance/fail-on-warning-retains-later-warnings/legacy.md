# Legacy differential

- namespace2xml 2.4.0: **agrees** on the expected tree and exit code, but only because it rejects
  the unknown option before any phase runs. Its diagnostics and execution semantics differ.
- Contract: Sections 15.3, 15.4, and 21.2; Section 26 item 92.
- Clean behavior: an early scheme warning does not stop input and planning checks, all three
  warnings remain ordered by phase, and publication is refused only after serialization.
