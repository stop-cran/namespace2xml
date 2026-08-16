# `key` projects an ordered mapping as a sequence of records

Acceptance item 22. Section 16.5.

## What the inputs ask for

Section 16.5 opens with a worked example, and this fixture is that example. The input, the scheme,
and both printed projections are copied from the specification rather than from the tool:

> Input:
>
> ```text
> a.b.x=1
> a.c.x=2
> ```
>
> Scheme:
>
> ```text
> a.key=name
> ```
>
> Logical transformed value:
>
> ```text
> [ { name: "b", x: 1 }, { name: "c", x: 2 } ]
> ```
>
> Namespace projection:
>
> ```text
> a.0.name=b
> a.0.x=1
> a.1.name=c
> a.1.x=2
> ```
>
> INI projection:
>
> ```ini
> [a:0]
> name=b
> x=1
>
> [a:1]
> name=c
> x=2
> ```

The mapping under `a` becomes a sequence of two records. Each mapping key becomes a field: "the
generated key field is inserted first as a string scalar containing the decoded mapping-key text",
and the child's own mapping fields follow it.

## Why the data is one level deeper than the specification's example

The specification's projections print the paths as they stand in the model, including the `a` that
`key` was applied to. A fixture has to name an output selector, and the selected subtree is what a
file contains, so a scheme of `a.output=…` with `a.key=…` would strip the very `a` the printed
projections show — the namespace file would read `0.name=b` and the INI file would have sections
`[0]` and `[1]`.

Everything is therefore written under `cfg`, and `cfg` is the selector. The transformation is
applied at `cfg.a`, exactly as the example applies it at `a`, and both files then reproduce the
specification's printed text verbatim.

## Why both formats

Section 16.5's first sentence is the claim under test:

> `key` is an output-neutral transformation from an ordered mapping to a sequence of records.

Output-neutral is not observable in one format. The transformation happens at pipeline step 16, once
per output instance, before any serializer runs, so the two formats must show the same records
differing only in how Section 19 spells a sequence — flat numeric path components in namespace,
`:`-joined section names in INI. A single format could equally be explained by a serializer that
happened to invent records itself.

The two formats are written as one comma-separated `output` declaration because Section 16.1 gives
formats in one list "a left-to-right declaration ordinal", which keeps this a single output instance
with two files rather than two independently configured instances.

## What each field of a record proves

- `name` comes first in both files. Section 16.5 says the generated field "is inserted first", and
  Section 5.2 mapping order — not insertion order — is what a serializer walks, so first means it
  must sort ahead of `x` rather than merely having been added before it.
- `name=b` carries the *decoded* mapping-key text, and `x=1` is the child's own field, unmoved.
- The records are at ordering values `0` and `1`. Neither child carried ordering-value provenance,
  so each "receives a fresh implicit ordering value in mapping order", and Section 5.4 allocates
  from a high-water mark of zero.

## Not asserted

Scalar typing of `x`. The namespace and INI formats spell an inferred integer and a string
identically, so this fixture cannot distinguish them and does not claim to. Section 16.5's rule that
"scalar inference is never applied to this generated field" is likewise unobservable here; it needs
a format with distinct scalar kinds.

Comment movement, which acceptance item 79 covers separately.

The exit code is 0 and no diagnostic is emitted: `key` bound to a path that exists, and nothing in
this fixture is a warning condition.

## Legacy differential

- namespace2xml 2.4.0: **differs**. The baseline writes `cfg.ini` and `cfg.properties` with
  different content than the case expects (the harness records `content cfg.ini` and
  `content cfg.properties`); the exit code matches.
- Contract: Section 16.5 is the substantive section that fixes the `key` projection — the
  generated field is a string scalar inserted first, mapping keys are decoded, and each record
  is placed at a fresh implicit ordering value in mapping order. Section 3 does not enumerate
  the projection rule; 3.1 preserves the *name* of the `key` scheme directive but not its
  detailed shape.
- Legacy observation: 2.4.0 produces a different projection at this path in both formats. The
  measurement records only that the bytes differ; the exact reduction 2.4.0 produces — whether
  the generated field appears in a different position within each record, or whether the record
  sequence uses a different indexing shape — is implementation-defined for the baseline. The
  fact that both formats diverge from the specified example bytes is the evidence.
- Clean behavior: the `a` mapping becomes a sequence of two records. Each record carries a
  generated `name` field first, holding the decoded mapping-key text, followed by the child's
  own fields, and the two records sit at ordering values `0` and `1`. The namespace and INI
  serializers project the same records identically up to how each format spells a sequence.
- The difference is intentional: Section 16.5's projection is output-neutral by construction —
  the transformation happens at pipeline step 16, before any serializer runs — so both file
  formats must reproduce the specification's printed example bytes together.
