# Changelog

All notable changes to the tool, to the contract, and to `CONTRIBUTING.md` are recorded here.

Each entry records what was added, what was **removed**, and — where applicable — **which inbound
report caused it**. The second and third of those are the point: an entry that only grows proves
nothing, and "we value feedback" is not evidence that a loop closed.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). The `contract-bundle` revision is
recorded separately from the package version, because the contract and the implementation move
independently.

## [Unreleased]

Nothing yet.

## [3.0.0-preview.2] - 2026-08-09

The preview that first transforms **every input format and every output format the specification
defines**, end to end. `3.0.0-preview.1` read namespace profiles only.

### Contract

- `contract-bundle` `r37+2d644be6926e`, covering `docs/specification.md` and
  `spec/diagnostics.registry.json`. Seven revisions since `r30`.
- §11.4 now defines *format-agnostic alias* and makes `Q{}local-name` the explicit canonical
  spelling of an unqualified element, so a path can be written that bypasses the alias index.
  **Caused by [#43](https://github.com/stop-cran/namespace2xml/issues/43)**, which observed that the
  term was used but never defined and that the spelling matched nothing.
- §12.2 no longer makes an inconsistent repeated capture a silent nonmatch; it is `WILDCARD001`, as
  §22 and Appendix B already said. **Caused by
  [#44](https://github.com/stop-cran/namespace2xml/issues/44)**, a contradiction between three
  clauses.
- §17.1 defines what an explicit later ordering value *patches*. **Caused by
  [#45](https://github.com/stop-cran/namespace2xml/issues/45)**.
- The `rule` diagnostic member has a normative value, so `WILDCARD002` can be given a fixture at
  all. **Caused by [#46](https://github.com/stop-cran/namespace2xml/issues/46)**.
- §22 lists diagnostic members **per condition** rather than per code, which is what a fixture
  author needs. **Caused by [#47](https://github.com/stop-cran/namespace2xml/issues/47)** — the
  report that blocked an acceptance item until it was resolved.
- §11.4 records the content-token placement question rather than leaving it to whoever implemented
  it first.
- Appendix C gained the run-B token-vector rules and the legacy differential lane (C.6).

### Added

- **Input front ends for JSON (§9), YAML (§10) and XML (§11)**, each with the §15.1 projection and
  secure default parsing. XML brings canonical addressing for attributes, namespaces, repeated
  children, mixed content, comments and CDATA; retained comments as ordered content nodes; the
  §16.8 input options and §11.7's `NormalizeFormattingWhitespace` compatibility mode.
- **§12 wildcard template evaluation**, §8.6 permanent exclusion masks and §8.7 numeric-map sequence
  inference, with the wildcard fixed point bounded and the bound's spender named.
- **§13 reference resolution**, including cycle canonicalisation with an injective ring identity.
- **Step 16 path-scoped view transformations** — §16.5 `key` and §16.6 `type` — and wildcard output
  selector expansion into concrete instances.
- **Output planning**: §16.2 destinations composed from the template, the §17.5 destination
  collision fold, and per-path high-water marks carried through a replace.
- **Rendering for every output format**: namespace, quoted namespace, INI, JSON, YAML and XML,
  including the four XML node kinds a `type` directive can name and the §19.5 sequence projection.
- **§21 validation and secure publication**, publishing through handle-relative no-follow filesystem
  opens rather than path-based ones.
- **A differential lane against namespace2xml 2.4.0** (Appendix C.6). Every fixture carries a
  measured verdict and prose saying *why* it diverges, and `docs/migration-2.x-to-3.0.md` is
  generated from those notes rather than written.
- **Format guides** for all five formats, and `docs/usage-methodology.md`, which now carries a
  worked cross-format specialization pipeline and the fixture-pinning discipline.
- **A tool-install gate** that installs the packaged tool on all three platforms in CI, so a package
  that cannot be installed fails before a tag rather than after one.
- Change-protocol rule **C7: evidence must be able to fail**. A gate nobody has watched go red is
  not a gate.
- `KNOWN-LIMITS.md` §1.1–§1.20 and §2.1: every limit that owes a resolution now names the issue that
  owns it, so the file states current behaviour and the tracker holds the argument.

### Fixed

- `--max-depth` beyond what this build can walk is now `CLI001` rather than a stack overflow the
  runtime gives no opportunity to report.
- The unspellable-path diagnostic fallback is injective; two distinct paths no longer print alike.
- `WARN009` no longer fires for a `filename` bound to a wildcard output match, where the filename
  was also being ignored. **Caused by
  [#50](https://github.com/stop-cran/namespace2xml/issues/50)**.
- Exact scheme directives now bind to the instance a wildcard literalizes.
- `merge=append` no longer silently accepts a non-sequence accumulator.
- The output root is derived *after* step 16, not before it.
- YAML positions are reported on §22 lines rather than YAML 1.1 lines.
- Sixteen input-reader and YAML-writer defects found by dual-model independent review across M2 and
  M5 — seven in the readers, nine in the YAML writer over two hardening passes.
- The cross-OS determinism gate hashes dotfile outputs, which it had been silently under-reporting.

### Changed

- A destination diagnostic is numbered by the §21.3 order, and a refused transform or destination
  fold is reported once per output instance rather than once per contribution.
- An acceptance item that no fixture can express is now discharged by a **named gate** instead of
  being left uncovered; 85 of 86 items are covered, and the manifest says where the last one is.
- **Release notes are the released version's own section**, prefixed with its contract revision and
  a tag-pinned specification link. The workflow passed the entire changelog, which buried each
  version under every earlier one and would have got worse with every release. The step runs before
  the nuget.org push and fails if the section is missing, so a changelog omission stops the release
  instead of leaving a published package with nothing to explain it.

### Removed

- `KNOWN-LIMITS.md` entries that had become false, including a claim that XML output had not landed,
  a `multiline` directive that does not exist, and a `substitute` described as "parsed and not
  applied" when it is refused with exit `70`. A limits list that overstates the gaps is as
  misleading as one that hides them.

## [3.0.0-preview.1] - 2026-08-06

### Contract

- `contract-bundle` `r30+35e144372ca0`, covering `docs/specification.md` and
  `spec/diagnostics.registry.json`.
- The specification is now committed to the repository at `docs/specification.md`, is shipped inside
  the NuGet package, and is the hashed root of the contract bundle. Previously it lived outside the
  repository, which made "which contract does this binary implement" unanswerable.
- Added §6.4, the structured diagnostics contract: the `--diagnostics-format` option, the argument
  pre-scan that resolves the encoding before validation can fail, the text encoding, and the
  canonical JSON encoding with a closed schema and a fixed byte layout.
- Rewrote §11.1 to state which bound governs each XML aspect, added `--max-xml-attributes`, and
  closed the entity-expansion question: because document type definitions are prohibited, decoded
  length can never exceed encoded length, so an implementation must not impose an expansion budget.
- Extended §22 so that a diagnostic carries its phase and the specification anchor it enforces, and
  so that the specification and the registry form one versioned contract bundle that `--version`
  reports.
- Added acceptance items 81–85 covering the above.

### Added

- Complete rewrite of the implementation for 3.0. The 2.x code is removed rather than migrated.
- `--diagnostics-format json`: the whole diagnostic stream on standard error as one canonical JSON
  array, written once at exit, with operational messages suppressed.
- `--version` now prints machine-readable `<field>: <value>` lines including the `contract-bundle`
  revision, the specification digest and the registry digest.
- Portable conformance corpus under `conformance/`, with a harness whose own self-tests include
  cases that must fail, so a comparer that never fails cannot ship unnoticed.
- Contract drift gates: the diagnostics registry, the diagnostic stream schema and the acceptance
  manifest are all generated from the specification, and CI fails if the committed artifacts do not
  match a regeneration.
- Determinism is measured rather than asserted: the corpus is hashed on Linux, Windows and macOS and
  the hashes must be identical.
- `AGENTS.md`, `llms.txt`, issue forms and the report protocol in `CONTRIBUTING.md`, making the tool
  discoverable and arguable-with by automated agents.
- Symbol packages, SourceLink and build provenance attestation on every release.

### Changed

- Target framework is now `net10.0`.
- **Releases are published from tags only.** The 2.x workflow published to nuget.org on every push to
  `master`, which meant any commit reaching the default branch was published under the trusted
  package name without a separate reviewed act.
- Publication uses a credential that expires — nuget.org trusted publishing, exchanging the run's
  OIDC identity for a short-lived key — rather than a stored API key.
- Direct dependencies reduced from twelve to four.

### Removed

- The 2.x implementation in its entirety.
- The `.NET 9` workflow.

---

## Contract revision log

| Revision | Date | Change | Caused by |
|---|---|---|---|
| `r1` | 2026-08 | Initial bundle: specification plus generated diagnostics registry. | — |
| `r2` | 2026-08 | Appendix C run-B token-vector rules; `expected-diagnostics.json` absence means no stream. | Fixture authoring for the informational modes, which the earlier text could not express. |
| `r3`–`r30` | 2026-08 | Not logged individually. These revisions were taken during the pre-preview specification work, before the first tag; each is recoverable from the history of `spec/contract-bundle.json`. Per-revision logging resumes below. | — |
| `r31`–`r37` | 2026-08 | §11.4 alias definition and `Q{}local-name`; §12.2 repeated-capture `WILDCARD001`; §17.1 patch semantics; normative `rule` member; §22 members listed per condition; §11.4 content-token placement question; Appendix C.6 legacy differential lane. | [#43](https://github.com/stop-cran/namespace2xml/issues/43), [#44](https://github.com/stop-cran/namespace2xml/issues/44), [#45](https://github.com/stop-cran/namespace2xml/issues/45), [#46](https://github.com/stop-cran/namespace2xml/issues/46), [#47](https://github.com/stop-cran/namespace2xml/issues/47). |

## `CONTRIBUTING.md` revision log

| Revision | Date | Added | Removed | Caused by |
|---|---|---|---|---|
| 1 | 2026-08 | Initial: ownership chain, rules C1–C6, four-route feedback protocol, two worked examples. | — | — |
| 2 | 2026-08 | Rule C7, evidence must be able to fail; §7 judgment list for specializing a foreign document and pinning depended-on behaviour. | — | Gate verification work that found three kinds of false survivor in the mutation harness. |

## 2.x

See the [2.x releases](https://github.com/stop-cran/namespace2xml/releases?q=v2). That line is
superseded and receives no further changes.
