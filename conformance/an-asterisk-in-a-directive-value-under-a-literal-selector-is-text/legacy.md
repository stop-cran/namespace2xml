# An asterisk in a directive value under a literal selector is text

Acceptance item 6. Sections 12.1, 16.3, 16.4, and 21.

## What the inputs ask for

The selector `a` defines no capture, and two of its directive values contain an asterisk anyway:

```text
a.delimiter=*
a.root=r*
```

Section 12.1:

> A scheme directive's value is decided the same way, from the captures its own pattern defines:
> its selector for the output-instance-scoped directives, its path for the path-scoped ones, as
> Section 15.2 separates them. The `substitute` directive does not apply to scheme declarations, so
> that pattern alone decides. […] Where it defines none, `*` in the value is literal text: in a
> scheme whose selector contains no wildcard, `*` in a `filename`, `root`, or `delimiter` value
> needs no escape.

So neither asterisk is a capture, and neither is an error. This is the negative half of item 6, and
it is the half an implementation is most likely to get wrong by rejecting rather than by
substituting.

## What this asserts

**The decision is made before the value is lexed.** Section 12.1 says "whether a value contains
wildcard tokens at all is therefore decided before the value is lexed, from the owning name's
captures". A build that lexed first and then asked what to do with the token it found would have to
choose between substituting nothing and refusing, and both discard what the author wrote.

**A literal asterisk is not a pattern anywhere downstream either.** Section 16.3 gives the root
value the *name* grammar, in which a bare `*` is a wildcard token. Carrying that token into a
concrete root would leave a pattern in a path that no later stage expects to match against, so the
decision Section 12.1 already made has to be carried into the lexed name rather than left for a
later matcher to reinterpret.

**Section 21 then escapes the literal asterisk on the way out.** "In every name part, escape: …
literal `*`". The root part `r*` would therefore emit `r\*` — except that the delimiter here is
itself `*`, and Section 16.4 takes precedence:

> A scalar that begins a configured-delimiter occurrence is always escaped as `\u{HEX}` under
> Section 16.4, even when Section 8.2 defines another lexer form for that scalar.

The part `r*` contains an occurrence of the delimiter `*` at its second scalar, so that scalar emits
`\u{2A}` and the part becomes `r\u{2A}`. The parts `r\u{2A}` and `x` then join with the literal
delimiter, giving `r\u{2A}*x=1`. A reader splitting that line on `*` recovers exactly two parts,
which is the injectivity Section 16.4's escape exists to preserve.

**`filename=all.conf` is written whole.** It contains only characters Section 16.2 step 5 retains,
so the portable segment algorithm leaves it alone and no `.properties` is appended.

## Not asserted

Whether a literal asterisk in a `filename` value is percent-encoded by Section 16.2 step 5. The
step's list retains "ASCII letters, digits, `-`, `_`, and `.`" and would encode `*` as `%2A`, but
the algorithm's opening sentence scopes it to substituted captures and selector-derived parts while
a later sentence extends the safety rules to "wholly literal segments". That tension is real and
this case avoids it by using a filename with no asterisk in it.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 12.1 literal-text decision under a capture-free selector; Section 16.3 `root`; Section 16.4 delimiter escaping; Section 21 namespace name encoding; Section 26 item 6.
- Legacy observation: the baseline exits `0` and writes `all.conf`, so it agrees on the explicit filename. The file contains `x=1` — neither the `root` nor the `delimiter` directive had any effect. Its bytes are CRLF-terminated under the Section 24 divergence.
- Clean behavior: `all.conf` containing `r\u{2A}*x=1`, LF-terminated.
- Why the divergence is the specified one: the two directives are unremarkable except for the asterisk in each value, and Section 12.1 makes that asterisk ordinary text here. Discarding both directives on account of it means the baseline treats a character the specification calls literal as a reason to ignore configuration, which is the silent-discard outcome Section 6.3 rules out. The escaped rendering is then forced: Section 16.4 escapes a delimiter occurrence inside a part unconditionally, and the delimiter is the same character.
