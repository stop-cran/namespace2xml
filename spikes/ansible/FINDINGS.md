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
| A filter plugin can encode arbitrary Python data as a namespace profile that the tool reads back as the same data. | `test_filter.py` side A, 3 cases covering ordinary nesting, hostile name parts and hostile values, compared against profile text authored from Sections 8.2, 8.3 and 8.7. 8/8 green. |
| The marshalling is not where the time goes. | `measure-invocation.py`, 15 timed runs after 3 warmups: **20.8 ms of 191.1 ms (10.9 %)** on a 25-record profile, **12.9 ms of 230.0 ms (5.6 %)** on a 625-record one. |
| Memoization collapses the fleet case. | Same harness: a cache hit is **0.098 ms** and **2.35 ms** respectively. 500 hosts × 3 files projects from **286.6 s to 0.72 s** and from **345.0 s to 4.21 s**. |
| A real 33 KB XML file survives a round trip with one line of content lost and 69 lines of cosmetic change. | The `logback.xml` attached to issue #24: 607 lines, **70 changed (11.5 %)**, of which 68 are `<x/>` → `<x />`, one is `encoding="UTF-8"` → `encoding="utf-8"`, and **one is a discarded comment**. |
| Comments outside the document element are discarded by the XML reader, silently, at exit 0. | A 3-comment document reports `WARN003 … 1 XML comment(s)`; `logback.xml` has 8 comments and reports 7. Both counts exclude exactly the comments outside the document element. |
| The oracle can fail. | Three mutations to the encoder — never escape a leading `Q`, drop the empty-container sentinel escaping, emit lowercase hex — each killed side A; control green. |

### The measurements, in full

`win-arm64`, JIT, `3.0.0-preview.4`, 15 runs after 3 warmups. **These are not the reference
numbers**; see "What is not yet known".

| | 8 leaves / 25 records / 728 B | 200 leaves / 625 records / 19 155 B |
|---|---|---|
| `bare` — `--version`, the process floor | 72.5 ms | 70.4 ms |
| `prepared` — spawn and read, profile already on disk | 170.2 ms | 217.0 ms |
| `full` — the whole filter call | 191.1 ms | 230.0 ms |
| `memoized` — a cache hit | 0.098 ms | 2.35 ms |
| **marshalling** = `full` − `prepared` | **20.8 ms (10.9 %)** | **12.9 ms (5.6 %)** |
| startup floor as a share of `full` | 38.0 % | 30.6 % |
| transformation work = `prepared` − `bare` | 97.7 ms | 146.7 ms |

## What each obstacle actually costs

**Obstacle 1, no stdin/stdout — the cheapest of the three, and the evidence argues against fixing
it.** Writing two temporary files and reading one back costs 5.6–10.9 % of a call. The deferred
`--stdout` decision (G4, tracked as #26) was deferred on the argument that no measured need had
appeared; this measurement is that need failing to appear again, on the workload most likely to
produce it. **This spike does not justify reopening G4.** It is a negative result and it is the
answer the issue asked for.

The two larger components are process startup at 30–38 %, which is what Native AOT (#28) attacks,
and the transformation itself. The lever ordering is therefore **memoization ≫ AOT startup >
stdout marshalling**, and only the first of those lives in the integration.

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

The writer supports the position: Section 20 says a comment bound to no value is written at
document level, "a document-leading comment after the XML declaration and before the document
element, and a document-trailing comment after the document element". A namespace or YAML source
reaches that position, because Sections 8.5 and 10.1 classify a top-of-file comment as
document-leading. An XML source cannot, so the writer can emit a node the XML reader can never
produce.

The word "prolog" does not occur in the specification, so Section 11 does not say what becomes of
such a comment. `KNOWN-LIMITS.md` states that "an XML comment survives an XML-to-XML run", which is
true of a comment holding a content-token slot and false of one outside the document element.

This is filed as a report rather than fixed here: the specification is silent, and this repository
does not settle a silence by encoding a guess in a fixture.

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
   know that `True` and `"true"` collide will find out by shipping it.
4. **Memoization is on by default and provably bypassable.** It is the only lever in the
   integration worth anything, and a cache nobody can turn off is a cache nobody can debug.

## What is not yet known

**Linux numbers.** Every timing above is Windows-on-ARM with a JIT build, and an Ansible controller
is POSIX-only, so the platform that matters was not measured here. WSL on this workstation is
broken (`Wsl/Service/CreateInstance/CreateVm/HCS/0x80090006`) and `ansible-core` has no Windows
controller support, so no playbook has been run locally at all.
`.github/workflows/ansible-spike.yml` measures both on `ubuntu-latest` on manual dispatch and
uploads `timings.json`, `outcome.txt` and the rendered outputs.

**Whether the ~98 ms of "transformation work" is work or warm-up.** It is 97.7 ms for a 25-record
profile and 146.7 ms for a 625-record one — a 25× increase in data buying a 1.5× increase in time,
which looks far more like JIT warm-up of the pipeline than like data-proportional work. If it is
warm-up, it is startup by another name and belongs to #28's budget rather than to the transformer's.
Not measured.

**Everything about Galaxy.** Packaging, naming, the `requires_ansible` floor, and whether the tool
binary is a documented prerequisite or something the collection installs.

## Traps, recorded because they cost time here

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
