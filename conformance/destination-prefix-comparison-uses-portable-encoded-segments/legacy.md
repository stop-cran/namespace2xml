# Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Sections 16.2 and 17.5; Section 26 item 95.
- Legacy observation: all ten Appendix C.6 samples exited 134 after publishing `out/a`; an
  unhandled `IOException` treated the captured `a/b` as a path beneath that file instead of
  encoding it as one segment.
- Clean behavior: capture encoding produces sibling files `out/a` and `out/a%2Fb`, so no
  destination-prefix collision exists.
- Intentional correction: Sections 3.2 and 16.2 require portable, pre-publication path handling;
  capture data cannot acquire directory semantics or trigger an unhandled user-input exception.
