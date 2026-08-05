# Copilot instructions

Operational notes for automated agents working in this repository.

`AGENTS.md` explains *what this project is* and the one rule that governs it: the specification is
the contract, and the code is an attempt to satisfy it. Read that first. This file is the mechanical
complement — the environment facts, the verification loop, and the traps.

Every trap below was hit during development and verified. Several fail in ways that point at the
wrong file, which is why they are written down.

---

## Environment

- **.NET 10** (`net10.0`). The solution file is **`namespace2xml.slnx`**, not `.sln`.
- `TreatWarningsAsErrors` is on, with `AnalysisLevel` `latest-recommended`. **An analyzer suggestion
  is a build failure.** This is deliberate.
- `Nullable` and `ImplicitUsings` are enabled; `InvariantGlobalization` is `true` and will not become
  configurable — it is what makes byte-identical output achievable.
- PowerShell scripts run with `pwsh -NoProfile`.

## The verification loop

Run this before pushing. It is the same order CI uses, cheapest first:

```powershell
dotnet build namespace2xml.slnx -c Release
dotnet test  namespace2xml.slnx --no-build -c Release
dotnet format namespace2xml.slnx --verify-no-changes --severity error
pwsh -NoProfile -File tools/hash-corpus-outputs.ps1 -Output corpus-hashes.txt
```
CI additionally runs `actionlint` over `.github/workflows/`, and a gate asserting that no path under
`conformance`, `spec`, `tools` or `spikes` is gitignored and that no conformance fixture carries a
CR byte.

## Generated files — never hand-edit

| File | Generator |
|---|---|
| `spec/diagnostics.registry.json` | `tools/sync-diagnostics-registry.ps1` |
| `spec/diagnostic-stream.schema.json` | `tools/sync-diagnostics-registry.ps1` |
| `src/Namespace2Xml.Core/Diagnostics/DiagnosticCodes.g.cs` | `tools/sync-diagnostic-codes.ps1` |
| `spec/contract-bundle.json` | `tools/sync-contract-bundle.ps1` |
| `conformance/assertions.json` | `tools/sync-assertion-manifest.ps1` |
| `docs/diagnostics.md` | `tools/sync-docs.ps1` |
| `docs/migration-2.x-to-3.0.md` | `tools/sync-docs.ps1` |

Run all five generators after touching `docs/specification.md` **or** the corpus, in the order listed
in `AGENTS.md`. The `contract-gates` job regenerates them and fails on any difference.

Generators are idempotent, and **idempotent is not correct**. A backtick bug once emitted a raw
PowerShell hashtable into line 5 of both generated documents, stably, on every run. Read the output
of a generator you changed; do not merely re-run it and observe that nothing moved.

---

## Traps

### An XML comment may not contain `--`

Writing `--version` inside a comment in `Directory.Build.props` is invalid XML. MSBuild recovers by
producing an **empty `TargetFramework`**, and the failure surfaces as:

```
NuGet.targets(196,5): error : Invalid framework identifier ''
```

…which names a file you did not touch. If a build breaks with a bizarre restore or framework error
right after you edited a `.props`, `.csproj` or `.targets` comment, check for a double hyphen.
Write `-` `-version` as "the version option" in prose instead.

### GitHub Actions evaluates `${{ }}` inside `run:` blocks, including on shell comment lines

A shell `#` comment does not hide an expression from Actions. An empty `${{ }}` is a parse error, and
the result is a **zero-second run failure with no job and no log**, attached to whatever event
arrived — including an event the workflow's own triggers should have excluded, because Actions could
not read the triggers either.

`tools/check-publication-triggers.py` cannot catch this: the file is valid YAML, and the defect lives
only in the expression grammar layered on top. `actionlint` catches it with a precise location and
runs in the `lint` job. Run it locally when editing workflows:

```powershell
curl.exe -sSLo actionlint.zip https://github.com/rhysd/actionlint/releases/download/v1.7.7/actionlint_1.7.7_windows_amd64.zip
Expand-Archive actionlint.zip -DestinationPath . -Force
.\actionlint.exe -no-color -oneline
```

`actionlint` also runs **shellcheck** over every `run:` block, and shellcheck `info` findings fail
the job. Unquoted variable expansion is the one you will hit: a shared path list must be a bash
array expanded as `"${paths[@]}"`, not a space-separated string expanded bare (SC2086).

### PowerShell backticks in double-quoted strings

A single backtick before `$(` escapes the subexpression, so `` `$(...) `` emits the literal text plus
a stringified object. To produce a **literal markdown backtick**, use a double backtick. This is the
bug that put a hashtable in the generated docs.

### Line endings are not normalized in three trees

`.gitattributes` marks `conformance/**`, `tests/**/fixtures/**` and `spikes/**` as `-text`. Git will
**not** fix line endings there, by design — these are byte-compared data. Author those files with
explicit LF:

```powershell
[IO.File]::WriteAllText($path, ($text -replace "`r`n","`n"), (New-Object Text.UTF8Encoding($false)))
```

Editor and tool writes on Windows may produce CRLF. Everywhere else, `* text=auto eol=lf` applies and
`dotnet format` enforces it.

`spikes/** -text` exists because normalization once stripped every CR byte from `25-crlf.yaml`, the
fixture whose entire purpose was CRLF — silently voiding the evidence a shipped document cited.
Evidence the toolchain rewrites is not evidence.

### `.gitignore` negations are load-bearing

The file inherits a Visual Studio template that ignores `bin/`, `[Rr]elease/`, `*.log`, `*.cache` and
`**/packages/*`. For a configuration transformer those are **ordinary fixture names**. The
`!/conformance/**`, `!/spikes/**` and `!/tests/**/fixtures/**` negations at the end re-include them.

Remove them and a contributor's expected output vanishes from a commit, leaving a corpus that passes
locally while asserting less in CI. The `lint` job fails if anything under those trees is ignored.

Running that check locally reports every `spikes/*/bin` and `spikes/*/obj` file if you have ever
built a spike. CI never sees them because it runs on a fresh checkout and no job builds the spikes.
Ignore that noise locally; only paths that would have been committed matter.

### `--version` must not carry a commit suffix

`IncludeSourceRevisionInInformationalVersion` is `false`. The SDK otherwise appends `+<sha>`, which
changes the reported contract identity on every commit and makes the release workflow's anchored
`grep -q "^version: ${version}$"` unsatisfiable. Provenance comes from SourceLink and the build
attestation instead.

### `<auto-generated>` silently disables the nullable context

A file whose header comment is `// <auto-generated>` is exempted from the project's `Nullable`
setting, so every `string?` in it becomes **CS8669** — an error here. The compiler names the remedy
badly ("requires an explicit `#nullable` directive"), and the header is exactly what you want on a
generated file for the analyzer exemptions it brings. Emit `#nullable enable` immediately after the
header; `tools/sync-diagnostic-codes.ps1` does.

Related: **CS1573** fires when a generated method gains a parameter without a matching `<param>` tag.
That is a useful accident — it caught a hand-injected defect during gate verification — but do not
rely on it, because it only fires when the doc comment and the signature disagree.

### C# specifics that bite under `TreatWarningsAsErrors`

- `String.EndsWith(char, StringComparison)` **does not exist**. Use `EndsWith(char)` or the `string`
  overload.
- **CS1631**: you cannot `yield` inside a `catch`. Extract a `TryX(out failure)` helper.
- **CA1859**: for private members, return and accept the concrete type (`Dictionary<,>`), not the
  interface (`IReadOnlyDictionary<,>`).
- **IDE0011**: `.editorconfig` sets `csharp_prefer_braces = true:error`. **Every** `if`, `else`,
  `while` and `for` needs braces, including single-statement bodies and one-line early returns. A
  parser written in the compact style produces twenty build errors from one file.
- **CA1720** rejects `Integer`, `Decimal`, `Float`, `Object` and friends as member names, *even when
  they are normative vocabulary from the specification*. `CanonicalScalarText.Integer(…)` is a build
  failure. Name the operation instead of the type — `value.ToCanonicalText()` — which also reads
  better at the call site.
- A conditional whose branches are a `ReadOnlySpan<char>` and a string literal does not compile.
  Write `.AsSpan()` on the literal branch too.

### Test-authoring specifics

- **CA1707 makes underscores in test method names a build error.** The `Given_When_Then` style
  every other .NET repository uses does not compile here. Write `AShortOptionHasNoInlineForm`, and
  put the sentence in a `<summary>` where it belongs. Thirty-odd errors from one new file, all
  identical, is this rule.
- **Shouldly's string `ShouldContain` / `ShouldNotContain` are case-*insensitive* by default.**
  `text.ShouldNotContain("E")` fails against `"1.0e21"`. Pass `Case.Sensitive` whenever the assertion
  is about letter case — which, for a specification that fixes a lowercase `e`, is exactly when you
  are reaching for it.
- `InvariantGlobalization` is `true`, so `new CultureInfo("de-DE")` **throws**
  `CultureNotFoundException` rather than giving you a hostile culture. To prove a conversion ignores
  the ambient culture, clone `CultureInfo.InvariantCulture` and mutate its `NumberFormat` — setting
  `NegativeSign = "MINUS"` makes a missing `InvariantCulture` argument visible immediately.
- Record equality is **not** structural when a member is `ImmutableArray<T>`: it compares the
  underlying array by reference, so two identically-populated results compare unequal. Compare a
  projection, not the record.

### Restoring a mutated file does not rebuild it

Proving a gate red means mutating a file and putting it back. `Copy-Item` **preserves the source
file's `LastWriteTime`**, so the restored file looks older than the DLL built from the mutant and
MSBuild skips recompiling it. The tests then still fail, against source that is already correct,
and the obvious conclusion — that the restore did not work — is wrong.

Touch the file after restoring:

```powershell
(Get-Item $path).LastWriteTime = Get-Date
```

### `expected-diagnostics.json` is compared as literal text, not as decoded JSON

`DiagnosticComparer` deliberately reimplements the canonical layout rules rather than delegating to
a JSON reader, so a `\u00a7` escape is compared against the emitted `§` and fails. Write the literal
character. This is the comparer being an independent oracle rather than a mirror, which is the whole
reason it exists — but it does mean a fixture cannot be authored in ASCII-escaped JSON.

---

## Rules that are easy to violate quietly

These are the ones a reviewer will reject, and the ones most likely to look reasonable while you are
writing them.

**Never capture expected fixture output from the tool.** Not once, not "just to get started". A test
that records the implementation's own opinion validates nothing, and the ability to tell correct from
customary is this project's main asset. Author expected output from `docs/specification.md`.

**The conformance comparer must not delegate to the production writer.** `DiagnosticComparer`
deliberately reimplements the canonical JSON layout rules rather than calling the code that emits
them. An oracle that asks the implementation what "correct" means cannot detect a wrong
implementation. If you find yourself sharing a helper between `src/` and the comparer, stop.

**A new gate needs a proof that it fails.** Before trusting one, reintroduce the defect it targets
and watch it go red, then restore. `HarnessSelfTests` exists entirely for this: it is a suite of
must-fail cases proving the comparer rejects what it claims to reject. Add to it when you add a rule.

**Ask what would notice if this were wrong.** A dual-model review found a defect on the default path
of every invocation that three separate mechanisms should each have caught, and all three failed
silently: no fixture reached that path, the comparer did not check layout, and the determinism script
discarded the stream being corrupted. The components were individually sound. The seam was not.

---

## Reporting

If the tool surprises you, file it — routing and forms are in `CONTRIBUTING.md`, and
`KNOWN-LIMITS.md` lists what is deliberately not covered yet. Check it before reporting a gap.

Include the `contract-bundle` revision from `--version` in every report; a report against an unknown
contract revision cannot be acted on.

Mark every claim `verified-in-session` or `proposed-but-untested`. Do not present reasoning as
observation. Draft the report, show it to your human, and let them approve it before filing.
