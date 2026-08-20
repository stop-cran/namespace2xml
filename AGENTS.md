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
- Do not trust a test you have not watched fail. Mutate what it guards, see it go red, restore.
  This is `CONTRIBUTING.md` C7, and `.github/copilot-instructions.md` carries the harness shape.
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
| `ansible/` | The `stop_cran.namespace2xml` Ansible collection. Python, released separately under `ansible-v*` tags. Not part of the .NET solution. |
| `spikes/` | Time-boxed investigations. Not shipped, not built by the solution. |

## Build and test

```
dotnet build namespace2xml.slnx
dotnet test  namespace2xml.slnx
```

The solution uses the `.slnx` format and targets `net10.0`. `Directory.Build.props` sets
`TreatWarningsAsErrors`; a warning is a build failure, deliberately.

After changing `docs/specification.md`, regenerate the derived artifacts or CI will reject the
change. Run all five, in this order — the codes come from the registry, the bundle hashes the
registry, and the docs read the bundle:

```
pwsh -NoProfile -File tools/sync-diagnostics-registry.ps1
pwsh -NoProfile -File tools/sync-diagnostic-codes.ps1
pwsh -NoProfile -File tools/sync-contract-bundle.ps1
pwsh -NoProfile -File tools/sync-assertion-manifest.ps1
pwsh -NoProfile -File tools/sync-docs.ps1
```

Adding or changing a conformance fixture also requires `sync-assertion-manifest.ps1` and
`sync-docs.ps1`, because coverage and the migration notes are both derived from the corpus.

An amendment also strands every copy of the amended sentence elsewhere in the repository, so run
`tools/check-specification-quotations.ps1` as well. Quote the contract in a blockquote and that gate
covers you.

An amendment moves the contract revision, and `KNOWN-LIMITS.md` names the revision it describes, so
run `tools/check-known-limits-issues.ps1` too. It fails on the stale header alone, and its message
is also the prompt to ask whether the amendment owes a `*(resolved)*` entry — a reader running the
last published preview has the old behaviour and nothing else tells them so.

See `.github/copilot-instructions.md` for the mechanical traps in this repository — several of them
fail in ways that point at the wrong file.

### The Ansible collection

`ansible/` is a separate artefact with its own toolchain. `dotnet test` does not touch it and it
does not build with the solution.

```
python -m pytest ansible/tests/unit -q
```

The `ansible` workflow additionally runs `ansible-test sanity`, builds the Galaxy tarball and
asserts its contents, runs the unit tests under four ansible-core versions, and runs two
integration targets against a locally packed build of the tool. None of that can run on Windows:
`ansible-doc`, `ansible-test` and `ansible-playbook` all call `os.get_blocking`, which fails on a
Windows handle. The unit tests are the whole of what you can check locally.

The collection ships two plugins that share the name `render` and almost nothing else. The filter
in `ansible/plugins/filter/render.py` flattens play variables into profile text and renders on the
controller. The module in `ansible/plugins/modules/render.py` runs on a managed node over files
that are already there, and writes rendered documents to a directory on that node; its logic lives
in `ansible/plugins/module_utils/`, because `AnsibleModule` itself cannot be imported on Windows
and the testable part must be.

Each plugin's argument reference is its own `DOCUMENTATION` block; that is what `ansible-doc`
prints, and it is checked by `ansible-test sanity`. Change the arguments and change that block in
the same commit.

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

Set the `Component` field so the report reaches the right surface: the CLI, or the Ansible
collection. If a playbook was in the loop it is the collection, even when the message came from the
tool underneath — the plugin owns how the tool is invoked, and that is where a fix would land.

Draft the report, show it to your human, and let them approve it before filing. Do not file
automatically. Mark every claim `verified-in-session` or `proposed-but-untested`; do not present
reasoning as observation.
