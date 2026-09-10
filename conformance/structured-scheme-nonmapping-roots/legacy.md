# Legacy differential

- namespace2xml 2.4.0: **differs**. It had no typed JSON/YAML scheme-root contract.
- Contract: Section 15 and Section 26 item 94.
- Clean behavior: each valid nonmapping root is `SCHEME003`; the valid sibling source is checked in
  the same phase but no output is published after the blocking failures.
