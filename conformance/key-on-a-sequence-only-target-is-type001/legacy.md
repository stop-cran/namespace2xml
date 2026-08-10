# `key` on a sequence-only target is `TYPE001`

Acceptance item 67. Section 16.5.

## What the inputs ask for

`cfg.a` has two children, `0` and `1`. Section 8.7 classifies a nonempty mapping as
sequence-inferable when "all its surviving concrete child names are canonical nonnegative decimal
ordering values", and both of these are, so `cfg.a` projects an indexed sequence and nothing else.
The scheme then asks for a `key` transformation at that path.

Section 16.5 closes the case in one sentence:

> Applying `key` to a sequence-only or scalar-only target is `TYPE001`.

and states the precondition twice more — "the target must be an ordered mapping", and "if no mapping
projection exists, it is a blocking type error".

## Why this case is worth pinning

Section 16.5 also says, of the sequence a `key` transformation produces:

> If an independent sequence projection already exists at the same node, the transformed
> contribution merges with it under the effective `merge` strategy.

That clause has no reachable input, and this fixture is one of the two rules that makes it so.
A sequence projection reaches a node in exactly two ways. Section 8.7 infers one only when *every*
surviving child name is an ordering value, so a single named child makes the node an ordered mapping
instead and there is no sequence to merge with. A native sequence contribution instead contests the
mapping under Section 4.4, which resolves exclusively and before step 16, so the loser is gone before
`key` runs. What remains is the case here — a genuine sequence with no mapping at all — and Section
16.5 refuses it rather than reaching the merge.

The unreachability argument is recorded in `KNOWN-LIMITS.md` section 1.12 and tracked as
[#61](https://github.com/stop-cran/namespace2xml/issues/61), which proposes amending the clause. An
amendment resting on this behavior needs the behavior pinned: if `key` ever began accepting a
sequence-only target, the clause would become reachable again and the amendment silently wrong.

## Why the diagnostic carries these members and not others

- `code` — `TYPE001`. The condition is a property of the data the directive met, not of the scheme's
  own syntax or option combination, which is what separates it from the `SCHEME001` of
  `type=array` plus `key`.
- `phase` — `planning`. The transformation runs at step 16.
- `source` and `line` — the `key` declaration, at line 2.
- `path` — `cfg.a`, the canonical absolute path the directive takes effect at.
- `declaration` — `cfg.a.key`, the directive's canonical spelling without its value.
- `column` — absent. The condition is about the whole record, not a position inside one.
- `destination` — absent. No file is produced.

## Not asserted

The message prose, which Appendix C.4 exempts.

The exit code is 1, and the absence of an `expected` directory asserts that nothing is written: the
error is blocking and reached before rendering.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 16.5's "applying `key` to a sequence-only or scalar-only target is `TYPE001`" and its requirement that the target be an ordered mapping; Section 8.7 sequence inference; Section 26 item 67.
- Legacy observation: the baseline exits `0` and writes `cfg.properties` containing `a.0.v=1` and `a.1.v=2`, 18 bytes. The measurement records `exit 0 (expected 1); extra cfg.properties`. Standard error carries only the banner and progress lines, ending in "Success! Exiting...", so no diagnostic is emitted. Re-running the baseline with the `cfg.a.key=name` line deleted produces a byte-identical file, which is the evidence that the directive was not applied in some other form but ignored outright.
- Clean behavior: one blocking `TYPE001` naming the declaration and the path, exit `1`, and no file.
- Why the difference is intentional: a `key` directive that names a field the output never contains has done nothing, and reporting success in that case tells an author their transformation worked. The control run is what makes this a silent no-op rather than a difference of opinion about the result -- there is no result. Section 3.2 lists the correction of silently ignored directives among the reasons for the rewrite, and Section 16.5 states the refusal three separate times, which is the specification treating this as a case an implementation is likely to get wrong.