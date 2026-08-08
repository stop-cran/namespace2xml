# `filemerge=error` rejects a second contribution

Acceptance item 26. Section 15.1 step 18, Section 16.11, Section 17.5.

## What the inputs ask for

`a.*.output=namespace` and `a.*.filename=out.properties` send both `a.x` and `a.y` to one
destination, exactly as the sibling fold fixtures do, and `a.*.filemerge=error` refuses it.

## What Section 16.11 requires

> `error`: any second contribution to that destination is a blocking collision error.

Appendix B maps that condition to `COLLISION001`, whose cardinality is "once per rejected
contribution after the first". Two contributions produce one rejection.

## Why `declaration` names the `filemerge` declaration

`COLLISION001` carries one `declaration` member, and Section 22 says a member names "where the
condition is, not where its subject came from". Both rejected contributions were created by the same
`a.*.output` declaration, so naming it would not tell a reader which of the run's settings refused
the fold — it would name something the two contributions have in common rather than the thing that
made them an error. The declaration the condition is *about* is `a.*.filemerge`.

The same reasoning fixes `PATH001` on `a.filename` in the sibling filename fixtures.

## The discrimination

Exit code 1 with an empty expected tree asserts that the rejection is blocking and happens in phase
`planning`, before publication: a run that wrote `out.properties` and then failed, or that folded
and warned, would both be caught. No `WARN005` accompanies it, because nothing was folded — the
Section 17.5 warning reports a "merge or replacement decision", and this run made neither.

## Not asserted

That `filemerge=error` is not sticky, which Section 16.11 states separately and which needs a
second declaration to observe. Nor a third contribution, which the cardinality rule says would be
rejected again.