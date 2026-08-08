# Wildcard generation crosses the generated-entry bound

Section 12.4 requires "configurable generated-entry **and** iteration limits". They are separate
bounds over separate quantities, and a rule set can cross either without approaching the other: this
one settles in a single wave — well inside the default 1,024 iterations — and is stopped by the
number of nodes it materializes.

## The rule set

```text
a.x=1
a.y=2
a.*.tag=marked
```

`a.*.tag` has its last wildcard-containing part at depth 2, so by Section 12.4 the eligible items are
the distinct depth-2 prefixes `a.x` and `a.y`. Both match, in one wave, producing `a.x.tag` and
`a.y.tag`.

## What is counted

Section 23: "`--max-generated` counts newly materialized overlay nodes, including carrier containers
required for a generated descendant. A generated contribution targeting only already-existing nodes
consumes no generated-node count."

`a` and `a.x` already exist, so `a.x.tag` materializes one node; likewise `a.y.tag`. The rule set
therefore costs 2, and `--max-generated 1` admits the first and refuses the second.

The bound is set to 1 rather than 2 so that the fixture measures the count. At 2 the run succeeds,
and a fixture pinned there would pass against an implementation that counted nothing.

## Which rule is responsible

Only one rule generates, so "report the rules responsible for the limit" names `a.*.tag` alone, and
Section 22's `rule` member carries it as a one-element array of its Appendix A canonical name. The
two matches belong to the same rule, so the report does not depend on which of `a.x` and `a.y` the
wave reached first — which matters, because a report that named the match rather than the rule would
make the diagnostic depend on candidate enumeration order, and Section 12.4 forbids depending on it
("never depend on hash-map iteration order").

Appendix B maps the crossed bound to `WILDCARD002` and exit code 1. No `expected/` directory: the run
stages nothing, because output planning follows the fixed point in Section 15.1.

## Legacy differential

- namespace2xml 2.4.0: **agrees**.
- Contract: Section 3.2 is not the anchor. Section 12.4 fixes the generated-entry bound as a
  requirement of the fixed-point evaluation, and Section 23 fixes what `--max-generated` counts.
- Legacy observation: the baseline exits `1` and stages no output tree, so the observed result
  matches this case's expected result (exit code `1`, empty tree). The measurement records no
  divergence and no standard error beyond the banner.
- Why the observable agreement is not compatibility evidence: this case has no expected outputs
  precisely because the clean tool refuses the run under `WILDCARD002`, but 2.4.0 has no
  `--max-generated` option. Its exit `1` therefore reports an unknown-option failure, not a
  crossed generated-entry bound, and the two runs land on the same observable for entirely
  unrelated reasons. This case cannot discriminate `WILDCARD002` from an early CLI parse error; a
  fixture that could would need the baseline to accept the invocation, which it cannot without
  the option.

