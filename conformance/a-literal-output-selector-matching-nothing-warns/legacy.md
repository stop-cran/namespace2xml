# A literal output selector that matches nothing warns and writes an empty file

Acceptance item 75. Section 14.1.

## What the inputs ask for

Two `output` declarations. `a.output=namespace` matches the data. `b.output=namespace` matches
nothing at all, because there is no `b` subtree.

Section 14.1:

> A concrete output instance created by a literal declaration or wildcard expansion remains a
> planned output even when its selected view contains no surviving payload, explicit container
> presence, descendants, or comments

and

> A concrete output instance whose selected view contains nothing also emits `WARN009`, and still
> produces its file as described above.

This is the companion of `wildcard-output-selector-matching-nothing-warns`, and the pair is the
point. The same authoring mistake — a selector naming a subtree that is not there — has two
spellings, and Section 14.1 gives them two different *outcomes*: the wildcard produces no file, the
literal produces an empty one. What it does not do is give them two different volumes. Both warn.

## What this asserts

**An empty selection is reported, not merely tolerated.** The file `b.properties` is written and is
zero bytes, which for namespace output is a well-formed, complete, deployable document. Nothing
downstream of this tool can distinguish it from a configuration that is empty on purpose: it parses,
it deploys, and it silently supplies no settings. The warning is the only stage at which the two can
still be told apart, because it is the only stage that still knows a selector was involved.

**The empty file is still written.** Warning is not refusing. Section 14.1 plans the instance
"even when no data path currently matches its literal prefix", and an author who wants an empty
document keeps getting one, at exit code 0. The change is that they are told.

**One warning, not one per format.** Section 22 counts `WARN009` "once per declaration or expanded
directive". The selection does not depend on the rendered format, so an instance emitting two
formats still warns once — which is why this case declares a single format and the sibling
`structured-bare-scalar-and-empty-documents` case, whose `absent` selector renders both `json` and
`yaml`, expects exactly one `WARN009` too.

**The literal declaration that did match stays silent.** `a` selects content, so it produces no
diagnostic. A rule that warned per output instance rather than per *empty* output instance would
report both.

## Why the diagnostic carries these members and not others

Section 22 supplies a member when the condition itself has the fact that member names.

- `source` and `line` — the condition is a property of the written declaration, at line 2.
- `column` — absent. The condition is about the whole record, not a position inside it.
- `path` — the selector that selected nothing, `b`.
- `declaration` — `b.output`, the directive's canonical spelling.
- `destination` — absent. The condition is about the selection, and is decided at Section 15.1 step
  14, before a destination is planned. The file that results is named later and by other rules.

The anchor is `§14.1`, which is what distinguishes this `WARN009` from Section 15.2's unbound
directive.

## Not asserted

The message prose, which Appendix C.4 exempts.

Whether a view emptied *later* — by `type=ignore` at step 16 rather than by selecting nothing at
step 14 — also warns. It does not, and deliberately: that is a directive doing what it was asked
to do, while this condition is a selector finding nothing to do it to. The distinction belongs to a
case of its own, and `type-ignore-removes-a-subtree-and-strands-its-directives` already covers the
directive side of it.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 14.1's planned-output rule and its empty-selection warning; Section 22
  `WARN009`; Section 26 item 75.
- Legacy observation: the baseline exits `0`, writes `a.properties`, and writes **no file at all**
  for `b` — it neither reports the empty selection nor produces the document Section 14.1 requires.
  Its standard error carries only its banner and its two `Reading input` lines.
- Clean behavior: `a.properties` holding `x=1`, `b.properties` holding nothing, and one `WARN009`.
- Why the divergence is the specified one: the baseline is silent *and* absent, so it diverges from
  Section 14.1 twice over, in opposite directions. Dropping the file contradicts "remains a planned
  output"; saying nothing leaves the author of a mistyped selector with no signal from any stage.
  The combination is the worst of the two available answers — a pipeline that templates a filename
  per environment gets a missing file in one environment and a present one in another, with no
  diagnostic to attribute the difference to. Reported as issue 79, whose finding was that the
  silence, not the file, is what makes the two spellings of one typo behave differently.
