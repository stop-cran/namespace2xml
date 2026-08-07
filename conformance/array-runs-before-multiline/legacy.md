# `array` runs before `multiline`

Acceptance item 54. Section 15.1 step 16, Section 16.6.

## What the inputs ask for

`cfg.lines` is a mapping with two named children, `p` and `q`. `cfg.lines.type=array,multiline`
selects two transformations at that one path.

## Why the order is observable here and nowhere else

Step 16 fixes the order:

> Apply path-scoped transformations to each selected output view in this order: `type=ignore`,
> explicit scalar/XML types, `type=array` or `type=mapping`, `multiline`, `key`, then `root`.

For most pairs the order is unobservable, because the two transformations touch different things. This
pair is the exception, and it is sharp in both directions.

**In the specified order** `array` runs first. Section 16.6:

> if the winning container is a mapping, convert that mapping

so `{p: first, q: second}` becomes the sequence `[first, second]` — `array` "discards names", which
is what `type-mapping-keeps-numeric-keys-and-array-discards-names` pins separately. `multiline` then
meets a sequence of scalar lines and combines them "using logical LF".

**In the reverse order** `multiline` runs first and meets a mapping. Section 16.6 lists that case
among its errors:

> a mapping with no sequence projection ... is `TYPE001`

So an implementation that applied these two in declaration order, in alphabetical order, or in one
visit per node that applied everything it found, would not produce a differently ordered result — it
would fail the run. Exit code 0 with this tree is therefore a direct assertion about the order.

## Why `array,multiline` is spelled that way

Section 16.6 lists `array,multiline` among the legal combinations and `multiline,array` nowhere. The
written order is not what step 16 obeys — a `type` set is unordered, and step 16 names the order
itself — but writing the legal spelling keeps the fixture about step 16 rather than about
Section 16.6's combination validation.

## The expected value

`first\nsecond`, one physical record. Section 19.1:

> Physical output entries are always one line. Multiline scalar data is represented through escapes,
> never literal record-breaking line terminators.

and its escape list gives "LF as `\n`". The two-character escape in the expected file is therefore
the assertion that a logical line break survived into the value; a file containing two records, or
one record holding a literal newline, would both be wrong for different reasons.

The sequence order is `first` then `second` because `array` converts the mapping in mapping order,
and mapping order here is source order.

## Not asserted

What `multiline` does to a sequence containing a null, an empty sequence, or a nested container —
those are Section 16.6's own cases rather than step 16's ordering. Nor the position of `key` or
`root` in the order: `key` after `multiline` is not separately observable without a second fixture,
and `root` is not a pass at all.
