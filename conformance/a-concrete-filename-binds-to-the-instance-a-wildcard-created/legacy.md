# A concrete `filename` binds to the instance a wildcard created

Acceptance items 36 and 50. Section 15.2.

## What the inputs ask for

One wildcard `output` declaration and one exact `filename` on a selector the wildcard matches:

1. `a.*.output=namespace`
2. `a.x.filename=custom.conf`

`a.*` literalizes to two concrete selectors, `a.x` and `a.y`. Section 15.2 states the binding rule
for the whole instance-scoped group:

> `output`, `filename`, `root`, `delimiter`, output options, and `filemerge` bind to the concrete
> output selector instance produced from their own selector;
> exact and wildcard declarations that literalize to the same concrete selector participate in one
> source-ordered override stream;

`a.x.filename` and the `a.x` instance that `a.*.output` produced are therefore the same stream. The
`filename` is not a separate declaration that needs its own `output`, and it is not unbound.

The third bullet is the boundary this case must not cross:

> a directive for selector `a` does not implicitly configure an independently created `a.x` output
> instance;

That clause is about a directive whose selector is genuinely different from the instance's. Here the
directive's selector and the literalized instance selector are the same concrete name, `a.x`, which
is exactly the case the second bullet admits. Reading the third bullet as a prefix rule would make
the second bullet unsatisfiable for every wildcard.

## What the expected tree asserts

Two files: `custom.conf` holding `k=1`, and `a.y.properties` holding `k=2`.

`custom.conf` is the whole point. Section 16.2 makes an explicit `filename` the complete relative
destination with no extension appended, so the instance the wildcard created for `a.x` writes to
`custom.conf` and not to `a.x.properties`. `a.y.properties` is the untouched sibling, present so the
case also proves the `filename` did **not** leak onto the other instance the same wildcard produced.

Both payloads are rendered relative to the selector, so `a.x.k=1` appears as `k=1`.

An implementation that keys options by the declaration's own written selector never consults a
winner recorded under `a.x` while building the instance for `a.*`. It then writes `a.x.properties`,
never writes `custom.conf`, and reports the `filename` as binding to nothing.

## Not asserted

The diagnostic stream is `[]`, which pins the absence of `WARN009` for `a.x.filename`. Nothing here
asserts what would happen if `a.x` had its own `output` declaration as well; that is the ordinary
override stream and it is covered elsewhere.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 3.2 correction against a synthetic internal root leaking into user-visible file
  names. The Section 15.2 binding rule under test is not a Section 3 divergence.
- Legacy observation: the baseline writes `custom.conf` containing `k=1` and `y.properties`
  containing `k=2`, exits `0`, and writes nothing to standard error beyond the banner. The
  measurement records this as `missing a.y.properties; extra y.properties`.
- Clean behavior: identical except that the defaulted filename is composed from the whole concrete
  selector under Section 16.2, so `a.y` writes to `a.y.properties`.
- What the agreement means here, unusually: the baseline binds the exact `filename` to the instance
  the wildcard created, which is precisely the Section 15.2 rule this case exists to pin. The
  divergence is confined to the defaulted name of the *other* instance. This case is therefore not
  a correction of legacy behavior. It is a regression test against a defect introduced by the 3.0
  rewrite, which keyed instance options by each declaration's written selector and so reported
  `a.x.filename` as unbound. The baseline was right about this clause and the preview was not.
