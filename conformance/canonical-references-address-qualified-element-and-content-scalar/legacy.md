# Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Sections 11.4, 13.1, and 13.3; Section 26 item 9.
- Legacy observation: all ten Appendix C.6 samples exited 1 and published no
  `out.properties`.
- Clean behavior: canonical `Q{uri}` and `#n` references resolve the qualified element and mixed
  content scalar directly, producing both expected assignments.
- Intentional correction: canonical references must distinguish qualified elements and ordered
  content; accepting only ambiguous simple names would make those preserved XML values unreachable.
