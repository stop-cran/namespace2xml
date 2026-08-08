# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 15.2, 14.1 and 22.
- Legacy observation: output-view transformations were not scoped to an output instance, so a
  directive that could not be applied was reported once for the run, whichever files it affected.
- Clean behavior: Section 15.2 evaluates `type`, `key`, and output-view ignores "against absolute
  stable pre-transformation paths in every output instance containing the path", and Section 14.1
  says nested output declarations "intentionally may select overlapping data and create duplicate
  content in separate files". Section 22 therefore counts `TYPE001` "once per path and applicable
  source/output instance".
- Why the case is shaped this way: `app.x.k` lies inside both the `app` instance and the `app.x`
  instance, so `app.x.k.type=array` is applied twice and refused twice. Those are two facts about
  two files. A cardinality key naming only the declaration and the path collapses them, and the
  run then reports one of the two files it failed to produce.
- The `solo` declaration closes the opposite reading. Section 14.1 makes the *selector* create an
  instance, so `output=namespace,ini` is one instance rendered twice, not two instances, and its
  refusal is reported once. A key that separated views by format would report it twice.
- The two `app.x.k` occurrences are byte-identical because Appendix B gives `TYPE001` no member
  naming an output instance. That is the specified member set, and the count is the assertion here.
