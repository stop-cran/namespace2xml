# A wildcard output selector that matches nothing warns and creates no file

Acceptance item 75. Section 14.1.

## What the inputs ask for

Two `output` declarations. `a.output=namespace` matches the data. `b.*.output=namespace` matches
nothing at all, because there is no `b` subtree.

Section 14.1:

> A wildcard output declaration that produces no concrete selector instance emits `WARN009` and
> creates no file.

This is the one case Section 14.1's empty-view rule does **not** cover. A literal declaration whose
subtree is missing is still "a planned output even when its selected view contains no surviving
payload", and writes an empty file. A wildcard declaration that matched nothing has no concrete
selector to plan *with*, so there is nothing to write and nothing to name it.

The literal `a` declaration is present so the run still produces a file. Without it the plan would
be empty and the run would additionally emit `WARN008`, and this case is about `WARN009`.

## Why the diagnostic carries these members and not others

Section 22 supplies a member when the condition itself has the fact that member names.

- `source` and `line` — the condition is a property of the written declaration, and the declaration
  is at line 2 of the scheme. This is not the tie-breaker case: the condition genuinely *is* at that
  record, rather than being about something that came from it.
- `column` — absent. The condition is about the whole record, not a position inside it, and Section
  22 makes `column` "one position within that record".
- `path` — the selector that matched nothing, `b.*`.
- `declaration` — `b.*.output`, the directive's canonical spelling. It names the directive, not the
  value; the same spelling `WARN009` already uses for an unbound directive in Section 15.2.
- `destination` — absent. No destination was ever planned, which is the finding.

The anchor is `§14.1` rather than `§15.2`. `WARN009` covers two conditions — a directive binding to
no output, and a wildcard output creating no instance — and only the anchor distinguishes them.

## Not asserted

The message prose, which Appendix C.4 exempts. The exit code is 0: `WARN009` is a warning, and
Section 6.3 reserves a nonzero code for a blocking error.

## Legacy differential

- namespace2xml 2.4.0: **agrees**.
- Contract: Section 3.1 preserves the existing wildcard-output declaration and output-format
  names. The substantive rules under test are Section 14.1's empty-selector clause and Section
  15.2's `WARN009` binding, and Section 3 does not enumerate them individually.
- Legacy observation: the baseline writes `a.properties` byte-identically with the expected
  content, writes no file for `b.*`, and exits `0` with no standard error beyond the banner.
- Why the observable agreement is not compatibility evidence: this case's expected result and the
  baseline's observed result coincide on the tree and the exit code, but the case exists to pin
  `WARN009` and the observation is silent about it. Diagnostics are not part of the observable
  the verdict is claimed against. The baseline has no `WARN009` code and no stated model for a
  wildcard output declaration that literalizes to nothing; producing the same tree therefore does
  not tell whether it consulted the same rule or arrived at "no file" by writing nothing when
  nothing matched. Discrimination of the warning belongs to `expected-diagnostics.json`, not
  here.

