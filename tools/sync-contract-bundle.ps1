#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Recomputes spec/contract-bundle.json from the artifacts it covers.

.DESCRIPTION
    Specification Section 22 defines the contract bundle as the specification text plus the
    machine-readable diagnostic registry, carrying one revision identifier that must change
    whenever either artifact changes. The binary reports that identifier from --version, so a
    consumer can tell exactly which contract a given build implements.

    This script recomputes both hashes and bumps the revision counter when either has moved.
    CI runs it and fails on a diff, which makes an unversioned contract change impossible.
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..'))
)

$ErrorActionPreference = 'Stop'

$bundlePath = Join-Path $RepositoryRoot 'spec/contract-bundle.json'

function Get-Sha256([string] $relativePath) {
    $bytes = [System.IO.File]::ReadAllBytes((Join-Path $RepositoryRoot $relativePath))
    return [System.BitConverter]::ToString(
        [System.Security.Cryptography.SHA256]::HashData($bytes)).Replace('-', '').ToLowerInvariant()
}

$specHash = Get-Sha256 'docs/specification.md'
$registryHash = Get-Sha256 'spec/diagnostics.registry.json'

$counter = 0
$previousSpec = $null
$previousRegistry = $null

if (Test-Path $bundlePath) {
    $previous = Get-Content -LiteralPath $bundlePath -Raw | ConvertFrom-Json
    $counter = [int]$previous.revisionCounter
    $previousSpec = $previous.specification.sha256
    $previousRegistry = $previous.registry.sha256
}

if ($previousSpec -ne $specHash -or $previousRegistry -ne $registryHash) {
    $counter++
}

if ($counter -lt 1) { $counter = 1 }

# The revision is short enough to paste into a bug report and long enough to be unambiguous.
$combined = [System.Text.Encoding]::UTF8.GetBytes("$specHash`n$registryHash")
$digest = [System.BitConverter]::ToString(
    [System.Security.Cryptography.SHA256]::HashData($combined)).Replace('-', '').ToLowerInvariant()

$document = [ordered]@{
    revision        = "r$counter+$($digest.Substring(0, 12))"
    revisionCounter = $counter
    specification   = [ordered]@{ path = 'docs/specification.md'; sha256 = $specHash }
    registry        = [ordered]@{ path = 'spec/diagnostics.registry.json'; sha256 = $registryHash }
    generatedBy     = 'tools/sync-contract-bundle.ps1'
}

$json = ($document | ConvertTo-Json -Depth 5).Replace("`r`n", "`n")
if (-not $json.EndsWith("`n")) { $json += "`n" }

[System.IO.File]::WriteAllText($bundlePath, $json, (New-Object System.Text.UTF8Encoding $false))

Write-Host "Contract bundle revision $($document.revision)."
