# Legacy differential

- namespace2xml 2.4.0: **differs**. Its delegated command-line parser had no specified
  host-argument boundary.
- Contract: Section 6.2 and Section 26 item 93.
- Clean behavior: the two path arguments remain individual tokens even though each contains a
  space.
