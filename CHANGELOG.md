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

### Contract

- `contract-bundle` `r2+0654ebfa8b7a`, covering `docs/specification.md` and
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

## `CONTRIBUTING.md` revision log

| Revision | Date | Added | Removed | Caused by |
|---|---|---|---|---|
| 1 | 2026-08 | Initial: ownership chain, rules C1–C6, four-route feedback protocol, two worked examples. | — | — |

## 2.x

See the [2.x releases](https://github.com/stop-cran/namespace2xml/releases?q=v2). That line is
superseded and receives no further changes.
