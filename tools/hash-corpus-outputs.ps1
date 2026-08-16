<#
.SYNOPSIS
    Hashes every byte the conformance corpus produces, so determinism can be measured rather
    than asserted.

.DESCRIPTION
    Specification Section 24 requires byte-identical output for identical inputs, on every
    supported platform, and Appendix C.7 extends that to structured diagnostics. This script runs
    each conformance case in both argument vectors, and emits a stable, ordinally sorted listing
    of exit code, standard output digest, standard error measurement and destination digests.

    A dual-model review found an earlier version discarding both streams, so the listing carried
    nothing but exit codes and the cross-platform gate compared six integers. Everything the
    specification calls contractual is measured here, or the gate proves nothing.

    What Section 24 calls contractual about the diagnostic stream is narrower than its bytes:
    "Diagnostic codes, severities, structured fields, and ordering must be identical; localized
    human-readable prose may differ", and Section 6.4.2 says of the text encoding that "Prose is
    localizable and is not part of byte-identical determinism". Hashing standard error verbatim
    therefore asserts more than the contract, and a conforming tool fails it: Section 7.2 requires
    a missing input's warning to name its *resolved* path, which is host-absolute and can never
    agree between a Linux, a macOS and a Windows runner. That is exactly how this gate first went
    red. So:

    - under the json vector, standard error is projected to its structured members with 'message'
      removed, canonicalised, and hashed. That projection is precisely Section 24's contractual
      set, and Section 6.4 guarantees the encoding switch "never changes which diagnostics occur,
      their fields, their cardinality, their order";
    - under the text vector, standard error is prose end to end and Section 6.4.2 places it
      outside byte-identical determinism, so only its presence is recorded.

    A gate must not be stronger than the contract it enforces. One that is will be silenced by
    whoever meets it next, and the honest half goes with it.

    Path separators are normalised to '/' because the destination *names* are contractual but
    the host's separator is not. Sorting is ordinal because Sort-Object collates by the current
    culture, which would make the output of a determinism oracle depend on the runner's locale.

    Appendix C.7 repeats every fixture under "at least two parser worker counts, including one",
    "at least two supported locales with different decimal conventions", "at least two time zones",
    and "repeated fresh output roots". An earlier version pinned one invariant environment on every
    runner, which made the oracle blind to the whole class of defect determinism is about: a
    culture-sensitive comparison, a localized number, or a scheduling-dependent order would have
    agreed with itself on all three platforms. The environments below are run per case and per
    vector, in fresh output roots, and every one must agree before a line is emitted; the emitted
    listing is then compared across platforms by the cross-os job.
#>
[CmdletBinding()]
param(
    [string] $Output = 'corpus-hashes.txt',
    [string] $Configuration = 'Release',
    [string[]] $Environments = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Appendix C.7, and a note on what each of these can actually catch, because a determinism oracle
# that overstates its own reach is the defect it exists to prevent.
#
# DOTNET_PROCESSOR_COUNT is the worker-count knob and is live on every platform: it sets
# Environment.ProcessorCount and with it the thread pool's sizing (verified — a child process
# reports 1 and 2 under it; the camel-cased spelling does nothing at all). The tool contains no
# parallelism today, so this dimension guards forward: it is what would catch output whose order
# depends on scheduling if any is ever introduced.
#
# TZ is honoured by .NET on Unix and ignored on Windows (verified), so the time-zone dimension is
# live on two of the three CI runners. That is enough for C.7, which asks for the repetition, not
# for it to bite on every host.
#
# LANG and LC_ALL are inert under InvariantGlobalization, which this project sets and Section 3
# says will not become configurable (verified — neither changes CurrentCulture or the decimal
# separator in a child process). The locale dimension is therefore satisfied by construction
# rather than by this probe, and the live guard is in-process: BigDecimalTests installs a culture
# with hostile separators, and RuntimeConfigurationTests asserts the shipped runtimeconfig still
# carries the invariant flag that makes the whole class impossible. These variables stay because
# they cost nothing and would become live the moment that flag were removed — which is the
# regression worth catching.
#
# tr-TR is kept for the dotless-i case mapping, which breaks culture-sensitive identifier
# comparison, for the same forward-guard reason.
$environmentMatrix = @(
    [pscustomobject]@{
        Name = 'invariant-1-worker'
        Variables = @{
            DOTNET_CLI_UI_LANGUAGE = 'en'; LANG = 'C'; LC_ALL = 'C'; TZ = 'UTC'
            DOTNET_PROCESSOR_COUNT = '1'
        }
    }
    [pscustomobject]@{
        Name = 'de-DE-4-workers'
        Variables = @{
            DOTNET_CLI_UI_LANGUAGE = 'en'; LANG = 'de_DE.UTF-8'; LC_ALL = 'de_DE.UTF-8'
            TZ = 'America/New_York'; DOTNET_PROCESSOR_COUNT = '4'
        }
    }
    [pscustomobject]@{
        Name = 'tr-TR-8-workers'
        Variables = @{
            DOTNET_CLI_UI_LANGUAGE = 'en'; LANG = 'tr_TR.UTF-8'; LC_ALL = 'tr_TR.UTF-8'
            TZ = 'Asia/Kolkata'; DOTNET_PROCESSOR_COUNT = '8'
        }
    }
)

if ($Environments.Count -gt 0) {
    $known = ($environmentMatrix | ForEach-Object Name) -join ', '
    $environmentMatrix = @($environmentMatrix | Where-Object { $Environments -contains $_.Name })
    if ($environmentMatrix.Count -eq 0) {
        throw "No environment matched -Environments. Known: $known"
    }
}

$root = Split-Path -Parent $PSScriptRoot
$corpus = Join-Path $root 'conformance'
$tool = Join-Path $root "src/Namespace2Xml.Cli/bin/$Configuration/net10.0/namespace2xml.dll"

if (-not (Test-Path $tool)) {
    throw "The tool is not built at '$tool'. Run: dotnet build namespace2xml.slnx -c $Configuration"
}

# Exactly the set ConformanceCaseTests.ReservedNames holds. A disagreement here would make the
# determinism listing and the conformance oracle describe different trees.
$reserved = @(
    'args.txt', 'args-diagnostics.txt', 'inputs', 'schemes', 'expected',
    'expected-diagnostics.json', 'expected-exit-code.txt', 'requirements.txt', 'legacy.md'
)

# Appendix C.1 tokenisation, matching ArgsFile: one token per line, blank lines are blank tokens,
# the file ends with LF and carries no CR. Dropping blank tokens here would run a different
# argument vector than the conformance harness runs.
function Read-ArgsFile([string] $path) {
    $text = [IO.File]::ReadAllText($path)
    if ($text.Contains("`r")) { throw "$path contains CR; Appendix C.1 requires LF only." }
    if ($text.Length -eq 0) { return @() }
    if (-not $text.EndsWith("`n")) { throw "$path does not end with LF." }
    return @($text.Substring(0, $text.Length - 1) -split "`n")
}

function Get-Digest([byte[]] $bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [Convert]::ToHexString($sha.ComputeHash($bytes)).ToLowerInvariant() }
    finally { $sha.Dispose() }
}

# Renders one JSON value in a form that depends only on the value, not on the writer: object
# members in ordinal key order, arrays in their given order because Section 24 makes diagnostic
# order contractual, numbers and strings by their raw source text. Written here rather than with
# ConvertTo-Json because that cmdlet neither fixes member order nor round-trips numeric text.
function Write-Canonical([Text.StringBuilder] $builder, $element, [string[]] $drop) {
    switch ($element.ValueKind) {
        'Object' {
            $names = [string[]] ($element.EnumerateObject() |
                ForEach-Object { $_.Name } |
                Where-Object { $drop -notcontains $_ })
            [Array]::Sort($names, [StringComparer]::Ordinal)
            [void] $builder.Append('{')
            for ($i = 0; $i -lt $names.Length; $i++) {
                if ($i -gt 0) { [void] $builder.Append(',') }
                [void] $builder.Append(($names[$i] | ConvertTo-Json -Compress)).Append(':')
                Write-Canonical $builder $element.GetProperty($names[$i]) @()
            }
            [void] $builder.Append('}')
        }
        'Array' {
            [void] $builder.Append('[')
            $first = $true
            foreach ($item in $element.EnumerateArray()) {
                if (-not $first) { [void] $builder.Append(',') }
                $first = $false
                Write-Canonical $builder $item $drop
            }
            [void] $builder.Append(']')
        }
        default { [void] $builder.Append($element.GetRawText()) }
    }
}

# Section 24's contractual view of the diagnostic stream: every structured member, in a fixed
# order, with the localizable prose removed. A stream that loses a diagnostic, reorders two, or
# changes any field still moves this digest; only the wording is free.
function Get-DiagnosticStructureDigest([byte[]] $stderr) {
    if ($stderr.Length -eq 0) { return 'empty' }

    try { $document = [Text.Json.JsonDocument]::Parse([Text.Encoding]::UTF8.GetString($stderr)) }
    catch { throw "standard error is not the single JSON array Section 6.4.3 requires: $($_.Exception.Message)" }

    try {
        $builder = [Text.StringBuilder]::new()
        Write-Canonical $builder $document.RootElement @('message')
        return Get-Digest ([Text.Encoding]::UTF8.GetBytes($builder.ToString()))
    }
    finally { $document.Dispose() }
}

# Section 6.4.1's pre-scan, which decides the encoding of standard error before any other
# argument is validated. Applied here rather than keying off the vector's label, because a case
# may name --diagnostics-format in args.txt, and args-diagnostics.txt may resolve to text.
function Resolve-DiagnosticsEncoding([string[]] $arguments) {
    $value = $null
    for ($i = 0; $i -lt $arguments.Count; $i++) {
        $token = $arguments[$i]
        if ($token -eq '--') { break }
        if ($token -eq '--diagnostics-format') {
            if ($i + 1 -lt $arguments.Count -and $arguments[$i + 1] -ne '--') { $value = $arguments[$i + 1] }
            else { $value = $null }
        }
        elseif ($token.StartsWith('--diagnostics-format=')) {
            $value = $token.Substring('--diagnostics-format='.Length)
        }
    }

    if ($null -ne $value -and [string]::Equals($value, 'json', 'OrdinalIgnoreCase')) { return 'json' }
    return 'text'
}

function Invoke-Tool([string] $workingDirectory, [string[]] $arguments, [hashtable] $variables) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = 'dotnet'
    $info.WorkingDirectory = $workingDirectory
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.UseShellExecute = $false
    $info.ArgumentList.Add($tool)
    foreach ($argument in $arguments) { $info.ArgumentList.Add($argument) }
    foreach ($entry in $variables.GetEnumerator()) { $info.Environment[$entry.Key] = $entry.Value }

    $process = [Diagnostics.Process]::Start($info)
    $out = New-Object IO.MemoryStream
    $err = New-Object IO.MemoryStream
    $outTask = $process.StandardOutput.BaseStream.CopyToAsync($out)
    $errTask = $process.StandardError.BaseStream.CopyToAsync($err)
    if (-not $process.WaitForExit(120000)) {
        $process.Kill($true)
        throw "The tool did not exit within 120 seconds in '$workingDirectory'."
    }
    [Threading.Tasks.Task]::WaitAll(@($outTask, $errTask))

    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        StandardOutput = $out.ToArray()
        StandardError = $err.ToArray()
    }
}

$lines = [Collections.Generic.List[string]]::new()
$scratch = Join-Path ([IO.Path]::GetTempPath()) ("n2x-corpus-" + [Guid]::NewGuid().ToString('N'))

$caseNames = [string[]] (Get-ChildItem -Path $corpus -Directory | ForEach-Object { $_.Name })
[Array]::Sort($caseNames, [StringComparer]::Ordinal)

try {
    foreach ($name in $caseNames) {
        $source = Join-Path $corpus $name

        $verbatim = Read-ArgsFile (Join-Path $source 'args.txt')
        $diagnosticPath = Join-Path $source 'args-diagnostics.txt'
        $diagnostic = if (Test-Path $diagnosticPath) {
            Read-ArgsFile $diagnosticPath
        } else {
            @($verbatim) + @('--diagnostics-format', 'json')
        }

        foreach ($vector in @(
            [pscustomobject]@{ Label = 'verbatim'; Arguments = $verbatim },
            [pscustomobject]@{ Label = 'json'; Arguments = $diagnostic })) {

            $encoding = Resolve-DiagnosticsEncoding ([string[]] $vector.Arguments)
            $agreed = $null
            $agreedName = $null

            foreach ($environment in $environmentMatrix) {
                # Appendix C requires a case never to run in place, so a produced destination can
                # never be mistaken for a fixture that was there all along. C.7 additionally asks
                # for a fresh output root per repetition, which is why the environment name is part
                # of the path rather than the directory being reused.
                $work = Join-Path $scratch "$name-$($vector.Label)-$($environment.Name)"
                New-Item -ItemType Directory -Force -Path $work | Out-Null
                Copy-Item -Path (Join-Path $source '*') -Destination $work -Recurse -Force

                $result = Invoke-Tool $work ([string[]] $vector.Arguments) $environment.Variables

                $measured = [Collections.Generic.List[string]]::new()
                $measured.Add(("{0}`t{1}`texit`t{2}" -f $name, $vector.Label, $result.ExitCode))
                $measured.Add(("{0}`t{1}`tstdout`t{2}" -f $name, $vector.Label, (Get-Digest $result.StandardOutput)))

                $stderr = if ($encoding -eq 'json') {
                    Get-DiagnosticStructureDigest $result.StandardError
                } elseif ($result.StandardError.Length -eq 0) {
                    'empty'
                } else {
                    'present'
                }
                $measured.Add(("{0}`t{1}`tstderr-{2}`t{3}" -f $name, $vector.Label, $encoding, $stderr))

                # -Force is load-bearing: on Unix a name beginning with '.' carries the Hidden
                # attribute, so Get-ChildItem omits it and the digest silently under-reports. The
                # corpus contains such an output ('..conf'), which made the cross-OS comparison
                # fail against Windows, where the same name is not hidden.
                foreach ($file in Get-ChildItem -Path $work -Recurse -File -Force) {
                    $relative = $file.FullName.Substring($work.Length + 1) -replace '\\', '/'
                    $segment = ($relative -split '/', 2)[0]
                    if ($reserved -contains $segment) { continue }

                    $hash = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                    $measured.Add(("{0}`t{1}`t{2}`t{3}" -f $name, $vector.Label, $relative, $hash))
                }

                foreach ($directory in Get-ChildItem -Path $work -Recurse -Directory -Force) {
                    $relative = $directory.FullName.Substring($work.Length + 1) -replace '\\', '/'
                    $segment = ($relative -split '/', 2)[0]
                    if ($reserved -contains $segment) { continue }

                    $measured.Add(("{0}`t{1}`t{2}/`tdirectory" -f $name, $vector.Label, $relative))
                }

                $ordered = $measured.ToArray()
                [Array]::Sort($ordered, [StringComparer]::Ordinal)

                if ($null -eq $agreed) {
                    $agreed = $ordered
                    $agreedName = $environment.Name
                    continue
                }

                # Section 24 makes this a contract, not a tolerance: the same inputs under a
                # different locale, time zone or worker count are still the same inputs.
                $difference = @(Compare-Object -ReferenceObject $agreed -DifferenceObject $ordered)
                if ($difference.Count -gt 0) {
                    $detail = ($difference | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }) -join "`n  "
                    throw ("Section 24: case '$name' vector '$($vector.Label)' is not " +
                        "environment-independent. '$agreedName' and '$($environment.Name)' differ:`n  $detail")
                }
            }

            $lines.AddRange($agreed)
        }
    }
}
finally {
    if (Test-Path $scratch) { Remove-Item -Recurse -Force $scratch }
}

$sorted = $lines.ToArray()
[Array]::Sort($sorted, [StringComparer]::Ordinal)

[IO.File]::WriteAllText(
    [IO.Path]::GetFullPath($Output),
    (($sorted -join "`n") + "`n"),
    (New-Object Text.UTF8Encoding $false))

Write-Host "Hashed $($sorted.Count) entries from $($caseNames.Count) cases into $Output."
