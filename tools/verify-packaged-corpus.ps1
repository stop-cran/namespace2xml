#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Runs the whole conformance corpus against a packaged tool rather than against the build output.

.DESCRIPTION
    `tools/verify-tool-install.ps1` proves the package is installable and runnable. It deliberately
    does not judge what the tool writes, because duplicating the oracle there would create a second,
    weaker opinion about correctness.

    This script closes the other half of that seam: it points the real oracle at the packaged
    artifact. The distinction matters because the two binaries are not the same bytes -- a release
    build normalizes source paths, so the assembly inside the package has never been the assembly
    the corpus judged. Everything between `dotnet build` and `dotnet tool install` -- packing, the
    NuGet layout, the generated runtimeconfig and deps files, the tool shim, the apphost -- can
    change behaviour while `dotnet build`, `dotnet test` and the install smoke test all stay green.

    It is deliberately not part of the per-push CI run. The seam it covers only matters at
    publication, and a fast CI is worth more on every other push than a check that can only fail
    for a release. The release workflow runs it before anything is pushed to nuget.org, so a
    failure costs a retag rather than a bad package under the trusted name.

    That argument was made once and was wrong in the way arguments about cost usually are: it
    reasoned about the subject and forgot the instrument. The seam does only matter at publication,
    but this script is ordinary code, and the first time it ran on Linux it could not see into
    `.store` and declared the package empty. So it now also runs on every push to master, where a
    defect in the check costs a commit instead of a retag. Pull requests are still spared the four
    minutes.

.PARAMETER Package
    Directory holding the .nupkg to install from. When omitted the CLI is packed into a temporary
    directory first, which is what a local run wants.

.PARAMETER Configuration
    Build configuration. Defaults to Release, which is what ships.

.PARAMETER ToolAssembly
    Skips packing and installing and judges this assembly directly. Used to prove the gate can fail:
    point it at a different build of the tool and the corpus must go red.
#>

[CmdletBinding()]
param(
    [string] $Package,
    [string] $Configuration = 'Release',
    [string] $ToolAssembly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = Split-Path -Parent $PSScriptRoot
$unique = [guid]::NewGuid().ToString('n')
$packDirectory = Join-Path ([IO.Path]::GetTempPath()) "n2x-corpus-pack-$unique"
$toolDirectory = Join-Path ([IO.Path]::GetTempPath()) "n2x-corpus-tool-$unique"
$packed = $false

function Assert-LastExitCode {
    param([string] $What)

    if ($LASTEXITCODE -ne 0) {
        throw "$What failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repository
try {
    if ($ToolAssembly) {
        $assembly = (Resolve-Path -LiteralPath $ToolAssembly).Path
    }
    else {
        if (-not $Package) {
            New-Item -ItemType Directory -Path $packDirectory -Force | Out-Null
            $packed = $true

            dotnet pack src/Namespace2Xml.Cli/Namespace2Xml.Cli.csproj -c $Configuration -o $packDirectory
            Assert-LastExitCode 'dotnet pack'

            $Package = $packDirectory
        }

        $Package = (Resolve-Path -LiteralPath $Package).Path

        $nupkg = Get-ChildItem -LiteralPath $Package -Filter 'namespace2xml.*.nupkg' |
            Where-Object { $_.Name -notlike '*.symbols.nupkg' } |
            Select-Object -First 1

        if (-not $nupkg) {
            throw "No namespace2xml package was found in $Package."
        }

        $version = $nupkg.BaseName -replace '^namespace2xml\.', ''
        Write-Host "Judging package version: $version"

        # Restricting the install to the package directory keeps it hermetic, and proves the
        # package carries everything it needs, on a machine whose configured feed is a proxy.
        dotnet tool install --tool-path $toolDirectory --source $Package --version $version namespace2xml
        Assert-LastExitCode 'dotnet tool install'

        # The payload sits under the tool store rather than beside the shim, and the corpus needs
        # the managed assembly itself: the harness launches it through the .NET muxer so that one
        # run is identical on every platform.
        # -Force is load-bearing: the payload sits under `.store`, and on Linux a leading dot makes
        # that directory hidden, which Get-ChildItem skips unless asked. Without it this search
        # finds nothing on the very platform the release runs on, while still passing on Windows,
        # where the name carries no such meaning.
        $found = Get-ChildItem -LiteralPath $toolDirectory -Recurse -Force -Filter 'namespace2xml.dll' |
            Where-Object { $_.FullName -match '[\\/]tools[\\/]' } |
            Select-Object -First 1

        if (-not $found) {
            throw "The installed tool carries no namespace2xml.dll under $toolDirectory."
        }

        $assembly = $found.FullName
    }

    Write-Host "Corpus will judge: $assembly"

    # Built without the override set, so the build itself is unaffected by it; the variable is read
    # once, when the harness first runs the tool.
    dotnet build tests/Namespace2Xml.Conformance/Namespace2Xml.Conformance.csproj -c $Configuration
    Assert-LastExitCode 'dotnet build'

    $env:N2X_TOOL_ASSEMBLY = $assembly
    try {
        dotnet test tests/Namespace2Xml.Conformance/Namespace2Xml.Conformance.csproj --no-build -c $Configuration
        Assert-LastExitCode 'dotnet test'
    }
    finally {
        Remove-Item Env:\N2X_TOOL_ASSEMBLY -ErrorAction SilentlyContinue
    }

    Write-Host "The packaged tool satisfies the conformance corpus."
}
finally {
    Pop-Location

    $temporary = @($toolDirectory)
    if ($packed) {
        $temporary += $packDirectory
    }

    foreach ($path in $temporary) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
