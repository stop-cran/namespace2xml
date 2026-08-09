# `substitute=None` disables interpretation at the node it matches

Acceptance item 18. Section 13.4, Section 15.1 step 6, Section 15.2, Section 16.7, Section 19.1.

## What the inputs ask for

Three entries carry the same reference text and one wildcard-shaped name, and two `substitute`
directives name two of them:

- `cfg.raw` is named by `cfg.raw.substitute=None`;
- `cfg.deep.raw` is named by nothing;
- `cfg.*.gen` is named by `cfg.*.gen.substitute=None`.

`lit=X` sits outside the selected subtree and supplies the referent.

## What Section 16.7 requires

| Mode | Names interpreted | Values interpreted |
|---|---:|---:|
| `None` | no | no |

Section 13.4 restates it: "`substitute=None` disables interpretation in both names and values."

## The three discriminations

**A matched value is text.** `cfg.raw` emits the six characters that spell `${lit}` rather than the
referent `X`. Section 19.1 encodes a literal reference start as `\${`, so the expected line reads
`raw=\${lit}`. A run that resolved the reference would emit `raw=X`; a run that disabled
interpretation but did not re-encode the output would emit `raw=${lit}`, which reads back as a
reference and breaks the Section 3.3 round trip.

**An ancestor's directive does not reach a descendant.** `cfg.deep.raw` is three components and the
pattern `cfg.raw` is two, so nothing matches it and `${lit}` resolves to `X`. Section 16.7 states no
scoping rule of its own; Sections 8.4, 10.1 and 10.2 each speak of a *matching* `substitute`
directive, and Section 16.10 makes the sibling input-phase directive node-scoped in as many words —
"a `merge` directive governs only the node it matches". Were the directive subtree-scoped,
`cfg.deep.raw` would emit `\${lit}` too, and the second line of the expected file is what says it
does not.

**A matched name is text.** `cfg.*.gen` under `None` is not a Section 12 template. Its middle
component is the one-character name `*`, and Section 19.1 escapes a literal `*` in a name part, so
the expected line reads `\*.gen=g`. Under `All` the same entry would be a wildcard template matching
nothing and would contribute no line at all, which is what makes this line an assertion rather than
a coincidence.

## Why the pattern `cfg.*.gen` matches the entry `cfg.*.gen`

Section 15.1 step 6 resolves the mode against "an entry's declared pre-expansion path", which for
this entry is a path containing a wildcard. A pattern is matched against that path by its spelling:
the wildcard component contributes the character that wrote it. Any other reading would make the
name column of Section 16.7's table unreachable, because the only entries whose names have anything
to interpret are exactly the ones a structural match would refuse.

## Not asserted

The `Key` and `Value` rows of Section 16.7's table, which the sibling `substitute-key-…` fixture
covers for native strings. Nor the pathless form of the directive, which is unit-tested.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 16.7, 13.4, and 19.1; Section 3.3's normalized same-format round trip.
- Legacy observation: the baseline exits 0 and writes `cfg.properties` containing `raw=${lit}`,
  `deep.raw=X`, and `*.gen=g`. Every semantic decision agrees with the case: the matched value was
  left uninterpreted, the unmatched descendant resolved its reference, and the wildcard-shaped name
  became a literal node. What differs is the encoding of the two lines that carry a metacharacter.
  2.4.0 emitted `${lit}` and `*.gen` unescaped, so its own output no longer reads back as the data
  it was written from: reading `cfg.properties` again would find a reference at `raw` and a wildcard
  template at `*.gen`.
- Clean behavior: Section 19.1 makes namespace name encoding total and injective and gives values
  the inverse of the value lexer, so the same two lines emit `\${lit}` and `\*.gen`.
- The difference is intentional, and it is the Section 3.3 round-trip guarantee rather than anything
  in Section 16.7. A tool whose disabled-substitution output re-enables substitution when read back
  cannot be used to stage a value through an intermediate file, which is the main reason to disable
  substitution in the first place.
- That the scoping and name-literalization decisions this case turns on match 2.4.0 is worth
  recording: Section 16.7 states neither, and the baseline is the only other artifact that had to
  answer both.
