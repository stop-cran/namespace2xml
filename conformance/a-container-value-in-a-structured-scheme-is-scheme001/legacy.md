# A container value in a structured scheme is `SCHEME001`

Acceptance item 18. Section 15, Section 15.4, Section 22.

## What the inputs ask for

One scheme, written as YAML, gives three recognized directives values that are not scalars: a
sequence, an empty mapping, and `null`. No directive in the file is well formed, and one namespace
profile supplies the data the run would otherwise transform.

## What Section 15 requires

> Every recognized directive requires a nonempty scalar value after format parsing. An empty value,
> null, container value, unknown directive value, or illegal option/type combination is `SCHEME001`.

Three of the five listed conditions are exercised, and the sentence's first clause is what makes
them errors rather than conveniences: a sequence is the spelling an author reaches for when a
directive takes a comma-joined list, and Section 15 refuses it in every format rather than accepting
a second spelling in two of them. `null` is named on its own so that a format which has a null
literal cannot be read as supplying an absent value.

Section 22 gives the cardinality — `SCHEME001` is emitted "once per declaration" — and Section 15.4
gives the recovery:

> A phase completes every independent check that does not depend on a failed result, buffers its
> deterministic diagnostic set, and then aborts before the next phase when any blocking diagnostic
> exists.

All three declarations are therefore reported from one run, in source order, and nothing is
published.

## The discrimination

Three diagnostics in document order, each naming its own line and column, catch:

- an implementation that stopped at the first blocking directive, which emits one;
- one that coalesced them under the file rather than the declaration, which emits one;
- one that accepted a sequence as a joined list, or an empty mapping as an empty value, or `null` as
  no value at all — each of which drops exactly one element and changes the exit code once all three
  are gone;
- one that reported the *key* position rather than the value position, which shifts every column.

The three values sit at three different offsets on their lines, so a column that is defaulted,
copied from the key, or taken from the start of the line is wrong in all three elements rather than
accidentally right in one.

## Why the column names the value

Section 22: "`column` — the condition further names one position within that record." The condition
is about the shape of the value, so the value is the position it names, and Section 22 measures it
in Unicode scalars from column 1 at the start of the line.

## Not asserted

The remaining two conditions in the same sentence. "Unknown directive value" and "illegal
option/type combination" belong to each directive's own section rather than to the projection, and
`contradictory-output-option-flags-are-scheme001` covers the second. Nor the namespace-profile
spelling of an empty value, which has no container form to confuse it with.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 15, Section 15.4, Section 22.
- Legacy observation: the baseline exits 0, logs `Success! Exiting...`, and writes no file at all.
  It read the YAML scheme — the two sibling fixtures show that it reads structured schemes
  correctly — and then reported nothing about three directives it could not use. The run that asked
  for an `app.json` and the run that asked for nothing are indistinguishable from its exit status
  and its output tree.
- Clean behavior: each of the three declarations earns its own `SCHEME001` naming its own line and
  column, and the run exits 1 having published nothing.
- The difference is intentional and is the point of the case. Silence about a directive whose value
  cannot be used is the failure mode Section 15's value sentence exists to prevent: an author who
  writes `output: [json, yaml]` because a sequence is the natural YAML spelling of a list gets no
  output and no reason, and has nothing to search for. Exit 0 with an empty output tree is the worst
  available answer, because every downstream check that looks at the status passes.
