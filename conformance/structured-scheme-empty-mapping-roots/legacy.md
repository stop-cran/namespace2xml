# Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Section 15 and Section 26 item 94.
- Legacy observation: all ten Appendix C.6 samples exited 134 with an unhandled
  `InvalidCastException` from `SchemeError` to `SchemeNode`.
- Clean behavior: valid empty mappings contribute no directives and only the empty-plan warning
  remains, so the run exits 0.
