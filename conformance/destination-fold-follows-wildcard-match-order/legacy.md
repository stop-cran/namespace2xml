# The destination fold follows wildcard match order

Acceptance item 26. Section 15.1 step 18, Section 17.5, Section 12.4.

## What the inputs ask for

`a.*.output=namespace` with `a.*.filename=out.properties` expands into two concrete output
instances, `a.zebra` and `a.alpha`, both writing `out.properties`. Each contributes a native
YAML sequence at `list` after its selector prefix is removed, so the fold has to decide which
sequence is allocated first.

## Why this fixture is about the fold key and nothing else

Section 17.5 folds contributions `strictly left to right` by four components. The first two are
identical here — one `output` declaration, one format — so the decision falls to component 3:

> 3. wildcard match order, as defined in Section 12.4;

Section 12.4 defines that order:

> Eligible items are enumerated in the first-appearance order of those depth-`k` nodes in the model
> being evaluated, which is the mapping order Section 5.2 preserves.

`zebra` is written first, so it matches first and is folded first. `alpha` follows, and
Section 17.5 rebases its implicit items above the accumulated high-water mark. The file therefore
reads `w`, `x`, `y`, `z`.

## The discrimination

The two names are chosen so that match order and selector order disagree: component 4 of the fold
key is "concrete selector encoded as UTF-8 and compared by unsigned-byte ordinal order", under which
`a.alpha` precedes `a.zebra`. An implementation that omitted component 3, or that reached for
component 4 first, folds `alpha` first and publishes `y`, `z`, `w`, `x`. Four lines in one
order are the whole assertion.

## Why the observable is a sequence and not a scalar

A scalar cannot see this. Section 4.4 settles a payload contest at a node by "the latest scalar/null
contribution", which is a source position and not a fold position, so two colliding scalars resolve
to the same value whichever way round the destination folds them. Sequence allocation is the
opposite: Section 17.5 rebases "an implicit item from a later output contribution ... onto the next
fresh destination ordering value", so the allocation is a direct function of fold order.

Writing this fixture with two scalars would produce a file that is identical under the correct rule
and under selector-byte order, and it would assert nothing.

## The warning

Section 17.5:

> For every destination collision, emit a warning identifying: destination path; earlier
> declaration; later declaration; merge or replacement decision.

`WARN005` carries `destination`; the rest of that list is in its message, which the corpus does
not compare. Exit code 0 asserts separately that a fold is a warning and not a failure.

## Not asserted

Components 1 and 2 of the fold key, which need two `output` declarations or a comma-separated
format list. The `filemerge` values other than the default. Both are covered by the sibling
fixtures for this item.