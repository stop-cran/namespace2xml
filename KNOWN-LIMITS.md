# Known limits

**As of `3.0.0-preview.1`, contract bundle `r2+0654ebfa8b7a`. Dated 2026-08.**

This file exists because a project that claims completeness cannot receive feedback: every gap reads
as user error, and the reporter concludes they are holding it wrong. During the preview this list is
long, and that is correct. It shrinks as milestones land.

If something you need is here, **say so** — an entry on this list is a statement of current state,
not a refusal. Adding your case to the relevant thread is what moves it.

---

## 1. Implementation completeness

The 3.0 rewrite lands in milestones that follow the specification's own pipeline order. Until a
milestone merges, the corresponding pipeline stage is **not implemented**, and the tool exits with a
non-normative status rather than pretending to succeed.

| Area | State | Specification |
|---|---|---|
| Command line, informational modes, diagnostics encoding | Implemented | §6, §6.4 |
| Contract bundle reporting | Implemented | §22 |
| Input and scheme parsing | Not yet | §7–§9 |
| Model construction, overlaying, reference resolution | Not yet | §12–§16 |
| Output planning | Not yet | §17–§18 |
| Rendering: namespace, quoted namespace, JSON, YAML, INI | Not yet | §19 |
| Rendering: XML | Not yet | §11, §19 |
| Publication and the validation gate | Not yet | §21 |

A preview binary returns exit status `70` when it reaches unimplemented pipeline work. That status is
deliberately outside the contract: `0` and `1` are normative, and a preview must never return either
for work it did not do.

## 2. Acceptance coverage

`conformance/assertions.json` records all 85 acceptance requirements from specification §26, each
with a status. Items marked `pending` have **no fixture coverage yet** and no claim is made about
them. Items marked `required` are covered and can never lose coverage.

Do not read a passing test run as evidence about a `pending` item.

## 3. Platform and environment

- **Supported:** Linux, Windows and macOS on x64 and arm64, via the .NET 10 runtime.
- **Not yet validated:** nothing. The Windows publication path is proven by the
  `spikes/windows-publication` prototype, which walks destinations component-by-component with
  `NtCreateFile` relative to retained parent handles, and is therefore TOCTOU-safe by construction
  rather than by a check. Two cases could not be exercised where the spike ran because creating a
  symbolic link needed privileges that were unavailable; they are recorded as untested rather than
  as passing.
- **Hard-link escape is out of scope.** A destination reached through a hard link to a file outside
  the output root cannot be detected by any no-follow walk, on any platform, because a hard link is
  not distinguishable from the original name. An optional refusal based on link count is
  demonstrated in the spike but is not enabled.
- **Native AOT** is a non-blocking investigation, not a shipped configuration.
- **YAML comment preservation** relies on a two-pass read: parser events for structure, plus a
  second scanner pass for the comment token inventory, because the parser event stream truncates
  comment-only documents and misreports inline-ness on root values. This is proven in
  `spikes/yaml-comments` but not yet implemented.

- **Globalisation is invariant by construction.** The tool sets `InvariantGlobalization`, so
  behaviour cannot vary with the host locale. This is deliberate and will not become configurable:
  it is what makes byte-identical output achievable.

## 4. Documented but not yet enforced

Some rules in `CONTRIBUTING.md` are stated as binding and have a CI gate; a few do not yet.

| Rule | Gate |
|---|---|
| C1 requirement-and-fixture-first | Partial — the manifest and traceability tests exist; nothing reads acceptance items out of the pull-request body, so "fails before" is reviewer-verified |
| C2 cite the specification | Partial — anchors are constrained by the registry and schema tests, but the citation itself is a template field, not a gate |
| C3 specification decision precedes acceptance | Contract-revision job, active |
| C4 bidirectional traceability | Active |
| C5 determinism | Active — `tools/hash-corpus-outputs.ps1` measures exit status, standard output, standard error and the produced file tree, for every argument vector each case declares, and `cross-os-hash` requires all three platforms to agree |
| C6 side-effect invariants first | Not yet — the §21 fixtures do not exist until publication is implemented, and no workflow contains a job by that name |

Stating a rule before its enforcer exists is a deliberate choice, but it is a debt. It is recorded
here rather than left implicit. `CONTRIBUTING.md` §3 repeats these qualifications inline; if the two
ever disagree, this table is the one that is maintained and the discrepancy is itself a bug report.

## 5. Documentation gaps

- `docs/usage-methodology.md` is an outline. The layering guidance is sound; the worked pipelines are
  not written yet.
- `docs/migration-2.x-to-3.0.md` is assembled from each fixture's `legacy.md` as fixtures land, so it
  is incomplete until the corpus is.
- There is no cookbook, and there probably should be. If you built something with this tool and had
  to work it out yourself, that is a **usage gap** report and it is the most valuable kind.

## 6. Things that are deliberately not going to change

Listed so nobody spends effort proposing them.

- **No snapshot-update mode.** No `--update-snapshots`, no regeneration of expected fixture output
  from the tool's own output. A test that records the implementation's opinion validates nothing, and
  the ability to distinguish correct from customary is the project's main asset.
- **No locale-sensitive behaviour.** See §3.
- **No publication from a branch.** Releases come from tags only.
- **No relaxation of the validation gate.** If any output fails validation, nothing is written. There
  will not be a `--force`.

## 7. How to report against this file

| Your situation | Route |
|---|---|
| An entry here is wrong or out of date | [Bug report](https://github.com/stop-cran/namespace2xml/issues/new?template=bug_report.yml) |
| You need something in §1 or §5 sooner | Comment on the tracking issue for that milestone |
| Something surprised you and is *not* listed here | [Pick a form](https://github.com/stop-cran/namespace2xml/issues/new/choose) — an unlisted gap is exactly what the preview is for |

Always include the `contract-bundle` revision from `--version`.
