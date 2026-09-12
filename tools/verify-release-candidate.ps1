#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Verifies the exact retained artifact used to reconcile a stable release.

.DESCRIPTION
    Fails closed unless the candidate directory contains exactly the two NuGet artifacts,
    their manifest, source metadata, and issue #24 acceptance evidence. Every identity is
    checked against the signed-tag workflow inputs before publication can resume.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $CandidateDirectory,

    [Parameter(Mandatory)]
    [string] $Version,

    [Parameter(Mandatory)]
    [string] $Commit,

    [Parameter(Mandatory)]
    [string] $Tag,

    [Parameter(Mandatory)]
    [string] $TagObject,

    [Parameter(Mandatory)]
    [string] $TagTarget,

    [Parameter(Mandatory)]
    [string] $RunId,

    [Parameter(Mandatory)]
    [int] $CurrentAttempt,

    [Parameter(Mandatory)]
    [string] $CiRunId,

    [Parameter(Mandatory)]
    [string] $AnsibleRunId,

    [Parameter(Mandatory)]
    [string] $PackageName,

    [Parameter(Mandatory)]
    [string] $SymbolName,

    [Parameter(Mandatory)]
    [string] $SigningFingerprint
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-PropertySet {
    param(
        [Parameter(Mandatory)]
        [object] $Value,

        [Parameter(Mandatory)]
        [string[]] $Expected,

        [Parameter(Mandatory)]
        [string] $Label
    )

    $actual = [string[]] @($Value.PSObject.Properties.Name | Sort-Object)
    $expectedSorted = [string[]] @($Expected | Sort-Object)
    if ([string]::Join("`n", $actual) -cne [string]::Join("`n", $expectedSorted)) {
        throw (
            "$Label does not contain exactly the expected properties. " +
            "Expected: $($expectedSorted -join ', '). Actual: $($actual -join ', ')."
        )
    }
}

$candidateRoot = (Resolve-Path -LiteralPath $CandidateDirectory).Path
$expectedFiles = [string[]] @(
    $PackageName,
    $SymbolName,
    'SHA256SUMS',
    'candidate-metadata.json',
    'issue-24-evidence.json')
$actualFiles = [string[]] @(
    Get-ChildItem -LiteralPath $candidateRoot -Force |
        Sort-Object -Property Name |
        ForEach-Object { $_.Name })
$expectedSorted = [string[]] @($expectedFiles | Sort-Object)
if ([string]::Join("`n", $actualFiles) -cne
    [string]::Join("`n", $expectedSorted)) {
    throw (
        'The retained artifact does not contain exactly the candidate file set. ' +
        "Expected: $($expectedSorted -join ', '). Actual: $($actualFiles -join ', ')."
    )
}

$manifestPath = Join-Path $candidateRoot 'SHA256SUMS'
$lines = [string[]] @(Get-Content -LiteralPath $manifestPath)
if ($lines.Count -ne 2) {
    throw "SHA256SUMS must contain exactly two entries; found $($lines.Count)."
}

$expectedManifestNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
[void] $expectedManifestNames.Add($PackageName)
[void] $expectedManifestNames.Add($SymbolName)
$seenManifestNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$manifestHashes = [Collections.Generic.Dictionary[string, string]]::new(
    [StringComparer]::Ordinal)

foreach ($line in $lines) {
    if ($line -notmatch '^(?<hash>[0-9a-f]{64})  (?<name>[^/\\]+)$') {
        throw "Invalid SHA256SUMS line: $line"
    }

    $name = $Matches.name
    $hash = $Matches.hash
    if (-not $expectedManifestNames.Contains($name)) {
        throw "SHA256SUMS contains unexpected file '$name'."
    }
    if (-not $seenManifestNames.Add($name)) {
        throw "SHA256SUMS contains duplicate file '$name'."
    }

    $actual = (
        Get-FileHash -LiteralPath (Join-Path $candidateRoot $name) -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    if ($actual -cne $hash) {
        throw "Immutable candidate hash mismatch for $name."
    }

    $manifestHashes.Add($name, $hash)
}

if (-not $seenManifestNames.SetEquals($expectedManifestNames)) {
    throw 'SHA256SUMS does not bind both expected candidate artifacts.'
}

$metadataPath = Join-Path $candidateRoot 'candidate-metadata.json'
$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
Assert-PropertySet -Value $metadata -Label 'Candidate metadata' -Expected @(
    'version',
    'commit',
    'tag',
    'tagObject',
    'tagTarget',
    'runId',
    'initialAttempt',
    'ciRunId',
    'ansibleRunId',
    'packageName',
    'symbolName',
    'signingFingerprint')

$constructionAttempt = [int] $metadata.initialAttempt
if ($metadata.commit -cne $Commit -or
    $metadata.runId -cne $RunId -or
    $metadata.version -cne $Version -or
    $metadata.ciRunId -cne $CiRunId -or
    $metadata.ansibleRunId -cne $AnsibleRunId -or
    $metadata.tag -cne $Tag -or
    $metadata.tagObject -cne $TagObject -or
    $metadata.tagTarget -cne $TagTarget -or
    $metadata.tagTarget -cne $Commit -or
    $metadata.packageName -cne $PackageName -or
    $metadata.symbolName -cne $SymbolName -or
    $metadata.signingFingerprint -cne $SigningFingerprint -or
    $constructionAttempt -lt 1 -or
    $constructionAttempt -gt $CurrentAttempt) {
    throw (
        'Candidate metadata does not identify this source, signed tag, package set, ' +
        'construction attempt, and required workflow runs.'
    )
}

$issueEvidencePath = Join-Path $candidateRoot 'issue-24-evidence.json'
$issueEvidence = Get-Content -LiteralPath $issueEvidencePath -Raw | ConvertFrom-Json
if ($issueEvidence.acceptance -cne
        'https://github.com/stop-cran/namespace2xml/issues/24' -or
    $issueEvidence.package.fileName -cne $PackageName -or
    $issueEvidence.package.version -cne $Version -or
    $issueEvidence.package.sha256 -cne $manifestHashes[$PackageName] -or
    $issueEvidence.workflow.commit -cne $Commit -or
    $issueEvidence.result.completeByteOracle -cne 'passed') {
    throw 'Issue #24 evidence does not bind this candidate package and source commit.'
}

Write-Host "Retained release candidate is complete and bound to $Tag at $Commit."
