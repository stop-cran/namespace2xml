# A cross-format collision replaces the earlier plan

Acceptance item 26. Section 15.1 step 18, Section 17.5.

## What the inputs ask for

Two literal output declarations name the same `filename`. `a` renders as `namespace` and `b`
as `ini`, and they contribute disjoint keys — `k` from `a`, `m` from `b`.

## What Section 17.5 requires

> If contributions have different output formats, the later contribution replaces the complete
> earlier file plan. This is a deterministic file-level override.

and, in more detail:

> A cross-format replacement discards the complete accumulated plan for that destination, including
> document data, comments, renderer state, sequence provenance, and every destination high-water
> mark.

`b` is later by component 1 of the fold key, output-declaration source order. So the published
`out.conf` is `b`'s plan alone.

## The discrimination

The two contributions supply *different* keys. That is deliberate: had they both supplied `k`, a
merge and a replacement would produce the same single line and the fixture would assert nothing. As
written, a merge would publish two lines and a replacement publishes one, so the expected file
distinguishes them.

The published file is INI, `b`'s format, which is the second half of "the complete earlier file
plan": renderer state is discarded with the data. A `namespace` rendering of `m=2` would be the
same bytes here, so the format is not independently pinned by this fixture — the key set is what it
asserts.

## Why this is not an error

> Cross-format collision is not a blocking error.

Exit code 0 with one `WARN005` is that sentence. Section 16.11 confirms `filemerge` is not
consulted:

> Cross-format collisions always use the deterministic complete-plan replacement rule in
> Section 17.5 rather than `filemerge`.

## Not asserted

The high-water reset the same clause requires. Dense rendering makes destination ordering values
invisible in every output format, so no fixture in this corpus can observe the reset; acceptance
item 64 records that gap explicitly rather than claiming coverage.