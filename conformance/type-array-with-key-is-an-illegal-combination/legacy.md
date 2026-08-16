# `type=array` and `key` at the same path is an illegal combination

Acceptance item 67. Section 16.5.

## What the inputs ask for

One mapping at `cfg.a`, and a scheme that asks for both transformations at that path.

Section 16.5:

> An effective `type=array` and `key` at the same path is therefore an illegal option combination
> and raises `SCHEME001`; implementations must not silently choose an order or reinterpret the
> resulting sequence as a mapping.

Both transformations are individually legal here. `cfg.a` is an ordered mapping with one child, so
`key` would build a one-record sequence and `array` would convert the same mapping to a sequence of
one item. That is the point: the refusal is not a consequence of either directive failing, it is a
rule about the pair.

The word "effective" carries the weight. The two directives are written on separate records and
neither overrides the other — Section 15.2's override stream is "for the same effective setting",
and `type` and `key` are different settings — so both survive to step 16, and it is their
coexistence at one path that is rejected.

## Why this is `SCHEME001` and not `TYPE001`

Appendix B maps "illegal option combination, `type=array` plus `key`" to `SCHEME001`, and Section 15
makes an "illegal option/type combination" a scheme error. The condition is a property of what the
scheme asked for, not of the data it met, and the fixture's data is deliberately well-formed for
both directives so that nothing else could be reported instead.

Section 22 counts `SCHEME001` "once per declaration". One diagnostic is emitted, not two.

## Why the diagnostic carries these members and not others

- `source` and `line` — the `key` declaration, at line 3. Section 22 supplies these when the
  condition is a property of a written record. Both declarations participate, and the later one is
  named because it is the one whose arrival completed the illegal pair.
- `path` — `cfg.a`, the canonical absolute path the two directives take effect at. Not `a`: the
  transformation runs against a view whose selector prefix has been stripped, but Section 22's
  `path` is a canonical path, and `a` is not a path any input wrote.
- `declaration` — `cfg.a.key`, the directive's canonical spelling without its value.
- `column` — absent. The condition is about two whole records, not a position inside one.
- `destination` — absent. No file is produced.

## Not asserted

The message prose, which Appendix C.4 exempts, though it names both declarations so that a reader
can find the other half of the pair.

The exit code is 1: `SCHEME001` is an error, and the run produces no output file. That the scheme
is rejected before any file is written is asserted by the absence of an `expected` directory.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 3.2 corrections against silent order-dependent behavior and against illegal option combinations that must not be silently reinterpreted; Section 16.5 `SCHEME001` for `type=array` plus `key`; Section 26 item 67.
- Legacy observation: the baseline exits `0` and writes a `cfg.properties` file. The measurement records `exit 0 (expected 1); extra cfg.properties`. Standard error is empty beyond the banner: no diagnostic is emitted for the pair, so the two directives were both accepted and applied in some order rather than reported as incompatible.
- Clean behavior: the pair is rejected before rendering with one `SCHEME001` and exit `1`, and nothing is written.
- Why the difference is intentional: Section 16.5 requires that "implementations must not silently choose an order or reinterpret the resulting sequence as a mapping". The baseline's silent success is exactly that -- an implementation choice made behind the user's back. The specific bytes the baseline wrote to `cfg.properties` reflect whichever of `type=array` or `key` its internal step ordering happened to apply first, and Section 3.2 lists "dependent on parallel execution order" and "dependent on shared mutable array-index state" among the corrections precisely so that no run's shape depends on such an accident.
