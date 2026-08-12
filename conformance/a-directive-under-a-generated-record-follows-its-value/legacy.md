# A directive under a generated record follows its value

Acceptance item 54. Section 15.1 step 16, Section 16.5.

## What the inputs ask for

`reg` is a mapping with two children of different shapes: `alpha` has a mapping projection, `beta` is
a bare scalar. `reg.key=name` turns the mapping into a sequence of records, and Section 16.5 puts the
two shapes at different depths:

> A child with a mapping projection becomes a record containing those mapping fields

so `alpha`'s own field stays at the top of its record, while

> A child without a mapping projection becomes a record containing the generated key field followed
> by `value` holding the complete child overlay

puts `beta` one level down. A `type=string` directive is bound at each child's pre-transformation
address.

## The rule the case is about

Section 15.1 re-addresses a directive bound beneath a reshaped node "together with the value it
bound to". Here the two values move to addresses of different lengths, and the case asserts that the
re-addressing follows the value rather than merely renaming a component: `reg.alpha.weight` becomes
`reg.0.weight`, and `reg.beta` becomes `reg.1.value`.

Both expected values are quoted strings. An unquoted `1` or `2` would mean the serializer looked the
directive up under an address the `key` transformation had already vacated — and it would say
nothing, because Section 15.2's warning is about a directive that bound *nowhere*, and both of these
bound.

## Why the two shapes are in one case

They are the same rule and different arithmetic. A re-addressing that recorded only the item's
ordering value would satisfy `alpha` and lose `beta`, and a case containing only `alpha` would
accept it.

## Not asserted

Section 16.5's record construction itself — the generated field's position, its string typing, and
the movement of comments — which `key-projects-an-ordered-mapping-as-records` and
`key-generates-a-string-name-and-moves-comments` pin.

## Legacy differential

- namespace2xml 2.4.0: **differs**, and loses data. It emits one record, for `alpha`, and drops
  `beta` from the output entirely. Exit `0`, and standard error carries nothing beyond the banner:
  the scalar child of a `key`-transformed mapping is discarded in silence.
- Contract: Section 15.1 step 16 re-addressing; Section 16.5 record construction for a child with
  and without a mapping projection.
- Clean behavior: two records, in source order, each carrying the generated `name` field first, with
  `alpha` keeping `weight` at the top of its record and `beta` placed under `value`; both scalars
  rendered as strings because a directive bound at each child's pre-transformation address followed
  it into the record.
