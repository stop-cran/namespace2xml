# Two destinations differing only by ASCII case collide

Acceptance item 64. Section 17.5.

## What the inputs ask for

Two independent selectors, `a` and `b`, each with its own `filename`: `out.conf` and `Out.conf`.

On Linux these are two files and the run is unremarkable. On Windows and on a default macOS volume
they are one file, and the second write silently destroys the first — or, worse, does not, depending
on which order the plan happened to enumerate.

## The rule

Section 17.5:

> Two nonidentical canonical paths with the same portability key are a blocking `PATH001` collision
> rather than a merge.

*Rather than a merge* is the operative phrase. Section 17.5 does define a fold for genuine same
destination collisions; this is deliberately not that. Folding here would mean picking a winner
between two files the author believes are separate, and the choice would be invisible.

*Blocking* is the second operative word: the run fails rather than warning, because a warning would
leave a half written output tree whose contents depend on the host filesystem's case behaviour —
precisely the platform dependence the whole of Section 17 exists to remove.

## Why this must fail on Linux too

Section 16.2 requires the portable segment algorithm to be "applied identically on every operating
system so identical inputs produce identical relative paths", and Section 3's determinism guarantee
is cross platform. A collision detected only where the filesystem happens to notice it would make
the tool's exit code a property of the host. The portability key is computed and compared in the
tool, so this fixture fails identically everywhere — which is why it can be a portable fixture at
all.

## What is asserted

A `PATH001` error in phase `planning` anchored at `§17.5`, exit code 1, and an empty output tree.

## What is not asserted

Which of the two paths the message names. The comparer checks the declared fields only, and the
diagnostic's `destination` is deliberately left undeclared: the pair is symmetric and naming one
member of it is an implementation choice, not a contract.

Collisions between more than two paths, and collisions arising from the *default* filename rather
than an explicit one.

## Legacy differential

- namespace2xml 2.4.0: **differs**. On Linux the baseline sees the two `filename` values as
  distinct names, writes one of them (the harness records `extra out.conf`), and exits 0. Neither
  the observed exit (0, expected 1) nor the observed tree (one file, expected none) matches.
- Contract: Section 3.2, "allowed output paths that differ only by ASCII letter case to coexist on
  some operating systems but collide on others", is the enumerated correction; Section 16.2 and
  Section 17.5 carry the mechanism.
- Legacy observation on Linux: `out.conf` and `Out.conf` coexist on the filesystem, so both files
  are opened and the run reports success — the exit code and tree therefore depend on which host
  the run is compared against. On Windows or a default macOS volume the two names refer to one
  file and the later write silently overwrites the earlier, so the same command produces different
  content depending on which selector 2.4.0's plan enumerated first. Neither outcome is a
  diagnostic.
- Clean behavior: the portability key is computed and compared in the tool at planning, and
  Section 17.5 makes the collision a blocking `PATH001` before any file is opened, so the exit
  code and tree are the same on every host.
- The difference is intentional: the 3.2 clause exists exactly because a run whose result is a
  property of the host filesystem cannot participate in the byte-identical determinism Section 3
  promises across platforms.
