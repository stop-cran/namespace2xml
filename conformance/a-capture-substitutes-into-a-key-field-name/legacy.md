# A capture substitutes into a `key` field name

Acceptance item 6. Sections 12.1, 16.5, and 17.2.

## What the inputs ask for

One wildcard `key` directive, matched at two different paths:

```text
n*.key=*_id
```

`n*` matches `na` and `ni`, binding the captures `a` and `i`. Section 16.5 says "Wildcard-qualified
`key` directives are supported", and Section 12.1 decides a scheme directive's value "from the
captures its own pattern defines … its path for the path-scoped ones" — so the one rule names the
field `a_id` under `na` and `i_id` under `ni`.

## What this asserts

**The field name varies per match, not per rule.** This is the whole point of the case. A build that
resolved the value once, at scheme-compile time, would have one field name to give both subtrees;
whichever it chose, one of the two records would be wrong. Two different captures producing two
different field names is the only outcome that distinguishes per-match substitution from a single
early resolution.

**The captures come from `key`'s own match.** Unlike `root`, `delimiter`, and `filename`, a `key`
rule is not scoped to an output instance — Section 15.2 evaluates it against absolute paths inside
every instance. The output selector here is the root, which defines no captures at all, so binding
the value from an instance expansion would leave nothing to substitute and the field would be
`_id`.

**The transformation itself is Section 16.5's.** Its worked example is

```text
a.b.x=1
a.c.x=2
```

with `a.key=name`, giving the namespace projection `a.0.name=b`, `a.0.x=1`, `a.1.name=c`,
`a.1.x=2`. This case reaches the same shape by substitution instead of by writing the field name
out, so an ordered mapping becomes a sequence of records: the former child name becomes the value of
the named field, and the mapping children keep their Section 5.2 order as sequence items in that
order.

**Nothing about the surrounding tree changes.** `ni` holds a single child and still becomes a
one-item sequence rather than staying a mapping, and the two subtrees are emitted in their own
order at the top level.

## Not asserted

What happens when a substituted `key` value is empty — the capture matched nothing, and Section 16.5
rejects a directive that "names no field". The check cannot be made at compile time for a template,
so it belongs in a case of its own.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 12.1 capture substitution into a scheme directive value; Section 16.2 root-selector default file name; Section 16.5 `key`; Section 26 item 6.
- Legacy observation: the baseline exits `0` and writes `OutputRoot.properties` containing `na.b.x=1`, `na.c.x=2`, `ni.d.x=3` — the input, unchanged. The wildcard `key` directive had no effect at all, and the root-selector default file name is `OutputRoot.properties` rather than `output.properties`. Its bytes are CRLF-terminated under the Section 24 divergence.
- Clean behavior: `output.properties` containing the six lines of the two record sequences, LF-terminated.
- Why the divergence is the specified one: Section 16.5 states plainly that "wildcard-qualified `key` directives are supported", and Section 16.2's table gives the root-selector default as `output.properties` for namespace output. The baseline's flat passthrough is not a different reading of the transformation — it is the transformation not happening, which leaves an author who wrote a `key` directive with no output difference and no diagnostic to explain it.
