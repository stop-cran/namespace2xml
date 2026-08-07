# A wildcard `filename` substitutes the selector's captures

Acceptance item 49. Sections 14.1 and 16.2.

## What the inputs ask for

`a.*.output=namespace` expands into the instances `a.x` and `a.y`, exactly as in
`wildcard-output-selectors-expand-per-capture-tuple`. `a.*.filename=cfg/*.conf` then chooses where
each of them is written.

Section 14.1:

> A wildcard `filename` may use the same captures to choose another destination.

*The same* captures — not a second match. The `*` in the filename is filled from the tuple the
selector expansion already bound, so `a.x` writes `cfg/x.conf` and `a.y` writes `cfg/y.conf`.

## What this asserts beyond the expansion itself

Three things, and each fails differently.

**The captures reach the filename.** Substituting an empty capture set instead would give both
instances the destination `cfg/.conf`, and they would collide.

**A wildcard filename is not silently discarded.** This is the shape of the defect the case is
really aimed at. `filename` compiles from a directive value, and a value containing a wildcard has
no literal text; reducing it to that text yields `null`, which is the encoding for *no filename
directive at all*. The two instances would then land on their Section 16.2 default names,
`a.x.properties` and `a.y.properties`, with no diagnostic — a run that ignores configuration the
user wrote and says nothing. Section 6.3 does not admit that outcome, and the expected tree here
names neither default.

**A literally written separator makes a directory.** Section 16.2: "Literal `/` or `\` separators in
`filename` intentionally create subdirectories", and the `cfg/` component is written in the scheme
rather than substituted, so it is split before substitution happens. The complementary rule — that a
separator arriving *inside* a capture is encoded rather than obeyed — is not exercised here; it
belongs with the portable-segment cases.

**No extension is appended.** `.conf` is the whole name. Section 16.2: "An explicit `filename` value
is the complete relative destination path … A format extension is never appended to it", and the
format is `namespace`, whose default extension would otherwise be `.properties`.

## Not asserted

What a wildcard in a `filename` means under a selector that bound no captures. The specification
does not say, and this fixture does not pretend to settle it.
