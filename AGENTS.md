# AGENTS.md

Entry point for automated agents working in this repository.

## What this repository is

`namespace2xml` is a deterministic configuration transformer. It reads ordered namespace profiles
and structured inputs, applies scheme directives, and renders many outputs from one overlaid model.
Identical inputs always produce byte-identical outputs on every supported platform.

**Version 3.0 is a complete rewrite.** The 2.x implementation is not a reference for anything.

## The one thing you must understand before changing code

**This project is specified before it is implemented, and the specification wins.**

`docs/specification.md` is the contract. It is not documentation of what the code does; the code is
an attempt to satisfy it. When code and specification disagree, the code is wrong until a reviewed
amendment says otherwise. "The implementation has always done it this way" is not an argument here.

Concretely, this means:

- Do not read the source to find out what the correct behaviour is. Read `docs/specification.md`.
- Do not change observable behaviour without a conformance fixture that fails before and passes
  after, authored from the specification.
- **Never** create a fixture's expected output by capturing what the tool currently prints. A test
  that records the implementation's own opinion validates nothing. This is the single easiest rule
  to violate quietly and the most damaging.

## Read order

| Order | File | Why |
|---|---|---|
| 1 | `CONTRIBUTING.md` | The binding change protocol and the report forms. Start here for any change. |
| 2 | `docs/specification.md` | The contract. Long. Read the sections you touch, in full. |
| 3 | `docs/diagnostics.md` | Every diagnostic code, its meaning, and its specification anchor. |
| 4 | `conformance/` | The independent oracle. Fixture layout is specification Appendix C. |
| 5 | `docs/usage-methodology.md` | How the tool is meant to be used, and when not to use it. |
| 6 | `KNOWN-LIMITS.md` | What is deliberately not covered yet. Check before reporting a gap. |

## Repository map

| Path | Role |
|---|---|
| `docs/specification.md` | **The contract.** Normative. Hashed into the contract bundle. |
| `spec/diagnostics.registry.json` | Canonical code-level diagnostic facts. Generated; do not hand-edit. |
| `spec/diagnostic-stream.schema.json` | JSON Schema for `--diagnostics-format json`. Extracted from the specification; do not hand-edit. |
| `spec/contract-bundle.json` | The revision `--version` reports, covering both files above. |
| `conformance/` | Portable conformance corpus. The oracle. |
| `conformance/assertions.json` | Every acceptance requirement, its status, and its fixture coverage. |
| `src/Namespace2Xml.Core/` | The library. |
| `src/Namespace2Xml.Cli/` | The `namespace2xml` dotnet tool. |
| `tests/Namespace2Xml.UnitTests/` | Unit tests, including the contract drift gates. |
| `tests/Namespace2Xml.Conformance/` | The corpus harness and its self-tests. |
| `tools/` | Generators for the derived contract artifacts. Run them; do not bypass them. |
| `spikes/` | Time-boxed investigations. Not shipped, not built by the solution. |

## Build and test

```
dotnet build namespace2xml.slnx
dotnet test  namespace2xml.slnx
```

The solution uses the `.slnx` format and targets `net10.0`. `Directory.Build.props` sets
`TreatWarningsAsErrors`; a warning is a build failure, deliberately.

After changing `docs/specification.md`, regenerate the derived artifacts or CI will reject the
change:

```
pwsh -NoProfile -File tools/sync-diagnostics-registry.ps1
pwsh -NoProfile -File tools/sync-contract-bundle.ps1
pwsh -NoProfile -File tools/sync-assertion-manifest.ps1
```

## Machine-readable output

Run the tool with `--diagnostics-format json` to receive the entire diagnostic stream on standard
error as one canonical JSON array conforming to `spec/diagnostic-stream.schema.json`. In that mode
operational log messages are suppressed entirely, so standard error is pure data. Every diagnostic
carries a stable `code`, a `phase`, and a `spec` anchor naming the clause it enforces.

`--version` prints one `<field>: <value>` line per field, including `contract-bundle`. Include that
revision in every report; a report against an unknown contract revision cannot be acted on.

## Reporting what you find

If the tool surprises you, that is a finding worth filing, and this project wants it. The routing
rule and the report form are in `CONTRIBUTING.md`. In short, ask: *what would have to change so this
never surprises anyone again?*

- The code should have matched the specification → **bug**
- The specification does not say, says two things, or says something surprising → **specification ambiguity**
- Both are right and you could not find out how to do the thing → **usage gap**, often the most valuable report
- The tool cannot express this at all → **feature request**

Draft the report, show it to your human, and let them approve it before filing. Do not file
automatically. Mark every claim `verified-in-session` or `proposed-but-untested`; do not present
reasoning as observation.
