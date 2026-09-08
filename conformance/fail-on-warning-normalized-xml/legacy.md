# Legacy differential

- namespace2xml 2.4.0: **agrees** on the expected tree and exit code, but only because it rejects
  the unknown options before XML normalization. Its diagnostics and execution semantics differ.
- Contract: Sections 11.7 and 21.2; Section 26 item 92.
- Clean behavior: normalization still emits `WARN007`; the opt-in warning policy therefore refuses
  publication and preserves the existing XML destination.
