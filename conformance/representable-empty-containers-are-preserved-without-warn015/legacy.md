# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 3.3 and 19.1-19.5; Section 26 item 98.
- Legacy observation: the baseline differs on at least one normalized byte representation.
- Clean behavior: namespace, JSON, and YAML preserve both empty-container categories, and XML
  preserves an empty mapping as an empty element; none of these representable shapes emits
  `WARN015`.
