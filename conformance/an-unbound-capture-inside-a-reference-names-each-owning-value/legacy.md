# Legacy differential

- namespace2xml 2.4.0: **crashes**. As above, there is no baseline diagnostic stream to compare.
- Contract: Section 13.3; Section 14.4; Section 22; Appendix A.4.
- Clean behavior: `a.*[0].copy=${a.*[9]}` writes a capture the owning template does not bind.
  Appendix A.4 calls that a free capture and makes it `REFERENCE001`; Section 13.3 says "a reference
  inside a wildcard template may contain only explicit captures already bound by that same
  template". Two names match, so the stream carries **two** diagnostics, one per owning value,
  matching Section 22's "once per reachable owning value".
- The count is the assertion, and it is the opposite of the companion case's. It is not a stylistic
  choice: Section 14.4 suppresses a reference error "in entries unreachable from every concrete
  output instance", so whether this condition is reported at all is decided per owning value. A
  once-per-rule diagnostic could not say "these two of five hundred", which is why the same
  authoring mistake is counted one way outside a reference and another way inside one.
- The phases differ for the same reason. A capture the rule does not define is refused while the
  entry is read; a reference is resolved only once the Section 14.4 closure is known, which is why
  this case reports `planning` and the companion reports `input`.
- Both diagnostics name `line` 3 -- one line of source, two owning values -- so `path` is what
  distinguishes them, and it is a projected output key here rather than a rule name, which is
  exactly the distinction Section 22 draws between `path` and `rule`.