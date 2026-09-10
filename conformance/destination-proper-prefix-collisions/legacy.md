# Legacy differential

- namespace2xml 2.4.0: **differs**. It had no complete-plan portable destination topology check.
- Contract: Section 17.5 and Section 26 item 95.
- Clean behavior: each descendant receives one `PATH001`, including case-folded descendants and a
  descendant behind more than one conflicting ancestor.
