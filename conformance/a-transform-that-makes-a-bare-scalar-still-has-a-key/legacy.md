# A transform that makes a bare scalar still has a key

Acceptance item 56. Sections 19.1, 19.2, 19.4, and 15.1.

## What the inputs ask for

`a` and `b` are sequences in the data. `type=multiline` joins each into a single scalar, so by the
time anything is rendered the selected view of each output instance *is* a bare scalar.

Sections 19.1, 19.2 and 19.4 each state the same rule for that shape, once per flat format:

> When the selected output root is a bare scalar, namespace output retains the final concrete
> selector part as the emitted key.

> When the selected output root is a bare scalar, quoted namespace retains the final concrete
> selector part as the assignment name.

> When the selected output root is a bare scalar, INI retains the final concrete selector part as a
> global key.

## Why the timing is the whole case

"The selected output root is a bare scalar" is a property of the view that gets *rendered*. Section
15.1 puts transformations at step 16 and rendering at step 19, so a view's shape is not final until
step 16 has run. A root derived at step 15 answers the question about a sequence and is stale.

Getting that wrong does not produce a wrong key — it produces a blocking `SERIALIZE001` on valid
input, because a bare scalar with an empty root has no key to write at all. The scheme is legal, the
data is legal, every directive binds, and the run fails.

The reverse direction is governed by the same sentence and fails the other way: a view that was a
bare scalar and becomes a container must *lose* the key, or the selector part is emitted twice.

## What the expected tree asserts

Three files, one per flat format, from two selectors.

`a.properties` is `a=x\ny` with `\n` written as the two-character escape. Section 19.1: "Physical
output entries are always one line. Multiline scalar data is represented through escapes, never
literal record-breaking line terminators." The joined value contains a real LF, and the encoder's
value rule renders "LF as `\n`".

`a.sh` is `a='x` LF `y'` — the same value with a **literal** line break inside single quotes.
Section 19.2 is deliberately different here: shell single-quoting "preserves spaces, `$`, backticks,
double quotes, backslashes, exclamation marks, and line breaks without expansion", so the escape
that Section 19.1 requires would be wrong in this format. The two files carrying the same joined
scalar in two spellings is what pins that the difference is intended.

`b.ini` is `b=solo`, a global key with no section header, from a one-item sequence. Joining one item
introduces no separator, which keeps the INI case clear of Section 19.6's rejection of CR and LF in
an INI value — that rule is real and is not what this fixture is about.

## Not asserted

JSON, YAML and XML. Section 14.1 gives JSON and YAML a different answer for the bare-scalar shape —
they "may emit a scalar document" — and all three formats are declined in this preview with exit
`70`, so the item cannot close until Section 19.3, 19.5 and the YAML writer land.

The empty root selector. Section 14.1 requires an explicit `root` there rather than falling back to a
selector part, because there is no final concrete selector part to fall back to. That is a different
branch and a different diagnostic.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 19.1, 19.2, and 19.4 bare-scalar-with-key rule; Section 15.1 step 16
  transformation order; Section 26 item 56. Section 3 does not enumerate the bare-scalar-with-key
  behaviour as its own compatibility line; these are substantive rules of Section 19.
- Legacy observation: the baseline writes `a.properties` with different bytes, does not write
  `a.sh` at all, and writes `b.ini` with different bytes. Exit `0`, no standard error beyond the
  banner. The measurement records `content a.properties; missing a.sh; content b.ini`.
- Clean behavior: after `type=multiline` joins each sequence into one scalar, Sections 19.1, 19.2,
  and 19.4 each retain the final concrete selector part as the emitted key, so `a.properties`
  writes `a=x\ny`, `a.sh` writes `a='x` LF `y'`, and `b.ini` writes `b=solo`.
- Why the difference is intentional: joining a sequence into a bare scalar is a shape change of the
  selected view, and 3.0 fixes what an entry with no key looks like for every flat format so a
  legal scheme is not turned into a blocking `SERIALIZE001`. 2.4.0 had no stated rule for the same
  shape; the observation is that all three flat destinations diverged, but the observable does not
  tell which of the three legacy paths — a missing key, a written filename, or a serializer
  refusal — produced each divergence, and this case is not written to identify it.