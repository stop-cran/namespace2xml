# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 24, 21.3, 17.5 and 19.6.
- Legacy observation: diagnostic order was an artifact of evaluation order, so a run that
  diagnosed several output files reported them in whatever order the work happened to complete.
- Clean behavior: a diagnostic whose Section 22 cardinality counts per output instance carries a
  destination and no source ordering key. Section 24 therefore orders it in group 2, by the
  Section 21.3 destination order, which is the minimum Section 17.5 fold key -- output-declaration
  source order first, canonical path only as a later tie-break.
- Why the case is shaped this way: Section 14.1 lets nested output declarations select overlapping
  data, so `app.x.a` fails in both `app.ini` (as `x.a`) and `app.x.ini` (as `a`). Keying those two
  occurrences at the item they share would make them tie on phase, ordering key and code, leaving
  only a path expressed in each output's own frame to separate them -- which would report `a`
  before `x.a` and so invert the order the files are written in. The declaration order here is
  `zzz`, `app`, `app.x`, while the canonical paths sort `app.ini`, `app.x.ini`, `zzz.ini`; the two
  disagree, so the expected stream distinguishes the fold key from the path.
