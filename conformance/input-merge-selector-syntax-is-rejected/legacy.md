# Legacy differential

- namespace2xml 2.4.0: **agrees**. It exits 1 and publishes no output, matching the Appendix C.6
  exit/tree oracle even though the clean implementation rejects the declarations through its
  explicit input-merge grammar.
- Contract: Section 16.10 and Section 26 item 94.
- Clean behavior: wildcard and unescaped reference syntax are rejected once per declaration.
