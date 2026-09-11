# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 5.4, 8.6, 13.1, and 19.1; Section 26 item 48.
- Legacy observation: all ten Appendix C.6 samples exited 0 and resolved `${a.2}` to `two`, but
  rendered the surviving item under sparse key `2` instead of dense visible key `1`.
- Clean behavior: references continue to address stable ordering value `2`, while namespace
  output independently densifies the visible surviving items to keys `0` and `1`.
- Intentional correction: Section 3.2 rejects shared mutable array-index behavior; stable internal
  identity and dense destination projection must not be conflated after deletion.
