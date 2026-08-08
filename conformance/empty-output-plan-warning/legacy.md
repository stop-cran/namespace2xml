# Legacy differential

- namespace2xml 2.4.0: **agrees** on the observable. A scheme that declares no `output`
  contributes no destinations, so 2.4.0 produces no files and exits 0 — which is exactly the
  case's expected exit 0 and empty tree. The `WARN008` diagnostic that the 3.0 stream carries is
  not part of the observable per Appendix C.6 (exit code and output tree only), so the agreement
  is genuine on what is checked. It is not evidence of compatibility on what is not checked: 2.4.0
  has no structured diagnostic stream and never announces that its output plan is empty, so a
  scheme that had been edited into declaring nothing was indistinguishable from a scheme that had
  been applied successfully.
- Contract: Section 22 `WARN008`; Section 21 validation gate. Section 3 does not enumerate this
  diagnostic; it is a new addition rather than a preservation or correction.
- Clean behavior: the validated output plan is checked for emptiness once per invocation and
  reports `WARN008`. The scheme here is well formed and its `merge` directive is honoured; it
  simply declares no destination, which is a warning rather than an error because writing nothing
  is a legitimate result of a filtered or partially-applied scheme.
- The input is deliberately non-empty: the warning is about the output plan, not about the absence
  of data, and a case with no input would not distinguish the two.
- `WARN008` declares no optional Section 6.4.3 members, so the occurrence is exactly the five
  required ones. It is the only diagnostic in the corpus whose whole content is its identity.
