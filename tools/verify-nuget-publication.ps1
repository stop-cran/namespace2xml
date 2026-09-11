#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Waits for nuget.org to serve the released package and symbols, then verifies their payloads.

.DESCRIPTION
    The release workflow passes the immutable candidate directory. The package downloaded from
    each public NuGet endpoint is compared by compare-nuget-package.ps1. Whole-file equality is
    preferred; a repository-signature-only difference is accepted only when every other ZIP entry
    is byte-identical.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $CandidateDirectory,

    [Parameter(Mandatory)]
    [string] $Version,

    [Parameter(Mandatory)]
    [string] $EvidenceDirectory,

    [ValidateRange(1, 120)]
    [int] $Attempts = 60,

    [ValidateRange(0, 300)]
    [int] $DelaySeconds = 20,

    [ValidateSet('package', 'symbols')]
    [string[]] $Artifacts = @('package', 'symbols'),

    [string] $PackageUrl,

    [string] $SymbolUrl
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$candidateRoot = (Resolve-Path -LiteralPath $CandidateDirectory).Path
[IO.Directory]::CreateDirectory($EvidenceDirectory) | Out-Null

$packageName = "namespace2xml.$Version.nupkg"
$symbolName = "namespace2xml.$Version.snupkg"
$packageCandidate = Join-Path $candidateRoot $packageName
$symbolCandidate = Join-Path $candidateRoot $symbolName

foreach ($path in @($packageCandidate, $symbolCandidate)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Immutable candidate file not found: $path"
    }
}

$lowerVersion = $Version.ToLowerInvariant()
if (-not $PackageUrl) {
    $PackageUrl =
        "https://api.nuget.org/v3-flatcontainer/namespace2xml/$lowerVersion/$packageName"
}
if (-not $SymbolUrl) {
    $SymbolUrl = "https://www.nuget.org/api/v2/symbolpackage/namespace2xml/$Version"
}

$allDownloads = @(
    [ordered] @{
        name = 'package'
        url = $PackageUrl
        candidate = $packageCandidate
        served = Join-Path $EvidenceDirectory $packageName
        evidence = Join-Path $EvidenceDirectory 'nuget-package-comparison.json'
    },
    [ordered] @{
        name = 'symbols'
        url = $SymbolUrl
        candidate = $symbolCandidate
        served = Join-Path $EvidenceDirectory $symbolName
        evidence = Join-Path $EvidenceDirectory 'nuget-symbol-comparison.json'
    }
)
$downloads = @(
    $allDownloads |
        Where-Object { $_.name -in $Artifacts })

foreach ($download in $downloads) {
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        Remove-Item -LiteralPath $download.served -Force -ErrorAction SilentlyContinue

        try {
            Invoke-WebRequest -Uri $download.url -OutFile $download.served -MaximumRedirection 10
            break
        }
        catch {
            $status = $null
            $responseProperty = $_.Exception.PSObject.Properties['Response']
            if ($null -ne $responseProperty -and $null -ne $responseProperty.Value) {
                $statusCodeProperty = $responseProperty.Value.PSObject.Properties['StatusCode']
                if ($null -ne $statusCodeProperty -and $null -ne $statusCodeProperty.Value) {
                    $status = [int] $statusCodeProperty.Value
                }
            }

            if ($null -eq $status) {
                throw (
                    "Public $($download.name) endpoint transport failure at " +
                    "'$($download.url)': $($_.Exception.Message)"
                )
            }

            if ($status -ne 404) {
                throw (
                    "Public $($download.name) endpoint returned HTTP $status at " +
                    "'$($download.url)'."
                )
            }

            if ($attempt -eq $Attempts) {
                throw (
                    "Public $($download.name) endpoint remained HTTP 404 after " +
                    "$Attempts attempts at '$($download.url)'."
                )
            }

            Write-Host (
                "$($download.name) is not served yet (attempt $attempt/$Attempts); " +
                "waiting $DelaySeconds seconds."
            )
            Start-Sleep -Seconds $DelaySeconds
        }
    }

    & (Join-Path $PSScriptRoot 'compare-nuget-package.ps1') `
        -Candidate $download.candidate `
        -Served $download.served `
        -EvidencePath $download.evidence
}

Write-Host (
    "nuget.org serves the requested $($Artifacts -join ' and ') payloads from the immutable " +
    "$Version candidate."
)
