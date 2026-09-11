# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 11.7 and 16.8; Section 26 item 16.
- Legacy observation: all ten Appendix C.6 samples exited 0 but emitted
  `<r a="" />` instead of the selected XML element tree.
- Clean behavior: the later complete `NormalizeFormattingWhitespace` directive replaces
  `PreserveWhitespace`, emits `WARN007`, and preserves `<a>1</a>` while removing only formatting
  whitespace.
- Intentional correction: Section 3.1 expressly preserves later-entry override precedence, so an
  earlier option set cannot remain effective after a later complete replacement.
