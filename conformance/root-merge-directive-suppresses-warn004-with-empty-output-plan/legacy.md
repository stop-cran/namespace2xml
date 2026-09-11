# Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Sections 8.7 and 16.10; Section 26 item 45.
- Legacy observation: all ten Appendix C.6 samples exited 1 instead of honoring the root
  `merge=append` directive and completing an empty output plan.
- Clean behavior: the directive suppresses `WARN004` for the two native root sequences; only
  `WARN008` remains and the run exits 0.
- Intentional correction: Section 3.1 preserves the `merge` directive and its input-model scope;
  an empty output plan does not erase the directive's effect or turn warnings into an input error.
