# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 8.6 and 13.1; Section 26 item 34.
- Legacy observation: all ten Appendix C.6 samples exited 0 and published `app.properties`,
  retaining a reference target that the permanent mask removed.
- Clean behavior: the masked target is missing when references resolve, so `REFERENCE002` blocks
  publication.
- Intentional correction: Section 3.1 preserves profile ignores and value references together;
  resolving through a permanently ignored target would silently restore data the author removed.
