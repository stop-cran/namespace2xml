# One destination folds by format before match order

Acceptance item 26. Section 15.1 step 18, Section 17.5.

## What the inputs ask for

`a.*.output=namespace,ini` with a single `a.*.filename=out.conf` sends **four** contributions to
one destination: two wildcard matches, each rendered in two formats. `a.zebra` supplies `first`
and `a.alpha` supplies `second`.

## Why this fixture exists

Section 17.5 folds contributions `strictly left to right` by a four-component key whose second and
third components are

> 2. format ordinal within one comma-separated `output` value;
> 3. wildcard match order, as defined in Section 12.4;

An implementation naturally produces these four contributions in the order it expanded them, which
is match order *outside* format ordinal — the opposite nesting. The sort is what corrects it, and
this is the only shape in which the correction is observable, which is why the sibling fold fixtures
do not catch a missing sort.

Section 17.5 also forbids the shortcut that would make the nesting moot:

> Implementations must not group by format before folding.

## What the two orders produce

Sorted by the fold key, the four contributions are namespace/zebra, namespace/alpha, ini/zebra,
ini/alpha:

1. namespace/zebra accumulates;
2. namespace/alpha folds into it — same format, `filemerge=deep`;
3. ini/zebra is cross-format, so it "replaces the complete earlier file plan";
4. ini/alpha folds into that — same format again.

Both keys survive, because step 4 merges rather than replaces. The file is `first=1` then
`second=2`.

In arrival order the contributions alternate format — namespace/zebra, ini/zebra, namespace/alpha,
ini/alpha — so **every** fold after the first is cross-format and each one discards everything
before it. Only `a.alpha`'s key would survive, and the file would be `second=2` alone.

## The three warnings

Section 22 counts `WARN005` "once per folded contribution pair", and four contributions at one
destination make three pairs. The count is part of the assertion: a cardinality key that named only
the later contribution would see pairs 1 and 3 as one occurrence — both are (zebra, alpha) — and
report two warnings for four contributions.

The array holds three structurally identical objects because `WARN005` carries only
`destination`; what distinguishes the three pairs lives in the message, which the corpus does not
compare.

## Not asserted

Which of the two formats the published bytes are in. `first=1` and `second=2` are the same bytes
under the namespace and INI serializers for data this flat, so the format is pinned only indirectly,
through the key set that survives. A fixture for the renderer-state half of "the complete earlier
file plan" would need a format pair whose serializations differ on the same model.

## Legacy differential

- namespace2xml 2.4.0: **differs**. The baseline writes `out.conf` with different content than
  the case expects (the harness records `content out.conf`); the exit code matches.
- Contract: Section 3.2 lists as a corrected defect legacy behavior "dependent on parallel
  execution order" and "dependent on shared mutable array-index state". Section 17.5 defines
  the four-component fold key that carries the correction.
- Legacy observation: 2.4.0 had no four-component fold key. Contributions to a destination were
  folded in the order the expansion happened to produce them, which nests match order outside
  format ordinal — the opposite of the specified nesting — so at this destination the four
  contributions alternate format instead of grouping by format, and every fold after the first
  is cross-format. Under a fold that "replaces the complete earlier file plan" on a cross-format
  boundary, only the last contribution's key survives, and the baseline `out.conf` therefore
  carries a single line rather than the two lines the specified sort produces. The measurement
  records only that the bytes differ; the specific one-line reduction is implementation-defined
  for the baseline.
- Clean behavior: sorted by the fold key the four contributions are namespace/zebra,
  namespace/alpha, ini/zebra, ini/alpha. Same-format neighbours fold under `filemerge=deep`,
  the sole cross-format boundary in the middle replaces once, and both keys survive as
  `first=1` then `second=2`. Section 17.5 additionally forbids grouping by format before
  folding, and this fixture is the only shape in the corpus where the correction is
  observable — the sibling fold cases have either one format or one match.
- The difference is intentional: without the specified sort, the survivor at a destination is a
  property of the expansion pipeline's iteration order rather than of the fold rules the
  scheme author wrote, which is exactly the class of parallel/iteration-order dependence
  Section 3.2 exists to remove.
