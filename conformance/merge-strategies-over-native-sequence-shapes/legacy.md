# Merge strategies over native sequence shapes

Both halves of this fixture are derived from Section 16.10, which defines what a strategy acts on
and what each strategy does with it. Neither expectation was captured from the tool.

## What a contribution "at a path" is

Section 16.10: "A contribution is **at path `P`** when it contributes a payload, explicit container
presence, sequence projection, or any descendant under `P`."

A native sequence is therefore a contribution at its path by virtue of its sequence projection, and
an explicitly empty native sequence is one too. Nothing in that sentence makes the presence of an
item a condition. The `append` clause states the mapping route separately and narrowly -- "a source
contribution that is a **nonempty** all-canonical-numeric mapping is sequence-eligible for this
purpose" -- and that adjective is what distinguishes a numeric mapping from a native sequence. Its
presence there and absence from the sequence clause is the distinction being drawn.

## `empty`: append over an empty later sequence

`empty.list` receives `[1]` from source 1 and `[]` from source 4, under `empty.list.merge=append`.

Section 16.10 for `append`: the later sequence contribution's items are rebased onto fresh implicit
ordering values above the earlier high-water mark. The later contribution has no items, so nothing
is rebased and nothing is appended. The earlier sequence survives unchanged.

Expected: `list.0=1`. An empty sequence is the one shape `append` is named after; rejecting it as a
non-sequence use would make `[]` an error, and Section 16.10 reserves that phrase for contributions
that are not sequences at all.

## `swap` and `back`: replace across a shape change

Section 16.10 for `replace`: "the later complete value replaces the earlier value. 'Value' here
means payload, container presence, children, and sequence projection."

The enumeration is exhaustive, and every member of it comes from the later contribution. A mark
that describes any part of the earlier value therefore cannot survive, because the thing it
described is gone. The position mark is the exception, and only because Section 5.2 governs where a
key sits in mapping order rather than what it contains.

`swap.k` is a native sequence at source 1 and a mapping at source 3. The later value is the
mapping, so the sequence projection is discarded: `k.child=inner`.

`back.k` is a mapping at source 2 and a native sequence at source 4. The later value is the
sequence, so the child is discarded: `k.0=9`.

Both directions are present because a merge that carried the earlier shape forward would be
detectable in only one of them, depending on which shape the surviving marks favour. The two roots
are otherwise identical, so a defect that reports a shape conflict shows up as a diagnostic on one
root and silence on the other.

## Diagnostics

Empty. Section 8.7 emits its compatibility warning when several sources contribute native implicit
sequences at one path and no explicit `merge` directive applies; a directive applies at all three
paths here. No other clause is engaged: a shape change under `replace` is what `replace` is for, so
it is not a type conflict.

Rendering of a sequence into namespace output as canonical numeric keys follows Section 16.4 and is
already fixed by `namespace-input-merge-strategies`.