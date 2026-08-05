<#
.SYNOPSIS
    Hashes every byte the conformance corpus produces, so determinism can be measured rather
    than asserted.

.DESCRIPTION
    Specification Section 24 requires byte-identical output for identical inputs, on every
    supported platform, and Appendix C.7 extends that to structured diagnostics. This script runs
    each conformance case in both argument vectors, and emits a stable, ordinally sorted listing
    of exit code, standard output digest, standard error digest and destination digests.

    A dual-model review found the previous version discarding both streams, so the listing carried
    nothing but exit codes and the cross-platform gate compared six integers. Everything the
    specification calls contractual is hashed here, or the gate proves nothing.

    Path separators are normalised to '/' because the destination *names* are contractual but
    the host's separator is not. Sorting is ordinal because Sort-Object collates by the current
    culture, which would make the output of a determinism oracle depend on the runner's locale.
#>
[CmdletBinding()]
param(
    [string] $Output = 'corpus-hashes.txt',
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

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

function Invoke-Tool([string] $workingDirectory, [string[]] $arguments) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = 'dotnet'
    $info.WorkingDirectory = $workingDirectory
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.UseShellExecute = $false
    $info.ArgumentList.Add($tool)
    foreach ($argument in $arguments) { $info.ArgumentList.Add($argument) }
    $info.Environment['DOTNET_CLI_UI_LANGUAGE'] = 'en'
    $info.Environment['LANG'] = 'C'
    $info.Environment['LC_ALL'] = 'C'
    $info.Environment['TZ'] = 'UTC'

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

            # Appendix C requires a case never to run in place, so a produced destination can
            # never be mistaken for a fixture that was there all along.
            $work = Join-Path $scratch "$name-$($vector.Label)"
            New-Item -ItemType Directory -Force -Path $work | Out-Null
            Copy-Item -Path (Join-Path $source '*') -Destination $work -Recurse -Force

            $result = Invoke-Tool $work ([string[]] $vector.Arguments)

            $lines.Add(("{0}`t{1}`texit`t{2}" -f $name, $vector.Label, $result.ExitCode))
            $lines.Add(("{0}`t{1}`tstdout`t{2}" -f $name, $vector.Label, (Get-Digest $result.StandardOutput)))
            $lines.Add(("{0}`t{1}`tstderr`t{2}" -f $name, $vector.Label, (Get-Digest $result.StandardError)))

            foreach ($file in Get-ChildItem -Path $work -Recurse -File) {
                $relative = $file.FullName.Substring($work.Length + 1) -replace '\\', '/'
                $segment = ($relative -split '/', 2)[0]
                if ($reserved -contains $segment) { continue }

                $hash = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                $lines.Add(("{0}`t{1}`t{2}`t{3}" -f $name, $vector.Label, $relative, $hash))
            }

            foreach ($directory in Get-ChildItem -Path $work -Recurse -Directory) {
                $relative = $directory.FullName.Substring($work.Length + 1) -replace '\\', '/'
                $segment = ($relative -split '/', 2)[0]
                if ($reserved -contains $segment) { continue }

                $lines.Add(("{0}`t{1}`t{2}/`tdirectory" -f $name, $vector.Label, $relative))
            }
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