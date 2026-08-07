# `type=mapping` keeps numeric keys as keys and `type=array` discards names

Acceptance item 54. Section 16.6.

## What the inputs ask for

Two sibling subtrees, each carrying one half of the item.

`cfg.m` has the keys `0` and `1`. Section 8.7 makes that "a nonempty mapping containing only
canonical nonnegative decimal keys", so pipeline step 11 projects it as an indexed sequence before
step 16 ever runs. `cfg.m.type=mapping` is the directive Section 16.6 describes as

> the explicit escape hatch for preserving numeric keys as mapping keys rather than projecting them
> as an array

and Section 16.6 says a "sequence projection becomes a mapping whose keys are the stable ordering
values rendered as canonical decimal strings".

`cfg.s` has the keys `b` and `c`, which are not ordering values, and `cfg.s.type=array` converts
that mapping. Section 16.6:

> otherwise every key is discarded and every child—including in-range numeric and out-of-range
> decimal children—receives a fresh implicit ordering value above the node's current high-water mark
> in current mapping order

## How a mapping is made observable in a flat format

Section 19.1 spells a sequence item and a canonical numeric mapping key identically, so
`type=mapping` alone cannot be seen in namespace output: `m.0=x` is what both would write. The proof
has to be what a *later* pass then meets.

`cfg.m.key=name` is that later pass. Section 16.5 requires an ordered mapping — "applying `key` to a
sequence-only or scalar-only target is `TYPE001`" — and step 16 runs `type` before `key`. Without
`type=mapping` this scheme is a blocking type error against the inferred sequence. With it, the
records are built, and each record's `name` field holds the mapping key that survived:

```text
m.0.name=0
m.1.name=1
```

Those two lines are the assertion. The keys `0` and `1` are still keys — the transformation read
them as mapping-key text and copied them into a field — rather than having been consumed as sequence
positions.

`x` and `y` land under `value` because Section 16.5 makes "a child without a mapping projection ...
a record containing the generated key field followed by `value` holding the complete child overlay".

The records themselves sit at ordering values `0` and `1` because "a child already carrying
ordering-value provenance retains that value and provenance through record construction", and a
canonical numeric mapping key is exactly that provenance under Section 5.4.

## What the `array` half proves

`s.0=1` and `s.1=2` show both halves of the Section 16.6 rule at once: the names `b` and `c` are
gone, and the fresh ordering values are allocated "in current mapping order", which Section 5.2
makes source order here. A conversion that kept the names, or that ordered the children by name
rather than by mapping order, would write different text.

The high-water mark at `cfg.s` is zero before the conversion, so "above the node's current
high-water mark" starts at `0`. The two subtrees are independent, so `m` and `s` do not share one.

## Not asserted

That `m` and `s` render in that order for any reason other than Section 5.2 mapping order, which the
corpus asserts elsewhere.

The exit code is 0 and no diagnostic is emitted. Every directive binds to a path that exists, and
Section 16.6's `TYPE002` shape-conflict warning does not arise: neither node has a losing container
projection.
