# Legacy differential

- namespace2xml 2.4.0: **fails**. It does not recognize the 3.0 warning-publication policy.
- Contract: Sections 7.2, 14.1, and 21.2; Section 26 item 92.
- Clean behavior: both warnings survive, exit code is 1, and the existing destination remains
  byte-identical because no publication operation occurs.
