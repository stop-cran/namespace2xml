# Legacy differential

- namespace2xml 2.4.0: **fails**. It does not recognize the 3.0 warning-publication policy.
- Contract: Sections 6.2 and 21.2; Section 26 item 92.
- Clean behavior: repeating the valueless option is idempotent, and a warning-free run publishes
  the same bytes and exits 0.
