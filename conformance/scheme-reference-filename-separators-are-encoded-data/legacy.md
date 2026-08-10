# A separator a scheme reference supplied is encoded data

Acceptance item 67. Sections 15.1, 15.2 and 16.2.

## The rule this fixture is about

Section 15.1 step 1 resolves references among scheme entries, and Section 16.2 says what the result
of that resolution *is*:

> Scheme references are resolved before capture substitution, but their resulting text is opaque
> segment data: `/` or `\` supplied by a reference is encoded and never creates a directory.

The clause exists because the two texts are indistinguishable once spliced. `dir/leaf.conf` written
in the scheme and `dir/leaf.conf` arriving through `${c.filename}` are the same eleven characters,
and Section 16.2 step 1 splits "the scheme-written path" — which the second one is not. An
implementation that resolves a reference into an ordinary literal has thrown away the only thing
that distinguishes them, and there is no later step that can recover it.

This is the same distinction Section 16.2 already draws for captures, and
`filename-captures-are-opaque-segment-data` is the capture half of it. The consequence is stronger
here, though: a capture comes from input data, whereas a reference names another *scheme* entry, so
an author reasonably expects it to behave like scheme text. It does not.

## What the inputs ask for

Five selectors, each with `output=namespace`, and one profile entry apiece so that each destination
has visible content.

| Selector | `filename` | Result | Why |
|---|---|---|---|
| `c` | `dir/leaf.conf` | `dir/leaf.conf` | the separator is written, so step 1 splits |
| `b` | `${c.filename}` | `dir%2Fleaf.conf` | the separator arrived through a reference |
| `a` | `pre-${b.filename}` | `pre-dir%2Fleaf.conf` | opacity survives a second hop |
| `d` | `first`, then `second` | `second` | Section 15.2 source order |
| `e` | `${d.filename}-copy.conf` | `second-copy.conf` | a reference reads the winner |

## Reading each row

**`c` → `dir/leaf.conf`.** The control, and the row that makes the fixture mean anything. Without
it, an implementation that encoded *every* separator in a `filename` would pass. Step 1 splits the
scheme-written path at this `/`, so a directory named `dir` is created and the file goes in it.

**`b` → `dir%2Fleaf.conf`, not `dir/leaf.conf`.** The characters are identical to `c`'s, and the
outcome is not. Step 1 has nothing to split — the written path is one reference and nothing else —
so the whole resolved text is one segment, and step 5 encodes `/` as `%2F` because it retains only
"ASCII letters, digits, `-`, `_`, and `.`". The file lands in the output root.

**`a` → `pre-dir%2Fleaf.conf`.** `b`'s own value was produced by resolving a reference, so this row
asks whether the opacity is a property of the text or of the *edge*. It is the text: `b.filename`
holds referenced data, and passing it through another reference does not launder it into written
path. An implementation that marks only the first hop opaque writes `pre-dir/leaf.conf` here and
still passes the `b` row.

**`d` → `second`.** `d.filename` is declared twice. Section 15.2 resolves that by source order, so
the later declaration wins. The row is here for `e`.

**`e` → `second-copy.conf`.** A reference names a *setting*, not the nearest declaration above it,
so it reads the same winner Section 15.2 computed rather than `first`. An implementation that
resolves references in one pass while reading the scheme — the obvious shape, since resolution has
to walk the entries anyway — produces `first-copy.conf`, because at the moment `e` is read the
second `d.filename` has not been seen yet. Note that this makes forward references work too, which
`a` and `b` both rely on: they reference entries declared *below* them.

No separator is involved in this row, which is deliberate. It isolates the Section 15.2 claim from
the Section 16.2 one.

## What this does not assert

That a reference-supplied `..` is renamed rather than rejected. It never becomes a segment of its
own — every separator around it is encoded first — so the question does not arise for the composer;
the unit tests pin the token-level behaviour. Nor does the fixture cover a reference to a directive
other than `filename`: Section 15's vocabulary is fixed, and `filename` is the only directive whose
value is free text with an observable effect on the output tree.

The error paths are `a-missing-scheme-reference-is-blocking` and
`a-scheme-reference-cycle-is-blocking`.

## Legacy differential

- namespace2xml 2.4.0: **differs**. The baseline exits 0 and writes four files where the case
  expects five. `second` and `second-copy.conf` match. `dir/leaf.conf` is written twice — the
  baseline logs "Writing output …dir/leaf.conf" for `c` and then "Appending output …dir/leaf.conf"
  for `b` — so it holds `name=beta` followed by `name=gamma` instead of `name=gamma` alone, and the
  case's `dir%2Fleaf.conf` is missing. `pre-dir/leaf.conf` is a file under a `pre-dir` directory
  where the case expects a file named `pre-dir%2Fleaf.conf` in the output root.
- Contract: Section 16.2 requires the text a reference contributes to be opaque segment data, so
  only separators written in the scheme create directory hierarchy. Section 3.2's "wrong output
  destination" family covers the resulting defect.
- Legacy observation: 2.4.0 does resolve scheme references, and resolves them to the Section 15.2
  winner and across forward declarations — the `d`/`e` rows agree exactly. What it does not do is
  keep the resolved text distinguishable from written text: it splices the referent's characters
  into the template and then splits, so a `/` that arrived through `${c.filename}` becomes a
  directory separator. That has two visible consequences here. `b` and `c` compose the *same*
  destination, which the baseline resolves by appending `c`'s content to the file `b` already
  wrote, silently merging two unrelated selectors into one file. And `pre-${b.filename}` creates a
  `pre-dir` directory that no scheme text asked for.
- Clean behavior: step 1 records what each reference contributed, step 1 of Section 16.2 splits
  only the written path, and step 5 encodes the referenced separators. The three destinations stay
  distinct, so the Section 16.2 collision question never arises.
- The difference is intentional: a referenced directive's value is data as far as path
  construction is concerned, and the baseline's behaviour makes the destination of one selector
  depend on characters held by another. The append is the more serious half — it is silent, and it
  produces a file whose content belongs to two selectors at once.
