# Legacy differential

- namespace2xml 2.4.0: **agrees** on the expected tree and exit code, but only because it rejects
  the unknown option before serialization. Its diagnostics and execution semantics differ.
- Contract: Sections 19.1 and 21.2; Section 26 item 92.
- Clean behavior: serialization still discovers `WARN013`, then the policy refuses publication and
  leaves the existing destination byte-identical.
