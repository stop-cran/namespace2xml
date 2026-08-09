#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Packs the CLI, installs it as a dotnet tool from that package alone, and asserts the installed
    tool reports the version the repository claims and runs end to end.

.DESCRIPTION
    A packaged tool is not the same artifact as a built binary. Packing, the NuGet layout, the tool
    shim and the apphost all sit between them, and each can break while `dotnet build` and
    `dotnet test` stay green. This script installs what a user installs.

    Its scope is deliberately narrow: it proves the artifact is installable and runnable, not that
    the transformation is correct. Correctness is the conformance corpus's job, and duplicating the
    oracle here would only create a second, weaker opinion about what the tool should print.

    The version assertion is anchored exactly as the release workflow's is, so a mismatch between
    Directory.Build.props and what the installed binary reports fails here, on every push, rather
    than at tag time when the only remaining move is to retag.

.PARAMETER Configuration
    The build configuration to pack. Defaults to Release, which is what ships.
#>

[CmdletBinding()]
param(
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = Split-Path -Parent $PSScriptRoot
$unique = [guid]::NewGuid().ToString('n')
$packDirectory = Join-Path ([IO.Path]::GetTempPath()) "n2x-pack-$unique"
$toolDirectory = Join-Path ([IO.Path]::GetTempPath()) "n2x-tool-$unique"
$workDirectory = Join-Path ([IO.Path]::GetTempPath()) "n2x-work-$unique"

function Assert-LastExitCode {
    param([string] $What)

    if ($LASTEXITCODE -ne 0) {
        throw "$What failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repository
try {
    New-Item -ItemType Directory -Path $packDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $workDirectory -Force | Out-Null

    # The declared version is the contract this script holds the built artifact to. XPath, rather
    # than property access, because Directory.Build.props has several PropertyGroup elements and
    # only one of them carries Version.
    $propsPath = Join-Path $repository 'Directory.Build.props'
    $props = [xml](Get-Content -LiteralPath $propsPath -Raw)
    $versionNode = $props.SelectSingleNode('/Project/PropertyGroup/Version')
    if (-not $versionNode) {
        throw "No <Version> element was found in $propsPath."
    }

    $declared = $versionNode.InnerText.Trim()
    if ([string]::IsNullOrWhiteSpace($declared)) {
        throw "The <Version> element in $propsPath is empty."
    }

    Write-Host "Declared version: $declared"

    dotnet pack src/Namespace2Xml.Cli/Namespace2Xml.Cli.csproj -c $Configuration -o $packDirectory
    Assert-LastExitCode 'dotnet pack'

    $package = Get-ChildItem -LiteralPath $packDirectory -Filter 'namespace2xml.*.nupkg' |
        Where-Object { $_.Name -notlike '*.symbols.nupkg' } |
        Select-Object -First 1

    if (-not $package) {
        throw "No namespace2xml package was produced in $packDirectory."
    }

    $packed = $package.BaseName -replace '^namespace2xml\.', ''
    if ($packed -cne $declared) {
        throw "The packed version '$packed' is not the declared version '$declared'."
    }

    # The tool package carries its dependencies inside tools/, and its nuspec declares none, so the
    # local folder is the only feed the install needs. Restricting it proves that, and keeps the
    # check hermetic on a machine whose configured feed is a private proxy.
    dotnet tool install --tool-path $toolDirectory --source $packDirectory --version $declared namespace2xml
    Assert-LastExitCode 'dotnet tool install'

    $executable = if ($IsWindows) {
        Join-Path $toolDirectory 'namespace2xml.exe'
    }
    else {
        Join-Path $toolDirectory 'namespace2xml'
    }

    if (-not (Test-Path -LiteralPath $executable)) {
        throw "The install produced no executable at $executable."
    }

    $versionOutput = & $executable --version
    Assert-LastExitCode 'namespace2xml --version'

    # Anchored, exactly as the release workflow anchors it against the tag. A commit suffix on the
    # informational version would break this line, which is why
    # IncludeSourceRevisionInInformationalVersion is false.
    if ($versionOutput -notcontains "version: $declared") {
        throw "The installed tool did not report 'version: $declared'. It reported:`n$($versionOutput -join "`n")"
    }

    if (-not ($versionOutput | Where-Object { $_ -like 'contract-bundle: *' })) {
        throw "The installed tool reported no contract-bundle revision.`n$($versionOutput -join "`n")"
    }

    # One transformation, to prove the installed artifact runs rather than to judge what it writes.
    $profilePath = Join-Path $workDirectory 'profile.txt'
    $schemePath = Join-Path $workDirectory 'scheme.txt'
    $outputDirectory = Join-Path $workDirectory 'out'
    $utf8 = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($profilePath, "a.b=1`n", $utf8)
    [IO.File]::WriteAllText($schemePath, "a.output=json`n", $utf8)

    & $executable -i $profilePath -s $schemePath -o $outputDirectory
    Assert-LastExitCode 'namespace2xml transform'

    $produced = Join-Path $outputDirectory 'a.json'
    if (-not (Test-Path -LiteralPath $produced)) {
        throw "The installed tool wrote no output at $produced."
    }

    if ((Get-Content -LiteralPath $produced -Raw) -notmatch '"b"') {
        throw "The output at $produced does not carry the emitted key."
    }

    Write-Host "Installed tool verified: $declared"
}
finally {
    Pop-Location

    foreach ($path in @($packDirectory, $toolDirectory, $workDirectory)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
