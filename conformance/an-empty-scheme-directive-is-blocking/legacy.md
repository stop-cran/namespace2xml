# An empty directive value is rejected

Acceptance item 67. Section 15.

## The rule this fixture is about

Section 15 states it without qualification:

> Every recognized directive requires a nonempty scalar value after format parsing.

"After format parsing" is the load-bearing phrase. It places the check after the value has been
lexed — so a value that is only a reference is *not* empty, because it is unresolved rather than
absent — and before Section 15.1 step 1 resolves anything.

The check being early is what makes the rest of the scheme phase simple: no later step has to ask
what an empty `filename` or an empty `delimiter` would mean, and a reference can never resolve to
nothing, because nothing is what a directive is not allowed to hold.

## What the inputs ask for

One selector with a valid `output` and two directives written with nothing after the `=`:

```
a.output=namespace
a.filename=
a.delimiter=
```

Both empties are `SCHEME001`, reported in source order, and the run exits 1 having written nothing.

## Reading each row

**`a.filename=` is not "the default filename".** An empty value is the most plausible way to write
"use the default", and it is rejected instead. The alternative reading has no way to distinguish it
from a truncated line or a variable that expanded to nothing in whatever produced the scheme, and
Section 16.2's own step 1 has nothing to split — as the composer's rejection message puts it, "an
empty 'filename' names no destination".

**`a.delimiter=` is rejected by the same rule, not by a `delimiter`-specific one.** An empty
delimiter has an arguable meaning — concatenate with nothing between — so this row is the one that
shows the rule is about *every recognized directive* rather than about the directives whose empty
case is nonsensical. A directive-by-directive check would let this one through.

**Both are reported.** Section 15.4 has each phase complete every independent check before it
aborts.

**`a.output=namespace` is valid and is still not honoured**, because the phase aborts. Nothing is
written.

## What this does not assert

The structured spelling of the same rule. A YAML or JSON scheme can write an empty value in three
more ways — `~`, `{}`, and an absent value — and
`a-container-value-in-a-structured-scheme-is-scheme001` covers those, from the structured reader,
which enforces the clause separately.

Nor does it assert that a value consisting only of a reference is accepted, which is the same
clause read the other way: `scheme-reference-filename-separators-are-encoded-data` writes
`b.filename=${c.filename}` and expects it to work.

## Legacy differential

- namespace2xml 2.4.0: **fails**. The baseline crashes with an unhandled
  `System.UnauthorizedAccessException` — "Access to the path
  '…\\<output root>' is denied" — after logging that it is writing an output whose name is the
  output root itself. The process exit code is `-532462766` (`0xE0434352`, an unhandled managed
  exception).
- Contract: Section 15 requires every recognized directive to carry a nonempty scalar value.
  Section 3.2 does not preserve legacy behavior "caused by unhandled user-input exceptions", which
  is what an empty value produces here.
- Legacy observation: 2.4.0 accepts the empty value, composes a destination from it, and arrives
  at a path equal to the output directory. Opening a directory as a file is what fails, so the
  message the author sees names a permissions problem at a path they did not write, and the stack
  trace is the tool's own. On a platform or configuration where that open *succeeded* the outcome
  would be worse than a crash.
- Clean behavior: the empty value is reported where it was written, with the directive named and
  the clause cited, before any destination is composed.
- The difference is intentional: an author who wrote an empty directive made a mistake that is
  cheap to name and expensive to diagnose from its consequences.
