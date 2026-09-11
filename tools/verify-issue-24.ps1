#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Verifies issue #24 against one exact namespace2xml package.

.DESCRIPTION
    Installs the package from a cleared local-only NuGet source, runs the reporter's exact
    logback.xml outside the source tree, and checks process channels, structured diagnostics,
    publication count, XML identity and parentage, comments, and the complete intentional byte
    delta. The resulting JSON evidence ties the acceptance result to the package and workflow.

.PARAMETER Package
    Exact .nupkg file, or a directory containing exactly one namespace2xml .nupkg.

.PARAMETER ExpectedPackageSha256
    Optional expected package SHA-256. A mismatch fails before installation.

.PARAMETER EvidencePath
    Optional path for the machine-readable acceptance evidence. The same JSON is written to
    standard output when this parameter is omitted.

.PARAMETER TestMutation
    Reviewer-only C7 self-test. AppenderIdentity mutates one appender identity after the real tool
    run and before the oracle; the verifier must reject it.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Package,

    [string] $ExpectedPackageSha256,

    [string] $EvidencePath,

    [ValidateSet('None', 'AppenderIdentity')]
    [string] $TestMutation = 'None'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $repository 'tests\Namespace2Xml.UnitTests\TestData\issue-24-logback.xml'
$unique = [guid]::NewGuid().ToString('n')
$root = Join-Path ([IO.Path]::GetTempPath()) "n2x-issue-24-$unique"
$toolDirectory = Join-Path $root 'tool'
$workDirectory = Join-Path $root 'work'
$inputDirectory = Join-Path $workDirectory 'input'
$outputDirectory = Join-Path $workDirectory 'output'
$stdoutPath = Join-Path $workDirectory 'stdout.bin'
$stderrPath = Join-Path $workDirectory 'stderr.bin'
$versionStdoutPath = Join-Path $workDirectory 'version-stdout.bin'
$versionStderrPath = Join-Path $workDirectory 'version-stderr.bin'
$utf8 = [Text.UTF8Encoding]::new($false, $true)

function Get-Sha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-Equal {
    param(
        [Parameter(Mandatory)] $Actual,
        [Parameter(Mandatory)] $Expected,
        [Parameter(Mandatory)] [string] $What
    )

    if ($Actual -cne $Expected) {
        throw "$What was '$Actual'; expected '$Expected'."
    }
}

function Assert-Sequence {
    param(
        [Parameter(Mandatory)] [object[]] $Actual,
        [Parameter(Mandatory)] [object[]] $Expected,
        [Parameter(Mandatory)] [string] $What
    )

    if ($Actual.Count -ne $Expected.Count) {
        throw "$What had $($Actual.Count) item(s); expected $($Expected.Count)."
    }

    for ($index = 0; $index -lt $Actual.Count; $index++) {
        if ([string] $Actual[$index] -cne [string] $Expected[$index]) {
            throw "$What differed at index ${index}: '$($Actual[$index])' != '$($Expected[$index])'."
        }
    }
}

function Get-OrdinallySorted([object[]] $Values) {
    $strings = [string[]] @($Values | ForEach-Object { [string] $_ })
    [Array]::Sort($strings, [StringComparer]::Ordinal)
    return $strings
}

function Invoke-CapturedProcess {
    param(
        [Parameter(Mandatory)] [string] $FileName,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [Parameter(Mandatory)] [string] $StandardOutputPath,
        [Parameter(Mandatory)] [string] $StandardErrorPath
    )

    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $FileName
    $start.WorkingDirectory = $WorkingDirectory
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true

    foreach ($argument in $Arguments) {
        $start.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $out = $null
    $err = $null

    try {
        if (-not $process.Start()) {
            throw "Could not start '$FileName'."
        }

        $out = [IO.File]::Create($StandardOutputPath)
        $err = [IO.File]::Create($StandardErrorPath)
        $copyOut = $process.StandardOutput.BaseStream.CopyToAsync($out)
        $copyErr = $process.StandardError.BaseStream.CopyToAsync($err)
        $process.WaitForExit()
        [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]] @($copyOut, $copyErr))

        return $process.ExitCode
    }
    finally {
        if ($out) {
            $out.Dispose()
        }

        if ($err) {
            $err.Dispose()
        }

        $process.Dispose()
    }
}

function Get-NamedIdentities {
    param(
        [Parameter(Mandatory)] [Xml.Linq.XDocument] $Document,
        [Parameter(Mandatory)] [string] $Element,
        [Parameter(Mandatory)] [string] $Attribute
    )

    return Get-OrdinallySorted @(
        $Document.Descendants($Element) |
            ForEach-Object {
                $value = $_.Attribute($Attribute)
                if (-not $value) {
                    throw "An <$Element> element has no '$Attribute' attribute."
                }

                $value.Value
            })
}

function Get-ReferenceParentage([Xml.Linq.XDocument] $Document) {
    return Get-OrdinallySorted @(
        $Document.Descendants('appender-ref') |
            ForEach-Object {
                $parent = $_.Parent
                if (-not $parent) {
                    throw 'An <appender-ref> element has no parent.'
                }

                $reference = $_.Attribute('ref')
                if (-not $reference) {
                    throw 'An <appender-ref> element has no ref attribute.'
                }

                $owner = if ($parent.Name.LocalName -ceq 'root') {
                    'root'
                }
                else {
                    $name = $parent.Attribute('name')
                    if (-not $name) {
                        throw "The <$($parent.Name.LocalName)> parent of an <appender-ref> has no name."
                    }

                    "$($parent.Name.LocalName):$($name.Value)"
                }

                "$owner->$($reference.Value)"
            })
}

function Resolve-Package([string] $Path) {
    $resolved = Get-Item -LiteralPath $Path

    if (-not $resolved.PSIsContainer) {
        if ($resolved.Name -notlike 'namespace2xml.*.nupkg' -or
            $resolved.Name -like '*.symbols.nupkg') {
            throw "'$($resolved.FullName)' is not a namespace2xml tool package."
        }

        return $resolved
    }

    $packages = @(
        Get-ChildItem -LiteralPath $resolved.FullName -File -Filter 'namespace2xml.*.nupkg' |
            Where-Object { $_.Name -notlike '*.symbols.nupkg' })

    if ($packages.Count -ne 1) {
        throw "'$($resolved.FullName)' contains $($packages.Count) namespace2xml tool packages; expected exactly one."
    }

    return $packages[0]
}

$packageFile = Resolve-Package $Package
$packageHash = Get-Sha256 $packageFile.FullName
$version = $packageFile.BaseName -replace '^namespace2xml\.', ''

if ($ExpectedPackageSha256 -and
    $packageHash -cne $ExpectedPackageSha256.ToLowerInvariant()) {
    throw "Package SHA-256 was '$packageHash'; expected '$($ExpectedPackageSha256.ToLowerInvariant())'."
}

try {
    New-Item -ItemType Directory -Path $toolDirectory, $inputDirectory, $outputDirectory -Force |
        Out-Null

    $fixtureHash = Get-Sha256 $fixture
    Assert-Equal $fixtureHash 'f3f1bad107807dc2fae8e8674c077eef109311a6feb43aab4ae60db7a44bc3d9' 'Fixture SHA-256'
    Assert-Equal (Get-Item -LiteralPath $fixture).Length 33002 'Fixture size'

    $inputPath = Join-Path $inputDirectory 'logback.xml'
    $schemePath = Join-Path $workDirectory 'scheme.properties'
    Copy-Item -LiteralPath $fixture -Destination $inputPath
    [IO.File]::WriteAllText(
        $schemePath,
        (@(
            "configuration.output=xml`n"
            "configuration.root=configuration`n"
            "configuration.filename=logback.xml`n"
        ) -join ''),
        $utf8)

    # A cleared NuGet.Config makes the installation evidence stronger than --source alone: no
    # configured machine or organization feed can satisfy any part of the install.
    $nugetConfig = Join-Path $root 'NuGet.Config'
    $escapedSource = [Security.SecurityElement]::Escape($packageFile.DirectoryName)
    [IO.File]::WriteAllText(
        $nugetConfig,
        (@(
            "<?xml version=`"1.0`" encoding=`"utf-8`"?>`n"
            "<configuration>`n"
            "  <packageSources>`n"
            "    <clear />`n"
            "    <add key=`"local`" value=`"$escapedSource`" />`n"
            "  </packageSources>`n"
            "</configuration>`n"
        ) -join ''),
        $utf8)

    & dotnet tool install --tool-path $toolDirectory --configfile $nugetConfig --version $version namespace2xml
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool install failed with exit code $LASTEXITCODE."
    }

    $executable = if ($IsWindows) {
        Join-Path $toolDirectory 'namespace2xml.exe'
    }
    else {
        Join-Path $toolDirectory 'namespace2xml'
    }

    if (-not (Test-Path -LiteralPath $executable)) {
        throw "The package installed no executable at '$executable'."
    }

    $versionExit = Invoke-CapturedProcess `
        -FileName $executable `
        -Arguments @('--version') `
        -WorkingDirectory $workDirectory `
        -StandardOutputPath $versionStdoutPath `
        -StandardErrorPath $versionStderrPath
    Assert-Equal $versionExit 0 'namespace2xml --version exit code'

    $versionStderr = [IO.File]::ReadAllBytes($versionStderrPath)
    Assert-Equal $versionStderr.Length 0 'namespace2xml --version stderr byte count'
    $versionOutput = $utf8.GetString([IO.File]::ReadAllBytes($versionStdoutPath))
    if ($versionOutput -notmatch "(?m)^version: $([regex]::Escape($version))$") {
        throw "The installed tool did not report version '$version'. It reported:`n$versionOutput"
    }

    if ($versionOutput -notmatch '(?m)^contract-bundle: .+$') {
        throw "The installed tool reported no contract-bundle revision.`n$versionOutput"
    }

    $arguments = [string[]] @(
        '-i', $inputPath,
        '-s', $schemePath,
        '-o', $outputDirectory,
        '--diagnostics-format', 'json')
    $exitCode = Invoke-CapturedProcess `
        -FileName $executable `
        -Arguments $arguments `
        -WorkingDirectory $workDirectory `
        -StandardOutputPath $stdoutPath `
        -StandardErrorPath $stderrPath

    Assert-Equal $exitCode 0 'Transformation exit code'

    $stdout = [IO.File]::ReadAllBytes($stdoutPath)
    Assert-Equal $stdout.Length 0 'Transformation stdout byte count'

    $stderr = [IO.File]::ReadAllBytes($stderrPath)
    Assert-Sequence $stderr ([byte[]] @(0x5b, 0x5d, 0x0a)) 'Structured stderr bytes'
    $diagnostics = [Text.Json.JsonDocument]::Parse(
        [Text.Encoding]::UTF8.GetString($stderr))
    try {
        Assert-Equal $diagnostics.RootElement.ValueKind ([Text.Json.JsonValueKind]::Array) 'Diagnostic root'
        Assert-Equal $diagnostics.RootElement.GetArrayLength() 0 'Diagnostic count'
    }
    finally {
        $diagnostics.Dispose()
    }

    $outputs = @(Get-ChildItem -LiteralPath $outputDirectory -File -Recurse)
    Assert-Equal $outputs.Count 1 'Published output count'
    $outputPath = Join-Path $outputDirectory 'logback.xml'
    Assert-Equal $outputs[0].FullName $outputPath 'Published output path'

    if ($TestMutation -ceq 'AppenderIdentity') {
        $mutated = [regex]::new('(<appender\b[^>]*\bname=")[^"]+').Replace(
            $utf8.GetString([IO.File]::ReadAllBytes($outputPath)),
            '${1}ISSUE24_MUTANT',
            1)
        [IO.File]::WriteAllText($outputPath, $mutated, $utf8)
        Write-Host 'ISSUE24_MUTATION_APPLIED: AppenderIdentity'
    }

    $inputBytes = [IO.File]::ReadAllBytes($inputPath)
    $outputBytes = [IO.File]::ReadAllBytes($outputPath)
    $inputText = $utf8.GetString($inputBytes)
    $outputText = $utf8.GetString($outputBytes)
    $inputDocument = [Xml.Linq.XDocument]::Parse(
        $inputText,
        [Xml.Linq.LoadOptions]::PreserveWhitespace)
    $outputDocument = [Xml.Linq.XDocument]::Parse(
        $outputText,
        [Xml.Linq.LoadOptions]::PreserveWhitespace)

    $inputComments = [string[]] @(
        $inputDocument.DescendantNodes() |
            Where-Object { $_ -is [Xml.Linq.XComment] } |
            ForEach-Object { $_.Value })
    $outputComments = [string[]] @(
        $outputDocument.DescendantNodes() |
            Where-Object { $_ -is [Xml.Linq.XComment] } |
            ForEach-Object { $_.Value })
    Assert-Equal $inputComments.Count 8 'Input comment count'
    Assert-Sequence $outputComments $inputComments 'XML comments in source order'

    $leadingComments = [string[]] @(
        $outputDocument.Nodes() |
            Where-Object { $_ -is [Xml.Linq.XComment] } |
            ForEach-Object { $_.Value })
    Assert-Sequence $leadingComments ([string[]] @(' // @formatter:off ')) 'Document-envelope comments'

    $namedCounts = [ordered] @{}
    foreach ($expectation in @(
        @{ Element = 'appender'; Attribute = 'name'; Count = 33 },
        @{ Element = 'logger'; Attribute = 'name'; Count = 42 },
        @{ Element = 'property'; Attribute = 'name'; Count = 7 })) {
        $expected = Get-NamedIdentities $inputDocument $expectation.Element $expectation.Attribute
        $actual = Get-NamedIdentities $outputDocument $expectation.Element $expectation.Attribute
        Assert-Equal $expected.Count $expectation.Count "$($expectation.Element) identity count"
        Assert-Sequence $actual $expected "$($expectation.Element) identities"
        $namedCounts[$expectation.Element] = $actual.Count
    }

    $inputParentage = Get-ReferenceParentage $inputDocument
    $outputParentage = Get-ReferenceParentage $outputDocument
    Assert-Equal $inputParentage.Count 39 'appender-ref parentage count'
    Assert-Sequence $outputParentage $inputParentage 'appender-ref identities and parentage'

    $expectedText = [regex]::Replace(
        $inputText.Replace('encoding="UTF-8"', 'encoding="utf-8"', [StringComparison]::Ordinal),
        '(?<!\s)/>',
        ' />')
    $expectedBytes = $utf8.GetBytes($expectedText)
    Assert-Sequence $outputBytes $expectedBytes 'Complete output bytes'

    $evidence = [ordered] @{
        schemaVersion = 1
        acceptance = 'https://github.com/stop-cran/namespace2xml/issues/24'
        workflow = [ordered] @{
            repository = $env:GITHUB_REPOSITORY
            runId = $env:GITHUB_RUN_ID
            runAttempt = $env:GITHUB_RUN_ATTEMPT
            runUrl = if ($env:GITHUB_SERVER_URL -and $env:GITHUB_REPOSITORY -and $env:GITHUB_RUN_ID) {
                "$($env:GITHUB_SERVER_URL)/$($env:GITHUB_REPOSITORY)/actions/runs/$($env:GITHUB_RUN_ID)"
            }
            else {
                $null
            }
            commit = $env:GITHUB_SHA
        }
        package = [ordered] @{
            fileName = $packageFile.Name
            version = $version
            size = $packageFile.Length
            sha256 = $packageHash
        }
        fixture = [ordered] @{
            fileName = 'issue-24-logback.xml'
            size = $inputBytes.Length
            sha256 = $fixtureHash
        }
        tool = [ordered] @{
            executable = $executable
            versionOutput = $versionOutput -split "`n" | Where-Object { $_.Length -gt 0 }
        }
        invocation = [ordered] @{
            workingDirectory = $workDirectory
            executable = $executable
            arguments = $arguments
        }
        result = [ordered] @{
            exitCode = $exitCode
            stdoutBytes = $stdout.Length
            stderrUtf8 = $utf8.GetString($stderr)
            diagnosticCount = 0
            publishedOutputCount = $outputs.Count
            outputFileName = $outputs[0].Name
            outputSize = $outputBytes.Length
            outputSha256 = Get-Sha256 $outputPath
            commentCount = $outputComments.Count
            leadingComment = $leadingComments[0]
            namedElementCounts = $namedCounts
            appenderReferenceCount = $outputParentage.Count
            completeByteOracle = 'passed'
        }
    }

    $json = $evidence | ConvertTo-Json -Depth 8
    if ($EvidencePath) {
        $evidenceFile = [IO.Path]::GetFullPath($EvidencePath)
        $evidenceDirectory = Split-Path -Parent $evidenceFile
        if ($evidenceDirectory) {
            New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
        }

        [IO.File]::WriteAllText($evidenceFile, "$json`n", $utf8)
        Write-Host "Issue #24 acceptance evidence: $evidenceFile"
    }
    else {
        [Console]::Out.WriteLine($json)
    }
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}
