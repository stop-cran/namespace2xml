# A replacement keeps the mark of a path it does not name

Acceptance item 26. Section 15.1 step 18, Section 17.5, Section 5.4, Section 5.2.

## What the inputs ask for

Four output declarations write `out.properties`, so step 18 folds four contributions in declaration
order.

`s1` contributes three explicitly indexed items at `b`, ordering values `0`, `1` and `2`. `s2`
contributes a single scalar at `c` and declares `filemerge=replace`; it does not name `b`. `s3`
contributes one native YAML item at `b`, which is implicit, under `filemerge=deep`. `s4` contributes
one explicitly indexed item at `b.0`, also under `filemerge=deep`.

`a-replaced-destination-keeps-the-high-water-mark` covers the case where the replacement does name
the path whose mark must survive. This one covers the case where it does not, which is a different
sentence of Section 17.5.

## What Section 17.5 requires

The high-water map a contribution carries is not restricted to paths that contribution renders:

> Every output contribution carries its complete per-path high-water map, including marks raised by
> items hidden by output projection.

and what `replace` removes is the projection, not the map:

> - `replace` discards the visible accumulated projection without lowering the destination
>   high-water mark.

A path the replacement does not name is exactly a path whose items are hidden and whose mark the
word "complete" keeps. The absorb-then-allocate order then applies as it does anywhere else:

> A destination accumulator absorbs the incoming high-water mark for a path before allocating or
> patching incoming items at that path.

So: `s1` leaves `b`'s destination mark at `2`. `s2` discards every visible item without lowering it.
`s3`'s implicit item at `b` is rebased onto the next fresh destination value, `3`. `s4`'s explicit
`0` retains its supplied value; nothing occupies `0`, so it is added rather than patching. Two items
survive at stable values `0` and `3`, and Section 5.4 renders them densely in ascending stable order
as `b.0=w`, `b.1=q`.

## The discrimination

The mark that must survive is at a path the replacement never mentions. An implementation that
carries marks only for paths the replacement also names drops `b`'s mark with `b`'s items, so `s3`'s
implicit item restarts at `0`, where `s4`'s explicit `0` meets it. One item is published instead of
two, and one of the two values is destroyed silently. Which one depends on how the losing
implementation resolves two contributions at a single ordering value, which is why this case pins
the count and the surviving pair rather than the mechanism of the loss.

As in the neighbouring case, the item **count** is what separates the readings. Section 5.4
renumbers survivors densely from zero, so an item at stable `0` and an item at stable `3` both
render as `b.0`. Only a fourth contribution addressing an explicit ordering value the wrong reading
would land on can make the difference visible, which is why `s4` addresses `0` rather than `3`.

## What the mark does not carry

`c` is expected before `b`, and that is the second assertion here.

Section 17.5 enumerates one thing that survives a `replace`: the high-water mark. The visible
projection is discarded, and with it `s1`'s contributions to `b`. Section 5.2 orders mapping keys by
"the earliest contribution that required the key", and after the replacement the earliest surviving
contribution requiring `b` is `s3`, which follows `s2`'s contribution of `c`. A mark carrier that
also preserved `s1`'s position would sort `b` first, reinstating an order whose only remaining
evidence Section 17.5 has just thrown away.

## Why the removed path does not reappear

A namespace destination renders nothing for a path with no items, whether or not the fold left a
mark behind for it, so this case cannot see the difference and does not claim to.
`a-replacement-leaves-no-trace-of-a-path-it-removed` renders the same situation to YAML, where an
implementation that keeps a mark on the overlay tree emits the removed path as an empty mapping.

## Not asserted

`append` at a destination fold, and the cross-format replacement that discards every destination
high-water mark, which `cross-format-replacement-renumbers-the-destination-order` covers.

Whether a mark survives a replacement into a *later* destination fold for a different file: Section
17.5 scopes the accumulator to one destination, and no case here crosses that boundary.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 3.2 correction that collisions between output instances must use `filemerge`
  rather than `merge`; Section 3.2 removal of behaviour dependent on shared mutable array-index
  state; Section 15.1 step 18; Section 17.5 complete per-path high-water map.
- Legacy observation: the baseline writes `out.properties` with different bytes from the expected
  file. The measurement records `content out.properties`, exit `0`, and no standard error beyond
  the banner.
- Clean behavior: `b`'s high-water mark of `2` survives a `replace` that never names `b`, so `s3`'s
  implicit item lands at `3` and `s4`'s explicit `0` adds rather than overwrites. Two items are
  published, `b.0=w` and `b.1=q`, after `c=1`.
- Why the difference is intentional: 2.4.0 had no `filemerge` directive, so `s2.filemerge=replace`
  was not recognized as a strategy declaration and no replacement happened at all. The baseline
  appends each contribution to the file as it is reached, emitting `b.0` three times with three
  different values in one properties document. Which of those a consumer sees depends on its
  duplicate-key policy, which is precisely the class of shared-array-index behaviour Section 3.2
  removes.
