# Ansible integration: findings

**Status: open. Non-blocking. Nothing here ships, and nothing in the repository depends on it.**

Contract bundle at the time of writing: `r98+5c76f294ea85` (`3.0.0-preview.4`).

## The question

Issue #37 asks how this tool should be reachable from Ansible, and proposes a filter plugin with a
thin action plugin later. Three obstacles were named in advance: the tool has no stdin/stdout mode,
an XML round trip normalizes the document, and a scheme is mandatory. This spike exists to find out
whether those obstacles are real, and how expensive each one is.

So the question is not "can a filter plugin call this tool". It obviously can. It is **"what does
each of the three named obstacles actually cost, measured, and which of them is worth engineering
against"**.

## What is verified in this session

| Claim | Evidence |
|---|---|
| A filter plugin can encode arbitrary Python data as a namespace profile that the tool reads back as the same data. | `test_filter.py` side A, 3 cases covering ordinary nesting, hostile name parts and hostile values, compared against profile text authored from Sections 8.2, 8.3 and 8.7. 8/8 green on Windows and on `ubuntu-latest`. |
| The marshalling is not where the time goes. | `measure-invocation.py` on `ubuntu-latest`, 25 timed runs: **1.53 ms of 171.8 ms (0.9 %)** on a 25-record profile, **1.46 ms of 199.4 ms (0.7 %)** on a 625-record one. |
| Memoization collapses the fleet case. | Same harness: a cache hit is **0.068 ms** and **1.63 ms**. 500 hosts × 3 files projects from **257.7 s to 0.62 s** and from **299.1 s to 3.04 s**. |
| A playbook rendering three files is idempotent, and the filter contributes nothing to making it so. | `playbook.yml` run twice under `ansible-core 2.21.3`: the second run reports `ok=4 changed=0`. The workflow fails if it does not. |
| A real 33 KB XML file survives a round trip with one line of content lost and 69 lines of cosmetic change. | The `logback.xml` attached to issue #24: 607 lines, **70 changed (11.5 %)**, of which 68 are `<x/>` → `<x />`, one is `encoding="UTF-8"` → `encoding="utf-8"`, and **one is a discarded comment**. |
| Comments outside the document element are discarded by the XML reader, silently, at exit 0. | A 3-comment document reports `WARN003 … 1 XML comment(s)`; `logback.xml` has 8 comments and reports 7. Both counts exclude exactly the comments outside the document element. |
| The prototype cannot emit an XML attribute, and fails loudly rather than quietly when asked to. | `{"appender": {"@name": "STDOUT"}}` encodes to `cfg.appender.\@name`, which is blocking `XML002 §11.2` because `\@name` is not an `NCName`. |
| The oracle can fail. | Three mutations to the encoder — never escape a leading `Q`, drop the empty-container sentinel escaping, emit lowercase hex — each killed side A; control green. |

### The measurements, in full

`ubuntu-latest`, `x86_64`, JIT, published `3.0.0-preview.4` (contract bundle `r90+e172e0ba4d2a`),
25 timed runs after warmups. **These are the reference numbers**: an Ansible controller is
POSIX-only, so this is the platform the integration would run on.

| | 8 leaves / 25 records / 728 B | 200 leaves / 625 records / 19 155 B |
|---|---|---|
| `bare` — `--version`, the process floor | 47.5 ms | 45.5 ms |
| `prepared` — spawn and read, profile already on disk | 170.2 ms | 197.9 ms |
| `full` — the whole filter call | 171.8 ms | 199.4 ms |
| `memoized` — a cache hit | 0.068 ms | 1.63 ms |
| **marshalling** = `full` − `prepared` | **1.53 ms (0.9 %)** | **1.46 ms (0.7 %)** |
| startup floor as a share of `full` | 27.6 % | 22.8 % |
| transformation work = `prepared` − `bare` | 122.8 ms | 152.5 ms |

The same harness on `win-arm64` against a local `r98+5c76f294ea85` build reports marshalling at
20.8 ms (10.9 %) and 12.9 ms (5.6 %) — **an order of magnitude worse, and misleading**. Creating and
deleting three temporary files is far more expensive on Windows than on Linux, so the platform that
does not run Ansible controllers is the one that makes the strongest case for avoiding temporary
files. Measuring only where it was convenient would have produced the opposite conclusion.

**Almost all of the per-call cost is fixed.** Twenty-five times the data buys 24 % more time
(122.8 ms → 152.5 ms), so the great majority of that ~123 ms is warm-up rather than work. The true
fixed cost of a call is therefore around 168 ms of a 172 ms call, not the 47.5 ms `bare` suggests:
`--version` returns before the pipeline is ever touched, so it understates the floor badly. That
makes ahead-of-time compilation (#28) a larger prize than the 27.6 % figure implies, and it makes
memoization the only lever that matters inside the integration.

## What each obstacle actually costs

**Obstacle 1, no stdin/stdout — the cheapest of the three by an order of magnitude, and the evidence
argues against fixing it.** Writing two temporary files and reading one back costs **0.7–0.9 %** of a
call on the platform Ansible runs on. The deferred `--stdout` decision (G4, tracked as #26) was
deferred on the argument that no measured need had appeared; this measurement is that need failing to
appear again, on the workload most likely to produce it. **This spike does not justify reopening
G4.** It is a negative result and it is the answer the issue asked for.

The two larger components are process startup and pipeline warm-up, together roughly 98 % of a call,
which is what Native AOT (#28) attacks. The lever ordering is therefore **memoization ≫ AOT startup
≫ stdout marshalling**, and only the first of those lives in the integration.

**Obstacle 2, XML round-trip normalization — real, one-time, and smaller than it looks.** 11.5 % of
lines change, which sounds alarming and is almost entirely `<x/>` → `<x />`. A team adopting the
tool takes that diff once, at the commit where the file becomes generated, and never again. The
substantive loss is one comment, discussed below.

**Obstacle 3, a mandatory scheme — dissolved by the filter, not worked around.** `synthesize_scheme`
writes the two or three directives the format needs from the filter's own arguments. The caller
supplies `root` for XML because Section 14.1 requires an element name for a multi-member view, and
that name is a fact about the target document rather than about the data, so guessing it would be
the filter inventing contract. No other scheme text is needed for the common case, and a caller who
needs more can pass their own.

## The one real defect this found

Comments in the XML prolog and epilog — outside the document element — are discarded by the reader.
Nothing reports it and the run exits 0.

Section 11.5 states the rule without qualification:

> XML comments are retained as ordered comment nodes.

The writer supports the positions those comments would need. Section 20 says a comment bound to no
value is written at document level, "a document-leading comment after the XML declaration and before
the document element, and a document-trailing comment after the document element". A namespace or
YAML source reaches that position, because Sections 8.5 and 10.1 classify a top-of-file comment as
document-leading. An XML source cannot, so the writer can emit a node the XML reader can never
produce, and a comment Section 11.5 says is retained is not.

`KNOWN-LIMITS.md` states that "an XML comment survives an XML-to-XML run", which is true of a comment
holding a content-token slot and false of one outside the document element.

This is reported rather than fixed here, because a spike is not the place to change the reader and
because the fix has a design question in it: a document-leading comment has no content-token slot to
occupy, so where it lives in the model is a real decision rather than an oversight to patch.

## Fidelity limits inherent to the mapping

These are properties of encoding typed data as text, not defects, and any published collection has
to document them:

- **Section 18 infers payload type from value text.** The string `"true"` and the boolean `True`
  produce the same record, as do the string `"3"` and the integer `3`. A `type` scheme rule is the
  only way to force a string.
- **Section 8.7 infers sequences from canonical decimal parts.** A mapping whose keys are `"0"` and
  `"1"` becomes a sequence. A leading zero disables inference for the whole parent, which is a
  sharp edge for zero-padded keys.
- **Binary data has no spelling.** The prototype refuses `bytes` rather than guessing an encoding.
- **Section 8.3 gives values no `\u{HEX}` form.** Only `\\`, `\*`, `\${`, `\n`, `\r` and `\t` are
  escaped; everything else is written literally. This is sufficient — a raw U+000B and a raw U+2028
  in a value both survive a round trip, verified — but it means a value cannot be escaped into
  safety the way a name part can.
- **The prototype cannot emit an XML attribute, a content token or a qualified element.** This is
  the direct cost of making the encoding total: every key is escaped so that it reads back as
  itself, so `@name` becomes `\@name`, which is a literal element name — and, because `\@name` is
  not an `NCName`, a blocking `XML002` rather than a wrong document. Failing loudly is the right
  direction, but it means the single most common dict-to-XML convention, the `@`-prefixed attribute
  key used by `xmltodict` and every badgerfish variant, does not work at all.

  A published collection has to resolve this, and the resolution is a design decision rather than a
  patch: either an opt-in argument that stops escaping the Section 11.4 markers, or a second filter
  with an explicitly XML-shaped convention. The prototype deliberately does neither, because
  choosing between them is what the issue is for.

## The decisions this spike supports

1. **A filter plugin is the right primary shape**, and an action plugin is a later convenience
   rather than a requirement. The filter is a pure function from data to text; `ansible.builtin.copy`
   already owns idempotence, check mode, diff, backup, mode and ownership, and does them better than
   a new module would. `playbook.yml` demonstrates exactly this, and the workflow fails if a second
   run reports a change.
2. **A separate repository and collection, `namespace2xml.core`.** The Python has a different
   release cadence, a different test runner and a different audience from the .NET tool, and Galaxy
   wants a repository shaped its way. Vendoring it here would couple the tool's release to Ansible's.

## The bar a published collection has to clear

Written down now, before there is a decision to be charitable towards:

1. **The oracle is two-sided and stays that way.** Side A compares the encoder against profile text
   authored from the specification; side B compares the filter against the tool. Neither side may
   be regenerated from the other, and neither may be regenerated from the filter.
2. **A second playbook run reports `changed=0`.** If it does not, either the tool is not
   deterministic or the filter is feeding it something that varies.
3. **The fidelity limits above are in the collection's README**, not only here. A user who does not
   know that `True` and `"true"` collide will find out by shipping it, and a user who writes
   `@name` expecting an attribute has to be told before the run fails rather than by the run
   failing.
4. **Memoization is on by default and provably bypassable.** It is the only lever in the
   integration worth anything, and a cache nobody can turn off is a cache nobody can debug.

## What is not yet known

**Whether the ~123 ms of "transformation work" is warm-up or work.** The evidence says mostly
warm-up — 25× the data costs 24 % more time — but that is an inference from two data points, not a
profile. It matters because it decides how much of a call ahead-of-time compilation could actually
remove, and #28's bar is written in milliseconds of median startup.

**Whether markers should be reachable, and how.** See the fidelity limits above. This is the one
question the spike deliberately leaves open, because both answers are defensible and the choice
belongs in the collection's design rather than in a prototype.

**Everything about Galaxy.** Packaging, naming, the `requires_ansible` floor, and whether the tool
binary is a documented prerequisite or something the collection installs.

**Windows as a target.** `ansible-core` has no Windows controller support and WSL on the authoring
workstation is broken (`Wsl/Service/CreateInstance/CreateVm/HCS/0x80090006`), so nothing here says
anything about running the filter from a Windows controller. That is a limitation of Ansible, not of
this tool, and it is unlikely to change.

## Traps, recorded because they cost time here

**A `workflow_dispatch`-only workflow on a non-default branch cannot be dispatched.** `gh workflow
run` answers `HTTP 404: workflow … not found on the default branch`, and the default branch here is
still 2.x. `native-aot-spike.yml` was authored that way and **has never run**, which is why its
FINDINGS still lists its central measurement as unknown. A push trigger scoped to the spike's own
paths fixes it. A measurement that cannot be taken is indistinguishable from one that was not taken.

**Measuring on the convenient platform gave the opposite answer.** Temporary-file marshalling is
0.9 % of a call on Linux and 10.9 % on Windows-on-ARM. Had this stopped at the workstation, the
recommendation would plausibly have been to reopen G4 on the strength of a number from the one
platform that cannot run an Ansible controller.

**A mutation differing only in letter case survives a PowerShell no-op guard.** `-eq` on strings is
case-insensitive in PowerShell, so `if ($mutated -eq $original) { throw }` passes a `%X` → `%x`
mutation straight through as if it had not applied. Use `-cne`. The harness reported a mutation it
had never written.

**`[IO.File]` uses the process working directory, not PowerShell's.** `cd $env:TEMP` then
`[IO.File]::WriteAllText("in.txt", …)` writes to the session's starting directory. The tool then
reports `WARN001 … does not exist` for a file that is plainly there in the shell, and the run
succeeds at exit 0 with an empty model. Use absolute paths.

**A hex-escape test pinned nothing until a letter appeared in it.** The only escaped scalar in the
oracle was U+0009, whose hex spelling is `9` in either case, so the mutation emitting lowercase
digits produced byte-identical output and survived. U+000B and U+200B were added for exactly this.
Note also that side B *cannot* kill that mutation and should not be expected to: Section 8.2 says
`\u{HEX}` "input accepts either case and normalized output uses uppercase", so the tool reads both
spellings alike. The uppercase choice is pinned on the side that compares text.

**The console renders U+2028 as a space.** A value carrying one appeared to have been corrupted into
`a b` on screen. `Format-Hex` shows `E2 80 A8` intact in JSON and XML and `\u2028` escaped in YAML,
which is correct in all three. Read the bytes before reporting a data-loss bug.

**The model is rooted at the XML document element, not at the file name.** `logback.output=xml`
against `logback.xml` selects nothing and fails with `TYPE001` after a `WARN009`, which reads like
a scheme syntax problem and is not one. The selector is `configuration`.
