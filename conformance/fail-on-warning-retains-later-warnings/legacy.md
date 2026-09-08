# Legacy differential

- namespace2xml 2.4.0: **fails**. It does not recognize the 3.0 warning-publication policy.
- Contract: Sections 15.3, 15.4, and 21.2; Section 26 item 92.
- Clean behavior: an early scheme warning does not stop input and planning checks, all three
  warnings remain ordered by phase, and publication is refused only after serialization.
