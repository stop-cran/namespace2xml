# Legacy differential

- namespace2xml 2.4.0: **differs**. Its post-delimiter option handling had no contract.
- Contract: Section 6.2 and Section 26 item 93.
- Clean behavior: post-`--` `-s` is a literal input value, so the required `scheme` option is absent.
