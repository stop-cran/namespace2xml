# Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Section 6.2 and Section 26 item 93.
- Legacy observation: all ten Appendix C.6 samples exited 1 and published no `out.conf`.
- Clean behavior: post-`--` `-s` is a literal input filename and satisfies the pending `input`
  occurrence, so the run exits 0 and publishes `out.conf`.
