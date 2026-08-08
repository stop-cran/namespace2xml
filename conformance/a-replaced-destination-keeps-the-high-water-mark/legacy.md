# A replaced destination keeps the high-water mark

Acceptance item 26. Section 15.1 step 18, Section 17.5, Section 17.2, Section 5.4.

## What the inputs ask for

Three output declarations write `out.properties`, so step 18 folds three contributions in
declaration order.

`s1` contributes three explicitly indexed items at `b`, ordering values `0`, `1` and `2`. `s2`
contributes one native YAML item at `b`, which is implicit, and declares `filemerge=replace`. `s3`
contributes two explicitly indexed items, at `b.0` and `b.1`, under `filemerge=deep`.

## What Section 17.5 requires

The per-path accumulator rules apply whatever the strategy, and the strategy bullet says only what
`replace` removes:

> - an implicit item from a later output contribution is rebased onto the next fresh destination
>   ordering value;
> - an explicit item retains its supplied ordering value and patches an existing item at that value
>   under `deep`;
> - `replace` discards the visible accumulated projection without lowering the destination
>   high-water mark.

and the order of the two operations is fixed:

> A destination accumulator absorbs the incoming high-water mark for a path before allocating or
> patching incoming items at that path.

So: `s1` leaves `b`'s destination mark at `2`. `s2` discards the three items but does not lower that
mark, and its own implicit item — allocated in its source against a mark that began at -1 — is
rebased onto the next fresh destination value, `3`. `s3`'s explicit `0` and `1` retain their supplied
values; nothing occupies either, so both are added rather than patching. Three items survive, at
stable values `0`, `1` and `3`.

Section 5.4 then renders them with fresh dense indices, in ascending stable order: `b.0=w`,
`b.1=v`, `b.2=q`.

## The discrimination

The mark that `replace` must not lower is at `b`, one path *below* the path `filemerge=replace` is
declared on. An implementation that kept only the mark at the declaration's own path restarts `s2`'s
item at `0`, where `s3`'s explicit `0` patches it; two items are published instead of three.

An implementation that keeps the mark at `b` but does not absorb it before allocating — taking the
incoming contribution's own mark of `-1` instead of the accumulated `2` — restarts `s2`'s item at
`1`, where `s3`'s explicit `1` patches it. Two items again, and a different pair.

The item **count** is what separates all three readings, which is what makes this fixture
load-bearing where a two-contribution one would not be. Section 5.4 renumbers the survivors densely
from zero, so after a `replace` that discards everything, one item at stable `0` and one at stable
`3` both render as `b.0`. Only a third contribution addressing explicit ordering values can make the
difference visible, and only if it addresses values the correct reading leaves free.

`s3` addresses `0` and `1` rather than `3` deliberately. Addressing `3` would put the readings'
disagreement inside a patch — two nodes meeting at one ordering value — and Section 17.1 decides that
by payload mark, which would make the fixture assert a second rule at the same time. Addressing the
two values the wrong readings would land on instead keeps every disagreement in the count.

## Why the earlier items are gone

Section 17.2 is explicit that the mark and the projection part company:

> `merge=replace` removes the earlier visible sequence projection but does not lower the path's
> allocation high-water mark.

`x`, `y` and `z` are absent from the expected file for that reason, while the mark they raised is
still doing work three lines later.

## Not asserted

The mark of a path the replacement removes and does not itself name. Section 17.5 asks a
contribution to carry its "complete per-path high-water map", and this implementation carries marks
on the overlay tree rather than in a separate map, so a path with no surviving node has nowhere to
keep one. Reaching that case needs a fourth contribution: one to raise the mark, one to replace
without naming the path, one to recreate it, and one to address an explicit value on it.
KNOWN-LIMITS.md records it.

Also not asserted: `append` at a destination fold, and the cross-format replacement that discards
every destination high-water mark, which
`cross-format-replacement-renumbers-the-destination-order` covers.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 3.2 correction that collisions between output instances must use `filemerge`
  rather than `merge`; Section 3.2 removal of behaviour dependent on shared mutable array-index
  state; Section 15.1 step 18; Section 17.5 destination high-water rule.
- Legacy observation: the baseline writes `out.properties` with different bytes from the expected
  file. The measurement records `content out.properties`, exit `0`, and no standard error beyond
  the banner.
- Clean behavior: three items survive the fold at stable ordering values `0`, `1`, and `3`, and
  Section 5.4 renders them densely as `b.0=w`, `b.1=v`, `b.2=q`. The `replace` accumulator holds
  the high-water mark that carries `s2`'s implicit item onto value `3` above `s3`'s explicit `0`
  and `1`.
- Why the difference is intentional: 2.4.0 had no `filemerge` directive, so `s2.filemerge=replace`
  and `s3.filemerge=deep` were not recognized as strategy declarations at all. The baseline's
  destination fold ran under whatever legacy sequence-merge logic it had and produced a different
  content; the observable divergence names the file rather than a mechanism because the baseline's
  strategy vocabulary is not the same one this case pins. Which of the readings enumerated in the
  discrimination above the baseline lands on is not something this fixture is designed to
  identify.
