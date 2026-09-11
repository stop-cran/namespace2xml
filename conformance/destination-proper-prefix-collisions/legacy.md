# Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Section 17.5 and Section 26 item 95.
- Legacy observation: all ten Appendix C.6 samples exited 134 with an unhandled `IOException`
  after creating an unexpected partial `tree` destination.
- Clean behavior: each descendant receives one `PATH001`, including case-folded descendants and a
  descendant behind more than one conflicting ancestor, before any destination is published.
