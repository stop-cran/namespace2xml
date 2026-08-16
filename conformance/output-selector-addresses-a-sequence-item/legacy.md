# An output selector addresses a sequence item by its ordering value

Acceptance item 48. Sections 15.1 step 9, 14.1 and 5.4.

## What the inputs ask for

`inputs/data.yaml` is a native YAML sequence of two mappings:

```yaml
a:
  - x: 1
  - y: 2
```

Section 5.4 allocates the two items implicit ordering values `0` and `1`. The scheme then names
those items directly as output selectors:

```
a.0.output=namespace
a.1.output=namespace
```

## Why the expected output is what it is

Section 15.1 step 9 states the unification rule that decides this case:

> a mapping child whose name is an in-range canonical ordering value and the sequence item with that
> value at the same path are one structural overlay node for merging, comments, references,
> **selectors**, generation, and wildcard candidacy, whether or not step-11 inference ultimately
> applies.

`selectors` is in that list. A sequence item is therefore addressable by an output selector spelled
with its ordering value, exactly as a numeric mapping child would be, and it is addressable whether
or not anything later infers the mapping back into a sequence.

Section 14.1 then fixes what each instance contains:

> An output instance selects its complete literal-prefix subtree.

The subtree at `a.0` is the mapping `{x: 1}`, and Section 14.1 removes the concrete selector prefix
unconditionally, so the instance renders `x=1` rather than `a.0.x=1`. The same reasoning gives
`a.1.properties` the single record `y=2`.

Two files are expected because two selectors were written, and nothing here is a wildcard: this
fixture is about addressing, not expansion.

## What this fixture is guarding

An implementation that walks only the mapping facet when it descends an output selector finds
nothing at `a.0`, because a native sequence item lives in the sequence facet. Section 14.1 also
says a namespace output with no records emits its normalized empty text file, so the failure has a
legitimate-looking shape: the run exits 0, reports nothing, and writes two empty files while the
data sits addressable in the model. `a.properties` would still render `0.x=1` and `1.y=2`, which is
what makes the loss silent.

Asserting the file *contents* rather than their presence is the point. A fixture that only checked
that `a.0.properties` exists would pass against that implementation.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 3.2 correction against a synthetic internal root leaking into user-visible file names; Section 14.1 default filename composition; Section 15.1 step 9 sequence/mapping unification for selectors.
- Legacy observation: the baseline writes `0.properties` and `1.properties` where this case expects `a.0.properties` and `a.1.properties`. The measurement records `missing a.0.properties; missing a.1.properties; extra 0.properties; extra 1.properties`. Exit code is `0` and standard error is empty beyond the banner, so two files land with the two sequence items' contents in them -- the leading `a.` segment is missing from each name.
- Clean behavior: the default filename is the whole concrete selector, so the two instances render at `a.0.properties` and `a.1.properties`.
- Why the difference is intentional: the missing `a.` prefix is the Section 3.2 synthetic-root leak, and the fact that both instances land at all says nothing about whether 2.4.0 addressed the sequence facet at step 9 or reached these items by another path -- the surviving *content* would be identical either way for this data, so the tree comparison here settles filenames rather than addressing. The Section 15.1 step 9 discriminator (a sequence item is addressable through its ordering value) is invisible in the observable this verdict is scored against; it is asserted by the fixture's `expected/` bytes, not by whether the baseline reproduces them for the right reason.
