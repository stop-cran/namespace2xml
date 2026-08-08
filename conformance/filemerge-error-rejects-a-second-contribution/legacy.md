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

## Legacy differential

- namespace2xml 2.4.0: **differs**. The baseline exits 0 and writes `out.properties` (the harness
  records `extra out.properties`), where the case expects exit 1 and an empty tree.
- Contract: Section 3.2 lists as a corrected defect legacy behavior "caused by relying on `merge`
  to control collisions between output instances; such schemes must use `filemerge`, while `merge`
  remains recognized with input/common-model scope". Section 16.11 defines `filemerge=error`.
- Legacy observation: `filemerge` is not a 2.4.0 scheme directive. The baseline had no way to
  express a blocking cross-destination-contribution rejection, so it merged the two contributions
  under its default behavior and published `out.properties`. It did not name the directive it did
  not recognize, and the observable is a successful run.
- Clean behavior: `filemerge=error` at `a.*` refuses the second contribution to one destination
  with `COLLISION001` in phase `planning`, before publication, and no file is written.
- The difference is intentional: Section 3.2's `merge`/`filemerge` split exists precisely to give
  scheme authors a way to reject a second contribution to one destination, and 2.4.0 had no such
  facility for the caller to reach.
