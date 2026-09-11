# Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Section 17.5 and Section 26 item 95.
- Legacy observation: all ten Appendix C.6 samples exited 134 with an unhandled
  `UnauthorizedAccessException` after publishing partial `TREE` and `tree` directory trees.
- Clean behavior: reversing declarations reverses the source-ordered `PATH001` stream while
  preserving the same collision set, and validation completes before publication.
