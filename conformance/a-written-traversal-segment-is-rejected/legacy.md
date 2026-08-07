# A `..` written in the scheme is rejected

Acceptance item 51. Sections 16.2 and 21.1.

## What the input asks for

`a.filename=../escape.conf`. Nothing is captured; the `..` is text the scheme author typed.

## Why this is an error rather than an encoding

Section 16.2:

> Statically written `.` and `..` segments are prohibited.

The verb is *prohibited*, and the same paragraph contrasts it with the treatment of everything else:
"Reserved device names are deterministically renamed with the prefix rather than rejected."
Renaming a written `..` to `%5F%2E%2E` would be the wrong repair — it would silently redirect a
destination the author asked for to a different one, and the author would have no way to tell from
the output that their path was not honoured.

Section 16.2 also states the containment rule directly:

> The path must be relative to the configured output root. Absolute paths and paths resolving
> outside the output root are errors.

Appendix B maps an "uncontainable destination path" to `PATH001`.

## What is asserted

A `PATH001` error in phase `planning` anchored at `§16.2`, and exit code 1.

The phase matters. The rejection is a property of the composed path and needs no filesystem, so it
is detected while the plan is built and before anything is opened. Section 15.1's validation gate
requires that a failing run leave the output root untouched, and this fixture's expected tree is
empty — an implementation that discovered the problem at write time would already have created
`escape.conf` somewhere, and could not un-create it.

## What is not asserted

The message text. The diagnostic comparer compares only the fields the fixture declares, and
wording is not part of the contract.

Whether `a/../b.conf` — a traversal that resolves back inside the root — is rejected. It is a
written `..` segment and therefore prohibited by the same sentence, but "prohibited" and "resolves
outside the root" are two different justifications and this fixture pins only the case where both
agree.
