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

.PARAMETER VersionOverride
    Optional package version for testing release-specific links without changing the product version.
#>

[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $VersionOverride
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = Split-Path -Parent $PSScriptRoot
$unique = [guid]::NewGuid().ToString('n')
$packDirectory = Join-Path ([IO.Path]::GetTempPath()) "n2x-pack-$unique"
$toolDirectory = Join-Path ([IO.Path]::GetTempPath()) "n2x tool # % $unique"
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
    if ($VersionOverride) {
        if ($VersionOverride -ceq $declared) {
            throw 'The verification override must differ from the declared product version.'
        }
        $declared = $VersionOverride
    }
    if ([string]::IsNullOrWhiteSpace($declared)) {
        throw "The <Version> element in $propsPath is empty."
    }

    Write-Host "Declared version: $declared"

    dotnet pack src/Namespace2Xml.Cli/Namespace2Xml.Cli.csproj -c $Configuration "-p:Version=$declared" -o $packDirectory
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

    $extracted = Join-Path $workDirectory 'package'
    [IO.Compression.ZipFile]::ExtractToDirectory($package.FullName, $extracted)
    $required = @(
        'README.md', 'llms.txt', 'AGENTS.md', 'CONTRIBUTING.md', 'KNOWN-LIMITS.md',
        'docs/specification.md', 'docs/diagnostics.md', 'docs/usage-methodology.md',
        'docs/format-namespace.md', 'docs/format-json.md', 'docs/format-yaml.md',
        'docs/format-xml.md', 'docs/format-ini.md', 'spec/contract-bundle.json',
        'spec/diagnostics.registry.json', 'spec/diagnostic-stream.schema.json',
        'conformance/assertions.json'
    )
    foreach ($item in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $extracted $item) -PathType Leaf)) {
            throw "Package is missing required offline document: $item"
        }
        foreach ($omitted in @('docs/migration-2.x-to-3.0.md', 'ansible/README.md', '.github/workflows/ci.yml')) {
            if (Test-Path -LiteralPath (Join-Path $extracted $omitted)) {
                throw "Source-only document unexpectedly bundled: $omitted"
            }
        }
    }

    $bundle = Get-Content -LiteralPath (Join-Path $extracted 'spec/contract-bundle.json') -Raw | ConvertFrom-Json
    foreach ($entry in @($bundle.specification, $bundle.registry)) {
        $actual = (Get-FileHash -LiteralPath (Join-Path $extracted $entry.path) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -cne $entry.sha256) {
            throw "Packaged $($entry.path) does not match the contract bundle."
        }
    }

    foreach ($item in $required | Where-Object { $_ -match '\.(md|txt)$' }) {
        $path = Join-Path $extracted $item
        $text = [IO.File]::ReadAllText($path)
        foreach ($match in [regex]::Matches($text, '\]\(([^)]+)\)')) {
            $target = $match.Groups[1].Value
            if ($target -match '^https?://') {
                if ($target -match '^https://github\.com/stop-cran/namespace2xml/(blob|tree)/master/') {
                    throw "$item contains a moving source link: $target"
                }
                continue
            }
            if ($target.StartsWith('#')) { continue }
            $local = ($target -split '#', 2)[0]
            if ($local -and -not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $path) $local))) {
                throw "$item links to a missing packaged target: $target"
            }
        }
    }

    $index = [IO.File]::ReadAllText((Join-Path $extracted 'llms.txt'))
    foreach ($phrase in @('metadata only', 'online only', '/conformance)', '/tests/Namespace2Xml.Conformance)', '/.github/workflows/ci.yml)')) {
        if (-not $index.Contains($phrase, [StringComparison]::Ordinal)) {
            throw "Packaged index is missing its verification boundary: $phrase"
        }
        foreach ($target in @(
            'blob/docs/migration-2.x-to-3.0.md',
            'blob/ansible/README.md',
            'blob/ansible/docs/specification-summary.md',
            'blob/ansible/plugins/filter/render.py',
            'blob/ansible/plugins/modules/render.py',
            'tree/conformance',
            'tree/tests/Namespace2Xml.Conformance',
            'blob/.github/workflows/ci.yml'
        )) {
            $parts = $target.Split('/', 2)
            $expected = "https://github.com/stop-cran/namespace2xml/$($parts[0])/v$declared/$($parts[1])"
            if (-not $index.Contains($expected, [StringComparison]::Ordinal)) {
                throw "Packaged index is missing its matching release link: $expected"
            }
        }
        $supportingLinks = @{
            'README.md' = @('docs/migration-2.x-to-3.0.md', 'ansible/README.md')
            'CONTRIBUTING.md' = @('tools/integration-rig/README.md', 'CODE_OF_CONDUCT.md', 'SECURITY.md')
        }
        foreach ($item in $supportingLinks.Keys) {
            $text = [IO.File]::ReadAllText((Join-Path $extracted $item))
            foreach ($target in $supportingLinks[$item]) {
                $expected = "https://github.com/stop-cran/namespace2xml/blob/v$declared/$target"
                if (-not $text.Contains($expected, [StringComparison]::Ordinal)) {
                    throw "$item is missing its matching release link: $expected"
                }
            }
        }
        $xmlGuide = [IO.File]::ReadAllText((Join-Path $extracted 'docs/format-xml.md'))
        if (-not $xmlGuide.Contains("/tree/v$declared/conformance", [StringComparison]::Ordinal)) {
            throw 'The XML guide does not link to the matching source corpus.'
        }
    }
    $nuspec = Get-ChildItem -LiteralPath $extracted -Filter '*.nuspec' | Select-Object -First 1
    $nuspecText = [IO.File]::ReadAllText($nuspec.FullName)
    if (-not $nuspecText.Contains("/blob/v$declared/CHANGELOG.md", [StringComparison]::Ordinal)) {
        throw 'Package release notes do not point to the matching online changelog.'
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

    $localField = @($versionOutput | Where-Object { $_ -clike 'local-documentation: *' })
    if ($localField.Count -ne 1) {
        throw 'The installed tool must report exactly one local documentation field.'
    }
    $uri = [Uri]$localField[0].Substring('local-documentation: '.Length)
    if (-not $uri.IsFile -or -not (Test-Path -LiteralPath $uri.LocalPath -PathType Leaf)) {
        throw "The installed index is not a readable file URI: $($localField[0])"
    }
    $expectedRoot = (Get-ChildItem -LiteralPath $toolDirectory -Filter 'namespace2xml.dll' -Recurse -Force |
        Where-Object { $_.FullName -match '[\\/]tools[\\/]' } | Select-Object -First 1).Directory.Parent.Parent.Parent.FullName
    if ($uri.LocalPath -cne (Join-Path $expectedRoot 'llms.txt')) {
        throw "The reported index is not from the running package: $($uri.LocalPath)"
    }
    $localSpecification = Join-Path $expectedRoot 'docs/specification.md'
    if (-not (Test-Path -LiteralPath $localSpecification -PathType Leaf)) {
        throw 'The installed specification is missing.'
    }
    Remove-Item -LiteralPath $localSpecification
    $missingOutput = & $executable --version --diagnostics-format json
    Assert-LastExitCode 'namespace2xml --version with missing documentation'
    if ($missingOutput -cnotcontains 'local-documentation: unavailable') {
        throw 'A missing specification must be reported as unavailable.'
    }
    $helpOutput = & $executable --version --help --diagnostics-format invalid
    Assert-LastExitCode 'namespace2xml --help precedence'
    if (($helpOutput -join "`n") -notmatch 'local-documentation') {
        throw 'Help did not take precedence or explain local documentation.'
    }

    # Isolate all per-user tool and package state; never install into the caller's real profile.
    $savedHome = $env:HOME
    $savedProfile = $env:USERPROFILE
    $savedCliHome = $env:DOTNET_CLI_HOME
    $savedPackages = $env:NUGET_PACKAGES
    $savedGenerateCertificate = $env:DOTNET_GENERATE_ASPNET_CERTIFICATE
    $savedTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
    $isolatedHome = Join-Path $workDirectory 'isolated home'
    $env:HOME = $isolatedHome
    $env:USERPROFILE = $isolatedHome
    $env:DOTNET_CLI_HOME = $isolatedHome
    $env:NUGET_PACKAGES = Join-Path $workDirectory 'package cache'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    try {
        New-Item -ItemType Directory -Path $isolatedHome -Force | Out-Null
        $nugetConfig = Join-Path $workDirectory 'nuget.config'
        $escapedFeed = [Security.SecurityElement]::Escape($packDirectory)
        [IO.File]::WriteAllText($nugetConfig, "<configuration><packageSources><clear/><add key=`"local`" value=`"$escapedFeed`"/></packageSources></configuration>")
        dotnet tool install --global namespace2xml --version $declared --configfile $nugetConfig
        Assert-LastExitCode 'global tool installation'
        $globalExecutable = Join-Path $isolatedHome '.dotnet/tools/namespace2xml'
        if ($IsWindows) { $globalExecutable += '.exe' }
        $globalVersion = & $globalExecutable --version
        Assert-LastExitCode 'global tool version'
        if ($globalVersion -cnotcontains "version: $declared") {
            throw 'The global tool reported the wrong version.'
        }
        $globalLocation = @($globalVersion | Where-Object { $_ -clike 'local-documentation: *' })
        if ($globalLocation.Count -ne 1 -or $globalLocation[0] -ceq 'local-documentation: unavailable') {
            throw 'The global tool did not report its installed index.'
        }
        $globalUri = [Uri]$globalLocation[0].Substring('local-documentation: '.Length)
        if (-not $globalUri.IsFile -or -not (Test-Path -LiteralPath $globalUri.LocalPath -PathType Leaf)) {
            throw 'The global tool did not report a readable index.'
        }
        $globalExpected = Join-Path $isolatedHome ".dotnet/tools/.store/namespace2xml/$declared/namespace2xml/$declared/llms.txt"
        if ($globalUri.LocalPath -cne $globalExpected) {
            throw 'The global tool reported an index from the wrong package.'
        }

        $localWork = Join-Path $workDirectory 'local manifest'
        New-Item -ItemType Directory -Path $localWork -Force | Out-Null
        Push-Location $localWork
        try {
            dotnet new tool-manifest
            Assert-LastExitCode 'tool manifest creation'
            dotnet tool install --local namespace2xml --version $declared --configfile $nugetConfig
            Assert-LastExitCode 'local tool installation'
            $localVersion = dotnet tool run namespace2xml --version
            Assert-LastExitCode 'local tool version'
            if ($localVersion -cnotcontains "version: $declared") {
                throw 'The local tool reported the wrong version.'
            }
            $localLocation = @($localVersion | Where-Object { $_ -clike 'local-documentation: *' })
            if ($localLocation.Count -ne 1 -or $localLocation[0] -ceq 'local-documentation: unavailable') {
                throw 'The local tool did not report its installed index.'
            }
            $localUri = [Uri]$localLocation[0].Substring('local-documentation: '.Length)
            if (-not $localUri.IsFile -or -not (Test-Path -LiteralPath $localUri.LocalPath -PathType Leaf)) {
                throw 'The local tool did not report a readable index.'
            }
            $localExpected = Join-Path $env:NUGET_PACKAGES "namespace2xml/$declared/llms.txt"
            if ($localUri.LocalPath -cne $localExpected) {
                throw 'The local tool reported an index from the wrong package.'
            }
        }
        finally {
            Pop-Location
        }
    }
    finally {
        $env:HOME = $savedHome
        $env:USERPROFILE = $savedProfile
        $env:DOTNET_CLI_HOME = $savedCliHome
        $env:NUGET_PACKAGES = $savedPackages
        $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = $savedGenerateCertificate
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = $savedTelemetry
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
