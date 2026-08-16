# `merge=error` rejects a second source contribution

Acceptance item 25. Section 16.10 `error`, and Section 22's rule for which members a condition
supplies.

## What the inputs ask for

`strict.merge=error` is declared at `strict`. Two input files contribute there: `first.txt` writes
`strict.a` and `strict.b`, `second.txt` writes `strict.c`.

Section 16.10 makes `error` reject "any distinct second source or generated contribution at the
path", and Section 16.10's definition of *at* is broad: "A contribution is **at path P** when it
contributes a payload, explicit container presence, sequence projection, or **any descendant under
P**." `second.txt` contributes no payload at `strict` itself, only a descendant, and that is enough.

One contribution is therefore fine and the second is refused. The run reports a blocking error, and
Section 21.2's validation gate means nothing is published — hence exit 1 and no `expected/` tree.

## Why exactly one diagnostic, and why it carries `path` and nothing else

This is the fixture Section 22 could not previously determine, and it is worth writing down why it
can now.

Section 22 makes each optional member a property of the condition, not of the code: a member is
supplied "when the condition itself has the fact that member names". `TYPE001` may carry `source`,
`line`, `column`, `path`, `declaration` and `destination`, and this condition has exactly one of
them.

- `path` — the condition concerns one overlay node, `strict`, and names it.
- `source` — **omitted**, and this is the part that had to be decided rather than observed. The
  condition is a property of the path *after folding*: it is not that `second.txt` is malformed, but
  that a second contribution of any kind arrived. Section 22's tie-breaker settles it — these
  members name "where the condition is, not where its subject came from". Attributing the
  diagnostic to one of its two contributions would make the report depend on which contribution the
  implementation happened to be holding when it noticed, and Section 24 forbids output that depends
  on that.
- `line` and `column` — absent for the same reason, and dependent on `source` in any case.
- `declaration` — the `merge=error` declaration is what *enabled* the condition, not what the
  condition is about. A reader who wants it has the path, and the declaration is at that path.
- `destination` — nothing has been planned yet; this is the input phase.

The Section 8.7 concatenation warning (`WARN004`) is the same shape and already carries `path` with
no `source` in `namespace-input-merge-strategies`, which is an independent check on the reading:
that fixture was authored before Section 22 said this, from the same argument.

## Why the anchor is §16.10 and the phase is `input`

Appendix B routes "input `merge=error` conflict" to `TYPE001`, which is the least specific of the
codes and needs its anchor to say which of its six conditions was met. Section 22 makes phase and
anchor "properties of the individual occurrence" for exactly this code, so `§16.10` and `input` are
both compared here even though neither is derivable from `TYPE001` alone.

## Not asserted

The message prose. Appendix C.4 exempts `message` from comparison, and this fixture says nothing
about how the conflict is worded.

## Legacy differential

- namespace2xml 2.4.0: **differs**. The baseline exits 0 and writes `strict.properties` (the
  harness records `extra strict.properties`), where the case expects exit 1 and an empty tree.
- Contract: Section 3.2 preserves `merge` "with input/common-model scope" — this fixture
  exercises that preserved scope — and Section 16.10's definition of `error` is the substantive
  rule. The clause a contribution is "at path P" if it "contributes … any descendant under P"
  is the reading Section 22 required.
- Legacy observation: 2.4.0's `merge=error` did not treat a descendant-only second contribution
  as a second contribution at the parent. `second.txt` writes `strict.c` without touching
  `strict` itself, so on the baseline no rejection fires; both sources merge normally and
  `strict.properties` is published with all three keys.
- Clean behavior: `strict.merge=error` refuses "any distinct second source or generated
  contribution at the path", where *at* covers any descendant. The condition is a `TYPE001`
  error with `path` of `strict` and no `source` — Section 22 says an input-phase condition
  reached only after both sources arrived cannot be attributed to either alone. The run exits
  1 and Section 21.2's validation gate prevents publication.
- The difference is intentional: `error` exists so that a scheme author can insist a subtree
  come from exactly one source, and that guarantee has to reach descendants or a typo in a
  second source silently shadows values in the first.
