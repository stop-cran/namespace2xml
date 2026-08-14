#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Regenerates spec/diagnostics.registry.json from docs/specification.md.

.DESCRIPTION
    The specification text governs the code-level facts it states directly: code, severity,
    condition, cardinality, and the Appendix B condition mapping. This script re-derives those
    from the specification so the registry can never silently drift from it.

    The registry additionally owns the per-code field sets, which the specification delegates to
    it (Section 22). Those are preserved from the existing registry rather than derived, so
    editing them is a deliberate registry change reviewed on its own merits.

    CI runs this script and fails when it produces a diff, which is the registry-drift gate.
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..'))
)

$ErrorActionPreference = 'Stop'

$specPath = Join-Path $RepositoryRoot 'docs/specification.md'
$registryPath = Join-Path $RepositoryRoot 'spec/diagnostics.registry.json'

$lines = [System.IO.File]::ReadAllLines($specPath)

$rows = [ordered]@{}
$inAppendixB = $false
$mappings = @{}

foreach ($line in $lines) {
    if ($line -match '^## Appendix B\.') { $inAppendixB = $true; continue }
    if ($line -match '^## Appendix C\.') { $inAppendixB = $false }

    if (-not $inAppendixB -and
        $line -match '^\|\s*`([A-Z]+[0-9]{3})`\s*\|\s*(error|warning)\s*\|\s*(.+?)\s*\|\s*(.+?)\s*\|\s*$') {
        $rows[$Matches[1]] = [ordered]@{
            severity    = $Matches[2]
            condition   = $Matches[3]
            cardinality = $Matches[4]
        }
    }

    if ($inAppendixB -and
        $line -match '^\|\s*(.+?)\s*\|\s*`([A-Z]+[0-9]{3})`\s*\|\s*$' -and
        $Matches[1] -ne 'Condition') {
        $code = $Matches[2]
        if (-not $mappings.ContainsKey($code)) { $mappings[$code] = @() }
        $mappings[$code] += $Matches[1]
    }
}

if ($rows.Count -eq 0) { throw "No diagnostic registry rows found in $specPath." }

$existingFields = @{}
if (Test-Path $registryPath) {
    $existing = Get-Content -LiteralPath $registryPath -Raw | ConvertFrom-Json
    foreach ($entry in $existing.codes) { $existingFields[$entry.code] = @($entry.fields) }
}

# Seed field sets used only when the registry does not already declare them. After the first
# generation the registry is the owner, so refining a set is an explicit registry edit.
$seedFields = @{
    CLI001       = @()
    PARSE001     = @('source', 'line', 'column')
    PARSE002     = @('source', 'line', 'column')
    SCHEME001    = @('source', 'line', 'column', 'path', 'declaration')
    SCHEME002    = @('source', 'line', 'column', 'path', 'declaration')
    WILDCARD001  = @('source', 'line', 'column', 'path', 'rule')
    WILDCARD002  = @('rule')
    REFERENCE001 = @('source', 'line', 'column', 'path')
    REFERENCE002 = @('source', 'line', 'column', 'path')
    REFERENCE003 = @('source', 'line', 'column', 'path')
    REFERENCE004 = @('source', 'line', 'column', 'path')
    REFERENCE005 = @('source', 'line', 'column', 'path')
    TYPE001      = @('source', 'line', 'column', 'path', 'declaration', 'destination')
    TYPE002      = @('source', 'path', 'destination')
    FLAT001      = @('path', 'destination')
    SHELL001     = @('path', 'destination')
    XML001       = @('source', 'line', 'column')
    XML002       = @('source', 'line', 'column', 'path')
    INI001       = @('path', 'destination')
    NAMESPACE001 = @('path', 'destination')
    COLLISION001 = @('declaration', 'destination')
    SERIALIZE001 = @('destination')
    PATH001      = @('declaration', 'destination')
    PATH002      = @('destination')
    LIMIT001     = @('source', 'line', 'column', 'path')
    WARN001      = @('source')
    WARN002      = @('source', 'line', 'column', 'declaration')
    WARN003      = @('source', 'destination')
    WARN004      = @('source', 'path')
    WARN005      = @('destination')
    WARN006      = @('source')
    WARN007      = @('source')
    WARN008      = @()
    WARN009      = @('source', 'line', 'column', 'path', 'declaration')
    WARN010      = @('source', 'path', 'destination')
    WARN011      = @('source', 'path')
    WARN012      = @('destination')
    WARN013      = @('path', 'destination')
}

$codes = foreach ($code in $rows.Keys) {
    $fields = @()
    if ($existingFields.ContainsKey($code) -and $null -ne $existingFields[$code]) {
        $fields = @($existingFields[$code])
    }
    elseif ($seedFields.ContainsKey($code)) {
        $fields = @($seedFields[$code])
    }

    [ordered]@{
        code        = $code
        severity    = $rows[$code].severity
        cardinality = $rows[$code].cardinality
        condition   = $rows[$code].condition
        fields      = @($fields)
        mappings    = @($mappings[$code])
    }
}

$specBytes = [System.IO.File]::ReadAllBytes($specPath)
$specHash = [System.BitConverter]::ToString(
    [System.Security.Cryptography.SHA256]::HashData($specBytes)).Replace('-', '').ToLowerInvariant()

$document = [ordered]@{
    '$schema'           = './diagnostics.registry.schema.json'
    specification       = [ordered]@{
        path   = 'docs/specification.md'
        sha256 = $specHash
    }
    authoritativeFor    = @('code', 'severity', 'cardinality', 'fields')
    notAuthoritativeFor = @('phase', 'spec', 'message')
    generatedBy         = 'tools/sync-diagnostics-registry.ps1'
    codes               = @($codes)
}

$json = ($document | ConvertTo-Json -Depth 8).Replace("`r`n", "`n")
if (-not $json.EndsWith("`n")) { $json += "`n" }

[System.IO.File]::WriteAllText($registryPath, $json, (New-Object System.Text.UTF8Encoding $false))

Write-Host "Wrote $($codes.Count) codes to $registryPath (specification sha256 $specHash)."

# Extract the normative diagnostic-stream schema of Section 6.4.3 so agents and the conformance
# harness can validate against a standalone file that cannot drift from the specification text.
$streamSchemaPath = Join-Path $RepositoryRoot 'spec/diagnostic-stream.schema.json'
$block = New-Object System.Collections.Generic.List[string]
$capturing = $false
$found = $false

foreach ($line in $lines) {
    if (-not $capturing -and $line -eq '```json') { $capturing = $true; $block.Clear(); continue }
    if ($capturing -and $line -eq '```') {
        $capturing = $false
        if ($block -join "`n" -match '"phase"\s*:\s*\{\s*"enum"\s*:\s*\[\s*"cli"') {
            $text = ($block -join "`n") + "`n"
            [System.IO.File]::WriteAllText($streamSchemaPath, $text, (New-Object System.Text.UTF8Encoding $false))
            $found = $true
            break
        }

        continue
    }

    if ($capturing) { $block.Add($line) }
}

if (-not $found) { throw 'Could not locate the Section 6.4.3 diagnostic-stream schema block.' }

Write-Host "Extracted the Section 6.4.3 stream schema to $streamSchemaPath."
