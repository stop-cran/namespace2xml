<#
.SYNOPSIS
    Hashes every byte the conformance corpus produces, so determinism can be measured rather
    than asserted.

.DESCRIPTION
    Specification Section 24 requires byte-identical output for identical inputs, on every
    supported platform. This script runs each conformance case and emits a stable, sorted
    listing of destination path and SHA-256. Comparing that listing across platforms is the
    only evidence that the requirement holds.

    Path separators are normalised to '/' because the destination *names* are contractual but
    the host's separator is not.
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

$reserved = @(
    'args.txt', 'args-diagnostics.txt', 'expected-exit-code.txt',
    'expected-diagnostics.json', 'requirements.txt', 'legacy.md', 'README.md'
)

$lines = [System.Collections.Generic.List[string]]::new()
$scratch = Join-Path ([IO.Path]::GetTempPath()) ("n2x-corpus-" + [Guid]::NewGuid().ToString('N'))

try {
    foreach ($case in Get-ChildItem -Path $corpus -Directory | Sort-Object Name) {
        # Appendix C requires a case never to run in place, so a produced destination can
        # never be mistaken for a fixture that was there all along.
        $work = Join-Path $scratch $case.Name
        New-Item -ItemType Directory -Force -Path $work | Out-Null
        Copy-Item -Path (Join-Path $case.FullName '*') -Destination $work -Recurse -Force

        $argsPath = Join-Path $work 'args.txt'
        $arguments = @()
        if (Test-Path $argsPath) {
            $text = [IO.File]::ReadAllText($argsPath) -replace "`r`n", "`n"
            $arguments = @($text -split "`n" | Where-Object { $_.Length -gt 0 })
        }

        Push-Location $work
        try {
            & dotnet $tool @arguments 2>$null | Out-Null
            $exitCode = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }

        $lines.Add(("{0}`texit`t{1}" -f $case.Name, $exitCode))

        foreach ($file in Get-ChildItem -Path $work -Recurse -File | Sort-Object FullName) {
            $relative = $file.FullName.Substring($work.Length + 1) -replace '\\', '/'
            if ($reserved -contains $relative) { continue }

            $hash = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            $lines.Add(("{0}`t{1}`t{2}" -f $case.Name, $relative, $hash))
        }
    }
}
finally {
    if (Test-Path $scratch) { Remove-Item -Recurse -Force $scratch }
}

$sorted = $lines | Sort-Object -CaseSensitive
[IO.File]::WriteAllText(
    [IO.Path]::GetFullPath($Output),
    (($sorted -join "`n") + "`n"),
    (New-Object Text.UTF8Encoding $false))

Write-Host "Hashed $($sorted.Count) entries from $((Get-ChildItem -Path $corpus -Directory).Count) cases into $Output."
