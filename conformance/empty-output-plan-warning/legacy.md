# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 22 `WARN008`; Section 21 validation gate.
- Legacy observation: a run whose scheme declared no output produced no files and said nothing, so
  a scheme that had been edited into declaring nothing was indistinguishable from a scheme that had
  been applied successfully. The exit status was the same in both cases.
- Clean behavior: the validated output plan is checked for emptiness once per invocation and
  reports `WARN008`. The scheme here is well formed and its `merge` directive is honoured; it
  simply declares no destination, which is a warning rather than an error because writing nothing
  is a legitimate result of a filtered or partially-applied scheme.
- The input is deliberately non-empty: the warning is about the output plan, not about the absence
  of data, and a case with no input would not distinguish the two.
- `WARN008` declares no optional Section 6.4.3 members, so the occurrence is exactly the five
  required ones. It is the only diagnostic in the corpus whose whole content is its identity.
