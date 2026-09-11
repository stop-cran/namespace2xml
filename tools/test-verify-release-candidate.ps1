#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Proves the retained release-candidate validator accepts only the sealed candidate.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Package,

    [Parameter(Mandatory)]
    [string] $SymbolPackage
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$sourcePackage = (Resolve-Path -LiteralPath $Package).Path
$sourceSymbols = (Resolve-Path -LiteralPath $SymbolPackage).Path
$version = '3.0.0'
$commit = '0123456789abcdef0123456789abcdef01234567'
$tag = 'v3.0.0'
$tagObject = '1111111111111111111111111111111111111111'
$tagTarget = $commit
$runId = '1001'
$ciRunId = '1002'
$ansibleRunId = '1003'
$fingerprint = 'CEF2B1528A812CC3D0BCBC32AD3E63ADB89CAE86'
$packageName = "namespace2xml.$version.nupkg"
$symbolName = "namespace2xml.$version.snupkg"
$root = Join-Path ([IO.Path]::GetTempPath()) (
    'namespace2xml-release-candidate-' + [guid]::NewGuid().ToString('n'))
$canonical = Join-Path $root 'canonical'
$validator = Join-Path $PSScriptRoot 'verify-release-candidate.ps1'
$utf8 = [Text.UTF8Encoding]::new($false)

function New-Case {
    param([Parameter(Mandatory)] [string] $Name)

    $path = Join-Path $root $Name
    Copy-Item -LiteralPath $canonical -Destination $path -Recurse
    return $path
}

function Invoke-Validator {
    param(
        [Parameter(Mandatory)]
        [string] $Directory,

        [Parameter(Mandatory)]
        [bool] $ShouldPass,

        [string] $FailurePattern
    )

    $output = & pwsh -NoProfile -File $validator `
        -CandidateDirectory $Directory `
        -Version $version `
        -Commit $commit `
        -Tag $tag `
        -TagObject $tagObject `
        -TagTarget $tagTarget `
        -RunId $runId `
        -CiRunId $ciRunId `
        -AnsibleRunId $ansibleRunId `
        -PackageName $packageName `
        -SymbolName $symbolName `
        -SigningFingerprint $fingerprint 2>&1
    $passed = $LASTEXITCODE -eq 0
    if ($passed -ne $ShouldPass) {
        throw (
            "Candidate case '$Directory' had unexpected result. Output: " +
            ($output | Out-String)
        )
    }
    if (-not $ShouldPass -and $FailurePattern -and
        ($output | Out-String) -notmatch $FailurePattern) {
        throw (
            "Candidate case '$Directory' failed outside oracle '$FailurePattern': " +
            ($output | Out-String)
        )
    }
}

function Write-CanonicalManifest {
    param([Parameter(Mandatory)] [string] $Directory)

    $lines = foreach ($name in @($packageName, $symbolName)) {
        $hash = (
            Get-FileHash -LiteralPath (Join-Path $Directory $name) -Algorithm SHA256
        ).Hash.ToLowerInvariant()
        "$hash  $name"
    }
    [IO.File]::WriteAllText(
        (Join-Path $Directory 'SHA256SUMS'),
        ($lines -join "`n") + "`n",
        $utf8)
}

try {
    [IO.Directory]::CreateDirectory($canonical) | Out-Null
    Copy-Item -LiteralPath $sourcePackage -Destination (Join-Path $canonical $packageName)
    Copy-Item -LiteralPath $sourceSymbols -Destination (Join-Path $canonical $symbolName)
    Write-CanonicalManifest -Directory $canonical

    $metadata = [ordered] @{
        version = $version
        commit = $commit
        tag = $tag
        tagObject = $tagObject
        tagTarget = $tagTarget
        runId = $runId
        initialAttempt = '1'
        ciRunId = $ciRunId
        ansibleRunId = $ansibleRunId
        packageName = $packageName
        symbolName = $symbolName
        signingFingerprint = $fingerprint
    }
    [IO.File]::WriteAllText(
        (Join-Path $canonical 'candidate-metadata.json'),
        ($metadata | ConvertTo-Json) + "`n",
        $utf8)

    $packageHash = (
        Get-FileHash -LiteralPath (Join-Path $canonical $packageName) -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    $issueEvidence = [ordered] @{
        acceptance = 'https://github.com/stop-cran/namespace2xml/issues/24'
        workflow = [ordered] @{ commit = $commit }
        package = [ordered] @{
            fileName = $packageName
            version = $version
            sha256 = $packageHash
        }
        result = [ordered] @{ completeByteOracle = 'passed' }
    }
    [IO.File]::WriteAllText(
        (Join-Path $canonical 'issue-24-evidence.json'),
        ($issueEvidence | ConvertTo-Json -Depth 4) + "`n",
        $utf8)

    Invoke-Validator -Directory $canonical -ShouldPass $true

    $missing = New-Case -Name 'missing'
    Remove-Item -LiteralPath (Join-Path $missing $symbolName)
    Invoke-Validator $missing $false 'exactly the candidate file set'

    $extra = New-Case -Name 'extra'
    [IO.File]::WriteAllText((Join-Path $extra 'extra.txt'), 'extra', $utf8)
    Invoke-Validator $extra $false 'exactly the candidate file set'

    $hiddenFile = New-Case -Name 'hidden-file'
    [IO.File]::WriteAllText((Join-Path $hiddenFile '.unexpected'), 'extra', $utf8)
    Invoke-Validator $hiddenFile $false 'exactly the candidate file set'

    $hiddenDirectory = New-Case -Name 'hidden-directory'
    [IO.Directory]::CreateDirectory((Join-Path $hiddenDirectory '.unexpected')) | Out-Null
    Invoke-Validator $hiddenDirectory $false 'exactly the candidate file set'

    $malformedManifest = New-Case -Name 'malformed-manifest'
    [IO.File]::WriteAllText(
        (Join-Path $malformedManifest 'SHA256SUMS'),
        "not-a-hash  $packageName`nnot-a-hash  $symbolName`n",
        $utf8)
    Invoke-Validator $malformedManifest $false 'Invalid SHA256SUMS line'

    $duplicateManifest = New-Case -Name 'duplicate-manifest'
    $hash = (
        Get-FileHash -LiteralPath (Join-Path $duplicateManifest $packageName) -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(
        (Join-Path $duplicateManifest 'SHA256SUMS'),
        "$hash  $packageName`n$hash  $packageName`n",
        $utf8)
    Invoke-Validator $duplicateManifest $false 'duplicate file'

    $hashMismatch = New-Case -Name 'hash-mismatch'
    [IO.File]::AppendAllText(
        (Join-Path $hashMismatch $packageName),
        'controlled mutation',
        $utf8)
    Invoke-Validator $hashMismatch $false 'hash mismatch'

    $metadataMismatch = New-Case -Name 'metadata-mismatch'
    $changedMetadata = Get-Content `
        -LiteralPath (Join-Path $metadataMismatch 'candidate-metadata.json') `
        -Raw |
        ConvertFrom-Json
    $changedMetadata.commit = 'ffffffffffffffffffffffffffffffffffffffff'
    [IO.File]::WriteAllText(
        (Join-Path $metadataMismatch 'candidate-metadata.json'),
        ($changedMetadata | ConvertTo-Json) + "`n",
        $utf8)
    Invoke-Validator $metadataMismatch $false 'Candidate metadata does not identify'

    $tagObjectMismatch = New-Case -Name 'tag-object-mismatch'
    $changedMetadata = Get-Content `
        -LiteralPath (Join-Path $tagObjectMismatch 'candidate-metadata.json') `
        -Raw |
        ConvertFrom-Json
    $changedMetadata.tagObject = 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee'
    [IO.File]::WriteAllText(
        (Join-Path $tagObjectMismatch 'candidate-metadata.json'),
        ($changedMetadata | ConvertTo-Json) + "`n",
        $utf8)
    Invoke-Validator $tagObjectMismatch $false 'Candidate metadata does not identify'

    $tagTargetMismatch = New-Case -Name 'tag-target-mismatch'
    $changedMetadata = Get-Content `
        -LiteralPath (Join-Path $tagTargetMismatch 'candidate-metadata.json') `
        -Raw |
        ConvertFrom-Json
    $changedMetadata.tagTarget = 'dddddddddddddddddddddddddddddddddddddddd'
    [IO.File]::WriteAllText(
        (Join-Path $tagTargetMismatch 'candidate-metadata.json'),
        ($changedMetadata | ConvertTo-Json) + "`n",
        $utf8)
    Invoke-Validator $tagTargetMismatch $false 'Candidate metadata does not identify'

    $evidenceMismatch = New-Case -Name 'evidence-mismatch'
    $changedEvidence = Get-Content `
        -LiteralPath (Join-Path $evidenceMismatch 'issue-24-evidence.json') `
        -Raw |
        ConvertFrom-Json
    $changedEvidence.result.completeByteOracle = 'failed'
    [IO.File]::WriteAllText(
        (Join-Path $evidenceMismatch 'issue-24-evidence.json'),
        ($changedEvidence | ConvertTo-Json -Depth 4) + "`n",
        $utf8)
    Invoke-Validator $evidenceMismatch $false 'Issue #24 evidence does not bind'

    Write-Host (
        'Release-candidate validator accepted the sealed candidate and rejected every ' +
        'controlled file, manifest, hash, metadata, and evidence mutation.'
    )
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
