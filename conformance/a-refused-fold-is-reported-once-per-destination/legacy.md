# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 17.5, 22 and 24.
- Legacy observation: destination folding had no merge strategies, so this condition did not exist
  and nothing was reported.
- Clean behavior: Section 16.10's `append` "needs a sequence contribution", and Section 17.5 folds
  a destination from the two contributions' view roots, so a `filemerge=append` between two mapping
  views is refused at the root of every destination it is declared on. Section 22 counts `TYPE001`
  "once per path and applicable source/output instance", which makes those separate occurrences.
- Why the case is shaped this way: Appendix A.2 spells a name as one or more components, so the
  root has no spelling and `path` is absent. A cardinality key built from the path alone is
  therefore the empty string at every destination, and one such key caps the whole run at one
  report -- the second destination is retired as a repeat of the first even though it is a
  different file failing for its own reasons. Two destinations are the smallest case that shows it.
- Appendix B gives `TYPE001` a `destination` member, and it is the only member that separates these
  two occurrences: they share a code, a phase, a spec anchor, a message, and an absent `path`. A
  fold diagnostic that omitted it would leave the reader unable to name either failing file.
- The two `WARN005` occurrences are the ordinary Section 17.5 collision warnings for the same two
  folds. They follow the errors because Section 24 orders diagnostics carrying a source ordering
  key before those carrying only a destination, and the refusals trace to source contributions
  while the collision warnings do not.
