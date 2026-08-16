# A replacement leaves no trace of a path it removed

Acceptance item 26. Section 15.1 step 18, Section 17.5, Section 5.4.

## What the inputs ask for

Two output declarations write `out.yaml`, so step 18 folds two contributions in declaration order.

`s1` contributes two explicitly indexed items at `d`, ordering values `0` and `1`. `s2` contributes
a single scalar at `c` and declares `filemerge=replace`; it does not name `d`.

Nothing recreates `d`. That is the whole point of the case.

## What Section 17.5 requires

`s1` raises a destination high-water mark at `d`, and Section 17.5 keeps it across the replacement:

> - `replace` discards the visible accumulated projection without lowering the destination
>   high-water mark.

The mark exists so that a *later* contribution recreating `d` allocates above the discarded items
rather than reusing their ordering values. Here there is no later contribution, so the mark is never
read.

What Section 17.5 keeps is a mark, not a path. The map is described as a property the contribution
carries into the fold — "every output contribution carries its complete per-path high-water map" —
and the fold is over once the last contribution has been absorbed. The document that remains is the
one the surviving contributions describe, and no surviving contribution describes `d`.

`out.yaml` therefore contains `c: 1` and nothing else.

## The discrimination

A namespace destination cannot see this. It renders one line per scalar, so a path with no scalar
beneath it produces no output whether or not it is present in the model, and
`a-replacement-keeps-the-mark-of-a-path-it-does-not-name` is silent about it for that reason.

YAML is exclusive: it renders the container itself. An implementation that holds high-water marks on
the overlay tree must materialise a node to keep the mark of a path the replacement removed, and
that node is a node the renderer walks. It emits

```yaml
c: 1
d: {}
```

putting back, as an empty mapping, a path `filemerge=replace` was asked to remove. The expected file
pins the absence.

The two items at `d` rather than one are deliberate. A single item at `d.0` leaves the high-water
mark at `0`, which some implementations spell the same way as "no mark raised"; two items put the
mark at `1`, where it cannot be confused with the initial state and the carrier cannot be optimised
away by accident.

## Not asserted

The value of the retained mark, which only a case that recreates the path can observe, and which
`a-replacement-keeps-the-mark-of-a-path-it-does-not-name` observes.

Whether the same holds for JSON and XML. The rule is a property of the fold rather than of a
renderer, and YAML is the shortest destination that can see it.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 3.2 correction that collisions between output instances must use `filemerge`
  rather than `merge`; Section 15.1 step 18; Section 17.5 `replace` at a destination fold.
- Legacy observation: the baseline writes `out.yaml` with different bytes from the expected file.
  The measurement records `content out.yaml`, exit `0`, and no standard error beyond the banner.
- Clean behavior: `d` is removed by the replacement and nothing recreates it, so the destination
  contains `c: 1` alone.
- Why the difference is intentional: 2.4.0 had no `filemerge` directive, so `s2.filemerge=replace`
  was not recognized as a strategy declaration and no replacement happened. The baseline emits both
  contributions, `d` with its two items followed by `c`, which is the merge this case exists to
  replace.
