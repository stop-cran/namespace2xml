# Legacy differential

- namespace2xml 2.4.0: **fails**. It does not recognize the 3.0 warning-publication policy.
- Contract: Sections 19.1 and 21.2; Section 26 item 92.
- Clean behavior: serialization still discovers `WARN013`, then the policy refuses publication and
  leaves the existing destination byte-identical.
