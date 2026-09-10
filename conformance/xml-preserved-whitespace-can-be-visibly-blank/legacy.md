# Legacy differential

- namespace2xml 2.4.0: **differs**. It had no distinction between generated formatting and
  preserved XML whitespace.
- Contract: Sections 11.4, 19.8, 24, and Section 26 item 97.
- Clean behavior: a visually blank line survives only because it is input content, not because the
  formatter inserted it.
