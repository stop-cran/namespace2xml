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

**The loop above is necessary and not sufficient.** `cross-os-hash` compares digests from three
runners and has no local equivalent, so a change that behaves differently on Unix passes everything
here and fails there. After pushing:

```powershell
gh run list --branch v3 --limit 3
```

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
Expand-Archive actionlint.zip -DestinationPath .actionlint -Force
.actionlint\actionlint.exe -no-color -oneline
```

Extract to a subdirectory, not to `.`. The archive carries `README.md`, `LICENSE.txt`, `docs/` and
`man/` alongside the binary, so `-DestinationPath .` **overwrites this repository's own `README.md`
and `license.txt`** and scatters seven files into `docs/`. `git status` then shows a working tree
that looks like someone rewrote the project's front page, and on a case-insensitive filesystem
`license.txt` and `LICENSE.txt` are the same file, so the damage is not obvious from the name.
Recover with `git checkout -- README.md license.txt` and delete the rest by hand. `.actionlint` is
gitignored.

`actionlint` also runs **shellcheck** over every `run:` block, and shellcheck `info` findings fail
the job. Unquoted variable expansion is the one you will hit: a shared path list must be a bash
array expanded as `"${paths[@]}"`, not a space-separated string expanded bare (SC2086).

### PowerShell backticks in double-quoted strings

A single backtick before `$(` escapes the subexpression, so `` `$(...) `` emits the literal text plus
a stringified object. To produce a **literal markdown backtick**, use a double backtick. This is the
bug that put a hashtable in the generated docs.

### PowerShell argument-mode `+` splits a string into three array elements

Inside `@( … )` an element like `'{"spec":"' + $s + '17.5"}'` is parsed in *argument* mode, where
`+` is not the concatenation operator: the element becomes **three** elements. A subsequent
`-join "`n"` therefore writes a raw newline into the middle of what should have been one JSON
string, and the conformance harness reports `'0x0A' is invalid within a JSON string` at a byte
offset in the middle of a line you believe has no newline in it.

Build every composed string into its own `$variable` first, then put the variable in the array.

### A fixture is verified by its bytes, not by its rendering

`Get-Content -Raw` on a broken `expected-diagnostics.json` prints exactly what you intended, because
the terminal renders the stray newline as a line break in a file that has line breaks anyway. Use
`Format-Hex` when a fixture behaves as though it contains something you cannot see, and remember
that **every conformance fixture file needs a trailing LF**, including `requirements.txt` and
`expected-exit-code.txt`.

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

### PowerShell gotchas that produce confidently wrong audits

`$obj.item` on a `ConvertFrom-Json` array collides with the `IList.Item` indexer and silently
returns the method signature rather than the property. Use `[int]$_.item` inside a
`ForEach-Object`.

Worse, because it fails in the safe-looking direction: **`@($null).Count` is `1`, not `0`.**
Auditing the assertion manifest with

```powershell
if (@($it.fixtures).Count -eq 0 -and @($it.gates).Count -eq 0) { ... }
```

reports **zero uncovered items** on a manifest where thirteen are uncovered, because `gates` is
absent on most entries and `@($null)` is a one-element array. The audit says the work is finished.
Test for the property first:

```powershell
$gc = if ($it.PSObject.Properties.Name -contains 'gates') { @($it.gates).Count } else { 0 }
```

An audit that reports completion is exactly the one nobody re-runs, so prefer a shape that fails
loudly. Cross-check any "everything is covered" result against a second, differently-written query
before believing it.

### `create`-style file writes on Windows produce CRLF

Four `legacy.md` files authored through an editor tool landed with CRLF under `conformance/`,
where `.gitattributes` says `-text` and CI asserts no CR byte. The conformance suite passed anyway
— the harness reads `legacy.md` as prose — so only the byte-level gate caught it, and only just
before the commit. After authoring anything under `conformance/`, `tests/**/fixtures/**` or
`spikes/**` by any means other than an explicit LF write, sweep the tree:

```powershell
Get-ChildItem conformance -Recurse -File |
  Where-Object { [IO.File]::ReadAllBytes($_.FullName) -contains 13 }
```

Note that fixing line endings changes generated documents that embed those files, so re-run
`tools/sync-docs.ps1` afterwards and expect a real diff.

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

### A build check that greps for "error" matches "0 Error(s)"

The MSBuild summary always contains the word `Error`, so `if ($build -match 'error')` reports
`BUILD FAILED` on a build that succeeded, and a mutation run wastes a cycle. Match the anchored
count instead:

```powershell
if ($b -notmatch '(?m)^\s*0 Error\(s\)') { 'BUILD FAILED'; $b }
```

The converse costs more: a mutation run whose build genuinely failed leaves the **previous**
binaries in place, and `dotnet test --no-build` then reports a confident green for a mutation that
never ran. Always assert the build result before believing the test result.

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

This bites the harness *script itself*, not only the source it mutates. A `.ps1` written with
`create` is CRLF, so patching it afterwards with `$t.Replace("...`n        Test = ...", ...)` matches
nothing and **silently changes nothing** — the harness then re-runs unmodified and reports the same
result, which reads as the fix having had no effect. Normalize the harness on creation too, or
rewrite it rather than patching it.

### A lone surrogate cannot survive a `[TestCase]` attribute

Attribute arguments are stored in metadata as UTF-8, so a lone surrogate in a `[TestCase]` string is
replaced before the test ever runs — and because the encoder emits one replacement per malformed
byte, `"lone\uD83Dsurrogate"` arrives as `lone` + **three** U+FFFD + `surrogate`. That string is
perfectly printable, so a test asserting that unwritable text is refused fails against correct code,
and the obvious conclusion — that the guard is missing — is wrong.

Method-body string literals live in the `#US` heap, which is UTF-16, so a lone surrogate there
survives intact. Build the value in code and assert it is what you think before using it:

```csharp
string text = "lone" + (char)0xD83D + "surrogate";
text.ShouldContain("\uD83D", Case.Sensitive);
```

Read the attribute back through reflection when a test case behaves as if its input were different:
`method.GetCustomAttributesData()[i].ConstructorArguments[0].Value`.

### `char.IsControl` does not report U+2028 and U+2029

They are Unicode categories Zl and Zp, not Cc, so `char.IsControl('\u2028')` is **false** — yet YAML
normalizes both as line breaks exactly as it normalizes LF. A guard written as "reject control
characters" therefore lets through the two characters most able to destroy a text format, and the
damage is not altered data but an unparseable file. U+0085 (NEL) *is* a C1 control and is covered.

Any predicate here that means "cannot be written as itself" needs three tests, not one: controls,
the two separators, and lone surrogates (which are not control characters either, and which UTF-8
cannot encode at all).

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

### `@(@('from','to'))` collapses, and a one-edit mutation then edits single characters

PowerShell's array subexpression **enumerates** an inner array, so `@( @('a','b') )` is a flat
two-element array of strings, not a one-element array of pairs. A harness that stores
`edits = @(@($from, $to))` and reads `$edit[0]` / `$edit[1]` therefore indexes into the *strings*
and mutates one character into another. Mutations with two or more edits keep their nesting and
behave, so the defect hits exactly the simplest entries and looks like three unrelated problems:

- a replacement of a character by itself → a genuine no-op → **all tests green → a survivor that
  is not one**, which is the worst outcome the harness can produce;
- `MUTATION TEXT NOT FOUND for … : c` — a one-character needle;
- every `Q` in the file replaced by `u`, reported as `DID NOT COMPILE` with errors on three lines
  you did not touch.

Write `@(, @($from, $to))`. Independently, assert the edit changed something — that guard alone
catches every variant of this:

```powershell
if ($text -eq $original) { throw "NO-OP mutation: $($m.name)" }
```

### A hash-trie enumeration order agrees with the specified order often enough to hide a tiebreak

`ImmutableDictionary` enumerates by hash, so a determinism tiebreak over its keys — "report the
smallest sibling under §5.2" — has a fair chance of agreeing with insertion-independent hash order
for any *one* key set. Deleting the tiebreak then leaves the test green, and it is the third kind of
false survivor: the fixture is invariant under the mutation. Reversing the source order does not
help, because the trie is not insertion-ordered.

Assert the same specified outcome over a dozen different names in one test. Every case is the
specification's answer, so nothing pins a coincidence, and the mutant dies on the first name whose
hash order disagrees. `AliasedComponentWarningTests` does this.

### §5.2 compares the position mark *before* the component kind

The kind order — ordinary, qualified element, typed attribute, typed content — is a **tie-breaker**
that only runs when two siblings carry equal position marks. So an "is sorted, not merely walked"
test needs an input where the walk genuinely disagrees with the specified order, and XML will not
give you one for a kind tie: an element's attributes are read before its children, so in
`<a b="1"><p:b>2</p:b></a>` the attribute already holds the earlier mark and walk order coincides
with ordinal order by accident. Deleting the sort left that test green.

Author the input as a **profile**, declaring the components in the order that makes the walk
disagree — `r.a.Q{urn:p}b=2` before `r.a.@b=1`. Then walk order is `Q{urn:p}b, @b`, ordinal order is
`@b, Q{urn:p}b` (`@` is 0x40, `Q` is 0x51), and the mutant dies.

### The conformance comparer never compares `message`, so prose needs a unit test

§6.4.3 makes `message` "human-readable prose … never compared". Anything a diagnostic says only in
its message — which alternatives it lists, and in which order — is therefore invisible to the entire
corpus, and a mutation to it survives every fixture. That is not a corpus gap to be fixed by adding
a fixture; it is a deliberate boundary. Pin it with a unit test that reads `Diagnostic.Message`.

### A `count`-bounded loop mutation is inert when the test passes `count == pattern.Length`

Mutating `for (var i = 0; i < count && …)` to `i < pattern.Length` changes nothing for a caller that
passes the two equal, which the production call site does. A test written against that call site
therefore cannot kill it. Exercise the helper directly with a pattern **longer** than `count`.

### `DID NOT COMPILE` can mean the mutation was over-applied

`String.Replace` replaces **every** occurrence. An anchor like
`cardinalityKey: $"{rule.Declaration.Source}:{rule.Declaration.Line}"` appears at every diagnostic
call site in a file, so a mutation meant for one of them lands on all of them and fails to build on
the ones where the new identifier is out of scope — reported at lines you did not intend to touch.
Widen the anchor to several lines, or replace a single occurrence by index and assert on the
surrounding text that it is the one you meant.

### On Unix a leading dot is the Hidden attribute, and `Get-ChildItem` drops it

`Get-ChildItem` omits hidden entries unless `-Force` is passed, and on Unix .NET reports the Hidden
attribute for any name beginning with `.`. A script that walks a generated output tree therefore
sees strictly fewer files on Linux and macOS than on Windows, where the same name is not hidden.

This is how `cross-os-hash` failed for four commits with a §24 violation it invented itself: the
corpus contains an output named `..conf`, the Windows digest listed it, the Unix digests did not,
and the tool had been byte-identical on all three platforms the whole time — `build-test` compares
that same output against the fixture and passed everywhere.

Note which way the failure points. A false alarm is the *lucky* outcome: for every other dotfile
output the gate compared nothing at all and reported agreement. Pass `-Force` in any enumeration
whose result is an assertion about completeness.

### The local verification loop does not include the cross-OS gate

`hash-corpus-outputs.ps1` run on one machine proves the corpus is *self-consistent there*. The
comparison that §24 actually promises happens only in `cross-os-hash`, which diffs the digests three
runners uploaded. Nothing you can run locally exercises it.

So a green local loop is not a green build, and the gap is exactly the platform-dependent behaviour
you are least likely to predict. **Check `gh run list --branch v3` after pushing.** Four consecutive
red runs went unnoticed here because every local check passed and the failure was in a job with no
local equivalent.

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

### Dense rendering hides most sequence-ordering defects

Section 5.4 makes namespace and INI "display fresh dense indices", so an item at stable ordering
value `0` and an item at stable `3` both render as `.0` when they are the only survivor. Every
allocation defect below that visibility threshold — a lost high-water mark, an unabsorbed incoming
mark, a rebase that restarts at zero — publishes a **byte-identical file**.

Two contributions can therefore never discriminate. Reach for three: two to create the disagreement
and one carrying an *explicit* ordering value that lands on a slot only one of the readings leaves
free, so the readings differ in the item **count** rather than in one item's position. Pick the
explicit values by working out what each wrong reading would produce and addressing those slots; a
mutation that survives here usually means the third contribution addressed a value both readings
left free.

Avoid making the disagreement a patch — two nodes meeting at one ordering value — because §17.1
settles that by payload mark, and the fixture would then be asserting two rules at once.

### A sorted-order test can pass with the sort removed

`ImmutableDictionary<long, T>` enumerates a handful of small keys in ascending order anyway. A test
that inserted ordering values `7, 2, 40` and expected `2, 7, 40` therefore **survived deleting the
`OrderBy` entirely** -- it was asserting a coincidence of the backing store, not the Section 5.4
rule. The same applies to any "is sorted" assertion over a small or clustered key set.

Spread the keys across the real range and insert them out of order, then re-run the mutation that
removes the sort and confirm it goes red. A sort assertion you have not seen fail is not evidence
that anything is sorted.

### The most thorough fixture is often the one that cannot discriminate

A comment fixture covering nine positions -- document-leading, leading, inline, trailing, nested
boundary, sequence item, document-trailing -- passed, read convincingly, and **stayed green when the
Section 20 document-leading branch was deleted entirely**. Its input had one top-level key, so that
key's value was the output root, and a comment owned by the document and a comment bound to the
first entry are emitted in exactly the same place. The fixture asserted rendered bytes, and the two
rules do not differ in rendered bytes there.

The discriminating case was three lines long: publish the *second* of two top-level keys. Then a
comment bound to the first entry lands in a subtree no output selects and disappears, while an
ownerless one still reaches the document.

Coverage breadth is not discrimination. Before believing a fixture pins a rule, ask what the losing
alternative would have emitted -- and if the answer is "the same bytes", the fixture is documenting
the rule, not testing it. Delete the branch and watch the corpus, not the unit tests: here the unit
tests went red and the corpus did not, and only the corpus is the oracle.

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

That note was written when the per-path carry looked like a misreading to guard against. It was
instead a live defect: `ReplaceMerge` maxed the mark only at the node `filemerge` was declared on and
took the replacement's children wholesale, and `MergeNode` never recurses under `replace`, so every
descendant mark was dropped. Fixed in `a2481ba`. Two lessons worth keeping — a trap that describes
"the mutation that *would* discriminate" is describing a test you have not written, and it is worth
writing it; and a mutation whose scope is wrong can hide a real defect as easily as it can fake a
test gap.

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

As of the M3 exit, ten codes were uncalled: `SCHEME002`, `REFERENCE002`–`REFERENCE005`, `XML002`,
`COLLISION001`, `WARN005`, `WARN007` and `WARN010`.

**Re-measured after `WARN010` landed in M9: zero remain.** `SCHEME002` acquired a call site in
`ViewTransformer` during M4–M8 and `WARN010` in `PlanningPhase` during M9; both were verified by
running them, not by reading the audit. The paragraph above claimed two survivors at the M9 entry
and was already wrong about one of them, which is the same staleness it warns about — it had been
wrong by eight codes once before. Every registry code now has a call site, so **any** entry the
audit reports from here is a finding rather than an expected baseline.

Re-run the audit whenever a milestone claims a diagnostic is covered, and update this paragraph
rather than trusting it — it was stale by eight codes before anyone checked.

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

**Test a writer by reading its output back, not by looking at it.** M5 shipped 197 unit tests and
six conformance fixtures over the JSON and YAML writers. They caught **none** of the eight defects
two independent reviews then found, and five of those silently destroyed data at exit `0`. The
reason is structural, not effort: every one of those tests asserted *what the writer chose to emit*
for an ordinary value, which is a question the writer cannot get wrong, because the test was written
by reading the writer.

The defects lived in the values nobody writes a test for — a leading blank line, `...`, `<<`, a
BOM, an emoji, a value ending in a blank line, U+2028. The assertion that finds them is not "does
the output look right" but *"does a parser give the value back"*, and the parser must not be ours:
the tool's own reader shares the tool's own assumptions. PyYAML found all of them in one pass.

For any format the tool both reads and writes, a round-trip fixture through an independent parser is
worth more than a dozen shape assertions, and the values it carries should be chosen to be hostile
rather than representative.

**Ask what would notice if this were wrong.** A dual-model review found a defect on the default path
of every invocation that three separate mechanisms should each have caught, and all three failed
silently: no fixture reached that path, the comparer did not check layout, and the determinism script
discarded the stream being corrupted. The components were individually sound. The seam was not.

**Check that the remedy a diagnostic offers is available to the author.** Where the specification
admits two readings, the one whose error message gives an impossible instruction is the wrong one.
§15.2 grants "an unmarked component" the simple alias index, and a wildcard is not explicitly
marked, so folding it is an available reading — but it made `r.a.*.type` blocking on any element
carrying an attribute and a child of one name, with a `SCHEME002` telling the author to "mark the
component to name one of them outright". A wildcard was written to match both and cannot be marked.
That impossibility is the evidence the reading is wrong, and it is visible from the message alone.

**Run the reporter's actual file, not only a minimal probe.** The wildcard-fold defect above was
shipped with 2359 unit tests and 496 conformance cases green, and was found by re-running the
33 KB `logback.xml` attached to issue #24, which turned from a clean 1243-line run into a blocking
`TYPE001`. A corpus is authored from the specification and therefore contains the shapes its authors
thought of; a third party's configuration file contains the shapes they did not. Keep the attached
reproductions and re-run them after any change to matching, addressing or binding.

Minimal probes mislead in the other direction too. The same issue's deeper `*.*.key` selectors draw
`WARN009`, and an earlier attempt to reproduce that used a **profile** input and failed — profiles
have no whitespace text nodes, so the `#n` components that make `*.*` miss at depth 2 never existed.
Reproduce with the same *format* as the report, not merely the same shape.

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

### `git checkout --` on a generated file destroys uncommitted hand-authored fields

`conformance/assertions.json` is written by a generator but its `fixtures`, `gates` and
`whyNotAFixture` fields are **hand-authored and merely preserved** across runs. Reverting the file
to discard a generator artifact therefore silently discards registration work, and re-running the
generator cannot recover it — the generator preserves what it finds, and it now finds nothing.

Seventeen fixture registrations were lost this way in one command. The failure surfaces two steps
later as `AnItemNamesExactlyTheFixturesThatClaimIt`, which names a fixture/item mismatch rather than
the revert that caused it.

The registrations are recoverable because each fixture's `requirements.txt` is the other half of the
double entry: rebuild `fixtures` as the sorted set of fixture directories whose `requirements.txt`
names the item. Verify the restoration against an audit taken *before* the revert — the uncovered
set and the gated set must both come back identical — rather than against a green test run, because
the gate compares the manifest to `requirements.txt` and is satisfied by any consistent pair.

Prefer editing the file in place over `git checkout --` whenever the working copy holds uncommitted
manifest edits.