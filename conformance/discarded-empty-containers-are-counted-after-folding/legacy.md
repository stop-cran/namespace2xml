# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 3.3, 17.5, and 19.2; Section 26 item 98.
- Legacy observation: the baseline silently omitted every explicit empty container it selected.
- Clean behavior: only the final folded destination is diagnosed; its nested and repeated empty
  mappings and sequences are summarized in one `WARN015`, while the replaced contribution adds no
  discarded-container count and the synthetic `root` wrapper is not counted.
