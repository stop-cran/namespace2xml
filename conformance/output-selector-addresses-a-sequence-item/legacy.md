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