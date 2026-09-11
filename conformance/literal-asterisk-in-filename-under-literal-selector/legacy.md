# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 15.2 and 16.2; Section 26 item 6.
- Legacy observation: all ten Appendix C.6 samples exited 0 but ignored the literal filename,
  publishing `app.properties` instead of `star-%2A.conf`.
- Clean behavior: the selector defines no capture, so `*` is literal directive text and the
  portable filename algorithm encodes it as `%2A`.
- Intentional correction: Section 3.1 preserves explicit `filename` values as complete paths;
  capture substitution cannot discard a literal value when the selector defines no capture.
