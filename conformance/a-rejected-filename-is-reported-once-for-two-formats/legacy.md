# A rejected `filename` is reported once, not once per format

Acceptance item 51. Sections 16.2 and 22.

## What the inputs ask for

`a.output=namespace,ini` names two formats for one selector, and `a.filename=../bad.conf` gives them
a destination path that Section 16.2 prohibits:

> Statically written `.` and `..` segments are prohibited.

## Why one diagnostic and not two

Section 22's diagnostic table fixes the cardinality of `PATH001` as **once per destination**, and
Section 16.2 fixes how many destinations two formats and one explicit `filename` make:

> An explicit `filename` value is the complete relative destination path and is used verbatim after
> the portable path processing below. A format extension is never appended to it.

Both formats therefore compose the same relative path. That is one destination, so it is one
diagnostic. Only the *default* names in Section 16.2's table carry a per-format extension, and only
those produce a distinct destination per format.

This is the whole substance of the case, and it is invisible to every other kind of assertion. The
exit code is 1 either way, the output tree is empty either way, and both diagnostics carry the same
code, severity, phase, declaration and anchor — so a run that emits the duplicate is
indistinguishable from a correct one except by counting. The conformance comparer compares the
diagnostic array element by element, which is what makes the count assertable at all.

## Not asserted

The message text, which is not part of the contract.

The cardinality of `PATH001` when the destinations really are distinct. `a.output=namespace,ini`
with no `filename` composes `a.properties` and `a.ini`, which are two destinations and would be two
diagnostics — but a selector-derived default name cannot produce a prohibited segment, because
Section 16.2 step 7 renames a dot-segment or reserved-device condition with a `%5F` prefix rather
than rejecting it. There is no input that reaches that branch, so this fixture does not pin it.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 16.2 "statically written `.` and `..` segments are prohibited"; Section 22
  cardinality "once per destination"; Section 26 item 51. Section 3 does not enumerate the
  traversal-rejection correction as its own bullet — this is a substantive rule of Section 16.2
  rather than a compatibility-versus-correction line item.
- Legacy observation: the baseline exits `0` with no standard error beyond its banner. Nothing is
  rejected. The measurement records `exit 0 (expected 1)` and no output-tree divergence because
  the case's expected tree is empty either way.
- Clean behavior: `a.filename=../bad.conf` is a prohibited written `..` segment, and Section 16.2
  fails the plan with one `PATH001` diagnostic covering both formats, because two formats sharing
  one explicit `filename` compose one destination.
- Why the difference is intentional: 2.4.0 resolved `filename` values against the process working
  directory with no traversal check, so `../bad.conf` was an ordinary relative path and the plan
  proceeded. The 3.0 correction refuses that path outright rather than write outside a
  configured output root. This case cannot separately observe the once-per-destination
  cardinality against the baseline: the baseline emits no `PATH001` at all.