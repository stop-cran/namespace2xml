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

## Proving a test can fail (CONTRIBUTING C7)

The loop above only shows that everything is green. Green is not evidence: a test nobody has watched
fail is a claim, not a check. Before a new test or gate is trusted, mutate the source it guards and
watch it go red.

The harness shape that works here — write it to a `TEMP-mutate-<topic>.ps1`, delete it before
committing:

```powershell
$orig = [IO.File]::ReadAllText($file)          # absolute path; see the CWD trap below
try {
    [IO.File]::WriteAllText($file, $orig.Replace($from, $to), (New-Object Text.UTF8Encoding($false)))
    dotnet build <project> -c Release --nologo -v q     # a build failure is NOT a kill
    dotnet test  <project> --no-build -c Release --nologo
}
finally {
    [IO.File]::WriteAllText($file, $orig, (New-Object Text.UTF8Encoding($false)))
    (Get-Item $file).LastWriteTime = Get-Date   # or MSBuild will not recompile it
}
```

Four things decide whether a run means anything, and each has its own trap section below:

1. **The mutation must compile.** `if (false)` is CS0162, which is an error here, and a harness that
   treats a failed build as a kill will report a confident green for a mutation nobody ran. Flip a
   value the guard *reads*, not the guard itself.
2. **The tests must actually run.** Check the reported test count against the count you expect; a
   `--filter` that matches fewer types than you assumed produces silent mass survival.
3. **A survivor is a hypothesis, not a verdict.** Read the mutant. Inert mutants, unreachable arms
   and a second defence standing in front of the one you mutated all survive legitimately.
4. **Rebuild afterwards.** The `finally` restores the source, not the binaries.

Aim mutations at the *decision* the test claims to pin. A mutation that produces a wrong result is
worth more than one that changes control flow, which tends to hang rather than report.

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

### PowerShell `-notlike` and `-replace` misfire on fixture text

`-like` / `-notlike` treat `[` and `]` as a character-class, so filtering output that contains
`Root = []` **throws** rather than matching. `-replace` takes a regex and, worse, silently matches
more than you meant: replacing `DEST` in a fixture also rewrote the `DEST` inside the literal
`"destination"`. Use `.Contains(…)` and `.Replace(…)` — the ordinal string methods — for anything
that is literal text, which in this repository is almost everything.

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
- **The test projects have no global usings.** Every new test file needs `using NUnit.Framework;`
  and `using Shouldly;` explicitly, or you get a wall of CS0246 naming types that plainly exist.
- **CS1718** — comparing a variable to itself is an error, so a reflexivity test must compare two
  identically-valued variables rather than `x.CompareTo(x)`.
- **CS1573** — under `GenerateDocumentationFile`, once one parameter of a member is documented every
  parameter must be, including on a **private** constructor.
- **CS8604** — Shouldly's `ShouldContain(string, string)` rejects a `string?`. Chain
  `.ShouldNotBeNull().ShouldContain(…)`.
- **CA1000** forbids static members on a generic type, so `StepOutcome<T>.Produced` does not
  compile. Put the factories on a non-generic companion class with a generic method.
- **A `[TestCase]` string argument cannot carry a lone surrogate.** Attribute arguments are stored
  in metadata as UTF-8, which has no encoding for one, so the compiler substitutes **U+FFFD** and
  `[TestCase("u\ud800", "u\udc00")]` arrives as two *equal* strings. Verified by reflecting over a
  custom attribute: the literals read `D800`/`DC00`, the attribute reads `FFFD`/`FFFD`. This is
  worse than a compile error, because a test asserting that two unspellable names **collapse** would
  pass for entirely the wrong reason. Build such strings in the test body, where the literals
  survive.

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

### A mutation that survives is not always a test gap

Three kinds of false survivor have already cost time here, and all three look identical in the
harness output — a green run against mutated source:

- **The mutant is semantically inert.** `new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)`
  does not make `GetBytes` emit a preamble, and removing a `ReferenceEquals(x, y)` fast path from a
  comparer that would return `0` anyway changes nothing. Read the mutant before writing a test for
  it; a test that pins inert behaviour is worse than no test.
- **The tests never ran.** `--filter FullyQualifiedName~Pipeline` matches `PipelineStepTests` and
  `PipelineRunTests` but **not** `DiagnosticBufferTests` in the same file. Eleven mutations
  "survived" that were never exercised. The filter matches the fully qualified *type and method*
  name, not the file. **Check the test count in the harness output against the count you expect**
  before believing a survivor.
- **The fixture is invariant under the mutation.** `new string('z', N)` is unchanged by reversing
  segment order, so a payload-comparison assertion that is entirely real proves nothing about
  ordering. Use position-varying data. A boundary is the special case: a mutation moving a clamp
  from the end of the *line* to the end of the *text* survived every position test, because the
  fixtures' later lines were plain ASCII and converting across them changed nothing. Put the
  distinguishing data **past** the boundary the mutation moves.
- **A second defence is standing.** `XmlInputReader` refuses a DTD twice: `Prolog.FindDoctype`
  rejects the text before the parser is constructed, and `XmlReaderSettings.DtdProcessing` refuses
  it again. Weakening the settings alone — to `Parse` with an `XmlUrlResolver` — **survives every
  fixture**, because the pre-scan means the parser never sees a DTD document. That is the design
  working, not a gap. Removing the pre-scan alone is killed, and removing both retrieves a real
  external subset. When a security posture survives, mutate the layer that actually runs first, and
  check whether the surviving setting is reachable by any input at all before writing a test for it.

### A mutation that does not compile has proved nothing

Under `TreatWarningsAsErrors`, deleting the only use of a parameter or field makes the build fail,
and a harness that treats a failed build as a kill will report a confident green for a mutation that
never ran. Two "KILLED (compile)" lines here were a reader ceasing to call a converter — exactly the
defect under test, invisible.

The escape is to mutate the **shared helper to the identity** instead of unhooking each caller.
Turning `SourceLines.ColumnOf` into a pass-through kept every reference alive, compiled cleanly, and
killed eleven tests across three fixtures at once — which is also stronger evidence, because it
proves every caller genuinely routes through the helper rather than proving one call site at a time.

The same applies to the most natural way to disable a guard. Rewriting `if (!approximate)` as
`if (true)` makes the code after it unreachable, and **CS0162** is an error here, so the harness
reports `KILLED (build)` for a mutation no test ever saw. Flip a value the guard reads instead —
passing `approximate: false` at the one call site reverted the same behaviour, compiled, and killed
thirteen tests.

### `create` writes CRLF on Windows, and a mutation harness will not tell you

A new `.cs` file authored through a tool lands with CRLF. `* text=auto eol=lf` normalizes it on
commit, so it is invisible in review and in CI — but a mutation harness matching LF-joined text
against the working tree reports `matched 0 times` and prints `SKIP`, which reads like a stale
pattern rather than a line-ending mismatch. Normalize new files as they are created:

```powershell
$t = [IO.File]::ReadAllText($p)
[IO.File]::WriteAllText($p, ($t -replace "`r`n","`n"), (New-Object Text.UTF8Encoding($false)))
```

### Host parsers throw types their own documentation does not lead you to

- `Utf8JsonReader.GetString()` raises **`InvalidOperationException`**, not `JsonException`, for an
  unpaired `\uXXXX` surrogate. A `catch (JsonException)` around the whole read misses it and the
  process dies with exit `-532462766`. Wrap every `GetString()` call site, not the reader loop.
- `Utf8JsonReader` counts lines by **LF alone**; Section 22 counts LF, CRLF and a lone CR. Two line
  tables are needed — one to index the host's line number, one to report ours.
- **YamlDotNet's `new Parser(TextReader)` sets `skipComments: true`.** A `case Comment:` in the
  event switch is therefore unreachable dead code that looks entirely correct. Build the scanner
  explicitly: `new Parser(new Scanner(reader, skipComments: false))`.
- `XmlReader` with `DtdProcessing.Prohibit` answers **any** `<!…>` markup declaration — including
  `<!doctype a>` and `<!FOO a>`, neither of which is a DTD — with "For security reasons DTD is
  prohibited", plus advice to set `XmlReaderSettings.DtdProcessing`. Host advice naming an API the
  user cannot reach is stripped in `Explain`; check for a new one whenever a host version changes.

### A host's *line* number can be wrong, not just its column

Converting a host column into Section 22 scalar columns is only half the problem, and fixing only
that half hides the other. **YamlDotNet's scanner is YAML 1.1, in which U+0085, U+2028 and U+2029
are line breaks.** Section 22 says a line ends "by LF, CRLF, or a lone CR, and by nothing else", so
one of those characters anywhere in a document makes every later `Mark.Line` too large — and a
column converted against that wrong line is a column of some other line entirely, so the fix that
addressed only columns made the failure *worse* on exactly these documents.

`Mark.Index` is the way out: it is a raw UTF-16 offset into the decoded text, verified to survive
CRLF, a lone CR and supplementary scalars without normalization, so `SourceLines.PositionOf` can
rebuild both coordinates and the host's line numbering stops mattering. Prefer an offset to a
(line, column) pair from any host that offers one.

`XmlReader` was measured and is **not** affected — XML 1.0 normalizes only CR, LF and CRLF, so its
line numbers already match Section 22. Measure rather than assume: the two hosts differ, and the
`SourceLines` doc comment previously asserted they agreed.

A test for this needs a **control document** that spends a real LF where the other spends the
excluded character. Without one, the host reports the same line for both — right for the control,
wrong for the other — and a single-document test passes on the coincidence.

### The mutation harness leaves the binaries mutated

`mutate2.ps1` restores the *source* in its `finally`, but the last `dotnet test` it ran compiled the
mutant. A following `dotnet test --no-build` therefore runs the mutated assembly against correct
source, and reports failures that `git status` says cannot exist. **Rebuild after a mutation run.**

Two related hazards:

- The `finally` does **not** run when the process tree is killed. After stopping a hung mutation,
  verify the file by hand and kill stray `dotnet` processes with `Stop-Process -Id`.
- Prefer mutations that produce a wrong *result* over ones that change control flow into a loop.
  Changing `used == SegmentSize` to `used > SegmentSize` makes a fill loop's `take` permanently `0`,
  and the harness hangs instead of reporting.

### A here-string in a CRLF file carries CRLF

Multi-line mutation text authored as `@' … '@` in a `.ps1` saved with CRLF will not match an
LF-normalized source file, and the harness reports `MUTATION TEXT NOT FOUND` rather than a survivor.
Join the lines explicitly:

```powershell
$from = @('        if (State == Aborted)', '        {', '            return;', '        }') -join "`n"
```

### The gitignore gate is noisier locally than in CI

`git ls-files --others --ignored --exclude-standard -- conformance spec tools spikes` is clean on a
CI runner, which never builds `spikes/`, and lists a hundred `bin/` and `obj/` paths on a
workstation where a spike has been run once. Those are genuinely ignored and genuinely fine. The
gate is aimed at a *fixture* named `release/`, `packages/` or `x.log`; read its output with that in
mind rather than concluding the tree is broken. `git status --porcelain` on the trees you touched is
the check that answers the question you actually have.

### A `--version` apphost failure is usually an architecture mismatch

`0x800700C1` from `hostfxr.dll`, or exit `2147516546`, means the published RID does not match the
installed runtime. This workstation is **Windows on ARM**, so `-r win-x64` produces an apphost that
cannot load the arm64 host, and `-r win-arm64` is the local RID. The CI matrix is `win-x64` and is
correct for the runners. Any harness that runs the published binary must surface its standard error
rather than only its exit code, or this reads as an unexplained numeric failure.

### Sorting two elements asks the comparer once

A comparer with symmetric branches — `left is null` and `right is null`, or a length tiebreak — has
two paths through each pair, and a two-element ordering test executes exactly one of them. A
mutation to the other branch survives against a test that genuinely asserts the specified order.

Assert comparer properties directly, over a set with one member per distinguishing feature:
reflexivity, antisymmetry (`sign(compare(a,b)) == -sign(compare(b,a))` for every ordered pair), and
transitivity. That is three tests, and it closes every branch at once.

---

### A sorted-order test can pass with the sort removed

`ImmutableDictionary<long, T>` enumerates a handful of small keys in ascending order anyway. A test
that inserted ordering values `7, 2, 40` and expected `2, 7, 40` therefore **survived deleting the
`OrderBy` entirely** -- it was asserting a coincidence of the backing store, not the Section 5.4
rule. The same applies to any "is sorted" assertion over a small or clustered key set.

Spread the keys across the real range and insert them out of order, then re-run the mutation that
removes the sort and confirm it goes red. A sort assertion you have not seen fail is not evidence
that anything is sorted.

### A scalar cannot observe fold order

Section 4.4 settles a payload contest at a node by "the latest scalar/null contribution", which is a
**source position** (`NodeMarks.PayloadMark`) and not a fold position. Two colliding scalars therefore
resolve to the same value whichever way round §17.5 folds their destinations. A fixture that writes
`k=1` and `k=2` to one destination and expects `k=2` **survives deleting the match order from the
fold key**, and its first draft here did.

Assert **sequence allocation** instead. §17.5 rebases implicit items above the destination high-water
mark in fold order, so `zebra`→`w,x` before `alpha`→`y,z` produces `w,x,y,z` and the reversed fold
produces `y,z,w,x`. That is a real discriminator; a scalar is not.

### The fold-key sort is inert except under a multi-format wildcard

The §17.5 fold key is (declaration order, **format ordinal**, **match order**, selector bytes), but an
expansion *produces* contributions in the opposite nesting — declaration, then match, then format. The
sort therefore changes nothing unless **one declaration has ≥2 formats and ≥2 matches reaching one
destination**. Mutating `OrderBy(c => c.Key)` to arrival order survives the entire corpus except
`one-destination-folds-by-format-before-match-order`, which exists solely for this shape. §17.5 says
"Implementations must not group by format before folding" for the same reason.

### A mutation aimed at the wrong scope proves nothing

A fourth false survivor, alongside the three listed under "A mutation that survives is not always a
test gap": the mutation was real, but it could not reach the property under test. Replacing §17.5's
cross-format `return later` with a `MergeStrategyMap.Create([], Replace)` merge left the high-water
assertion green — not because the implementation is untested, but because a replace **at the root**
carries every child wholesale, so no child's mark is ever consulted. The mutation that does
discriminate carries the accumulated marks forward **per path**, which is the misreading of "a
destination accumulator absorbs the incoming high-water mark for a path" that the sentence exists to
forbid.

Before believing a survivor, check that the mutation operates at the same scope as the assertion.

### The scalar/container shape contest needs its own mark

Specification Section 4.4 step 1 asks for "the latest scalar/null contribution at the node". That is
not the position mark: an explicit mapping-presence contribution advances the position mark without
being a scalar contribution, so judging payload precedence by position makes a genuinely later
`a=2` lose to an earlier `a=1` when an `a={}` landed between them. `NodeMarks.PayloadMark` exists
for this and nothing else.

### A diagnostic's `path` is in the output instance's frame, so a conflict at the root has none

`FlatIdentity.PathText` returns the path *relative to the output root*, and Section 6.4.3 omits an
absent member rather than writing an empty string. A condition that fires at the output root
therefore emits a diagnostic with **no `path` member at all**, and Appendix C.4 compares members
exactly, so a fixture written to pin the path fails with "expected member 'path' is missing" while
looking entirely correct.

Put the condition a level below the output root. `conformance/namespace-shape-conflict-precedence`
declares `app.output=namespace` and conflicts at `app.seqwins` for exactly this reason; conflicting
at `app` itself would have made the case silent about the one member it exists to pin.

### A diagnostic code can be dead, and the generated factories hide it

`tools/sync-diagnostic-codes.ps1` emits one factory per registry code, so `DiagnosticCodes.Warn008`
existed, compiled, and was documented in `docs/diagnostics.md` while **nothing ever called it** —
the empty-output-plan warning was simply never implemented. Grepping for the code name finds the
generated factory and looks like coverage.

Grep for *callers*, not for the code: a single hit in `DiagnosticCodes.g.cs` and nowhere else means
the diagnostic does not exist. The same check reads as a one-line audit across the whole registry,
and is worth running whenever a milestone claims a diagnostic is covered.

```powershell
$files = Get-ChildItem src -Recurse -Filter *.cs |
    Where-Object { $_.Name -ne 'DiagnosticCodes.g.cs' -and $_.FullName -notmatch '\\(obj|bin)\\' }
$text = ($files | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n"
(Get-Content spec/diagnostics.registry.json -Raw | ConvertFrom-Json).codes.code | Where-Object {
    $text -notmatch ('DiagnosticCodes\.' + $_.Substring(0,1) + $_.Substring(1).ToLower() + '\b')
}
```

As of the M3 exit, ten codes are uncalled: `SCHEME002`, `REFERENCE002`–`REFERENCE005`, `XML002`,
`COLLISION001`, `WARN005`, `WARN007` and `WARN010`. Every one belongs to an area `KNOWN-LIMITS.md`
lists as not yet implemented, so that list is the expected baseline rather than a defect list. Check
new entries against it, and check `COLLISION001` and `WARN005` at M4 — `filemerge` collision folding
is claimed as implemented, so those two are the pair most likely to be genuinely missing next.

### A shape-mark test only bites when the new key is later than the mark it must tie with

`NodeMarks.ContainerIsMapping` resolves an equal mapping/sequence shape-mark to the mapping, so the
defect a one-sided refresh prevents is a *tie*, not an inequality. A fixture whose sequence
shape-mark is already later than the key being grafted therefore stays a sequence under both the
correct code and a mutant that refreshes both facets, and the mutation survives against a test that
is otherwise exactly right.

Build the node so the grafted key is **later** than both existing shape-marks — put the hand-built
nodes at source ordinal `0` and let the rule come from a real profile at ordinal `1`. Then the
mutant produces the tie, the tie resolves to the mapping, and the test goes red.

The same shape of error hides any "resolves to X on a tie" rule: if your fixture never reaches the
tie, you are testing the inequality branch twice.

### A diagnostic member whose spelling the specification does not fix cannot go in a fixture

Section 22 lists `declaration or wildcard rule` among the members a diagnostic carries, and
Section 6.4.3 fixes the member *order* and the JSON layout — but nothing fixes the **text** of the
`rule` member. Its spelling is therefore an implementation choice, and writing it into an
`expected-diagnostics.json` would be capturing the tool's own opinion, which is the one rule this
repository does not bend.

Assert such a member in a unit test with a `ShouldContain` over the part that *is* specified (the
rule's path), and leave it out of the corpus. If the member matters enough to pin, amend the
specification first. This is a live gap for `WILDCARD002`, whose only optional member is `rule`.
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

**An assertion must name something a fixture can see.** Before writing a line into
`conformance/assertions.json`, ask: *if this claim were false, which byte of which fixture would
change?* "No external resource is retrieved" survived in the manifest for months against a case
whose every input declared a DTD — refused before any identifier is looked at, so a retrieving
implementation would have passed it unchanged. Traceability was satisfied the whole time, because
C1–C6 ask whether evidence exists, never whether it could have come out differently. This is
CONTRIBUTING C7; it is the rule most easily satisfied on paper.

**When the specification is ambiguous and nothing observable depends on it yet, stop.** Document the
clause, both readings, and the cost of each in `KNOWN-LIMITS.md`, and file the ambiguity report.
Do not pick a reading and encode it in a fixture: the corpus is what the project uses to tell
correct from customary, and a guess pinned there is indistinguishable from a decision afterwards.
`Q{}local-name` (§11.4, KNOWN-LIMITS §1.6) is the worked example — a real defect, deliberately left
unfixed because both places it would be observable refuse with `NOTIMPL` in this preview.

---

## Reporting

If the tool surprises you, file it — routing and forms are in `CONTRIBUTING.md`, and
`KNOWN-LIMITS.md` lists what is deliberately not covered yet. Check it before reporting a gap.

Include the `contract-bundle` revision from `--version` in every report; a report against an unknown
contract revision cannot be acted on.

Mark every claim `verified-in-session` or `proposed-but-untested`. Do not present reasoning as
observation. Draft the report, show it to your human, and let them approve it before filing.
