# A wildcard cascade completes within the iteration bound

The companion of `wildcard-cascade-crosses-the-iteration-bound`, and the half of Section 12.4's
requirement that a bound be a *bound* rather than a refusal. The rule set, the inputs and the scheme
are identical; only `--max-wildcard-iterations` differs, 3 here against 2 there.

## What it fixes

Section 12.4 says the fixed point "continues until no new match pair or generated contribution
exists", and that "breadth-wave iteration counts apply only to generative templates". Two readings
of the second sentence differ by exactly one:

- charge every wave, including the one that settles the fixed point — under which a three-level
  cascade needs 4;
- charge only waves that generate — under which it needs 3.

The specification's "apply only to generative templates" selects the second. Under the first reading
`--max-wildcard-iterations 1` could never be satisfied by any rule set that matched anything, which
is not a bound anyone could configure meaningfully. This fixture is set at exactly 3 so that the
distinction is what it measures.

## Expected output

Section 12.4 makes generated entries ordinary contributions merged "at [their] deterministic
rule/match position", so each rung of the ladder carries the ordering key of the rule that produced
it. The scheme roots the output at `a`, and Section 14's namespace serializer emits each leaf under
its path relative to that root:

```text
x=1
x.b=2
x.b.c=3
x.b.c.d=4
```

`x` comes from the concrete line 1; `x.b`, `x.b.c` and `x.b.c.d` from the rules on lines 2, 3 and 4,
in that order. The chain is a single descent, so tree order and source order agree and the file is
unambiguous under either.

## Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Section 12.4 wildcard fixed-point evaluation and iteration accounting; Section 23 wildcard iteration budget; Section 26 items 7 and 36. Section 3 does not enumerate `--max-wildcard-iterations`; the fixture pins the bound side of Section 12.4 rather than a Section 3.1 preservation or a Section 3.2 correction.
- Legacy observation: the baseline exits `1` with no output tree and prints `ERROR(S):` on standard error. The measurement records `exit 1 (expected 0); missing a.properties`.
- Clean behavior: `--max-wildcard-iterations 3` admits the three generating waves, the fixed point settles, and the run writes `a.properties` with the four expected lines at exit `0`.
- Why the difference is intentional: 2.4.0 has no `--max-wildcard-iterations` option, so its CommandLineParser rejects the unknown flag before any input is read and prints `ERROR(S):` -- the standard CommandLineParser preamble for an argument diagnostic. The correction is not merely that the option must be accepted but that the bound be a *bound*, so the companion case `wildcard-cascade-crosses-the-iteration-bound` reproduces the failure side of Section 12.4 and this case pins that a run below the bound still completes. Neither half of that guarantee is expressible in 2.4.0, whose iteration limit was hard-coded rather than configurable.
