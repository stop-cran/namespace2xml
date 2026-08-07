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
