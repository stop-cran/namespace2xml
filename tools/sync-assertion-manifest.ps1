#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Regenerates conformance/assertions.json from Section 26 of docs/specification.md.

.DESCRIPTION
    Several Section 26 items bundle many independent behaviours into one sentence, so item count
    is a poor measure of coverage. The manifest keeps the numbered items in sync with the
    specification and carries the decomposition into individually testable assertions.

    Item text, and the item set, are derived from the specification and must never be edited by
    hand. Milestone ownership, coverage status, and decomposed assertions are authored and are
    preserved across regeneration.

    CI runs this script and fails on a diff.
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..'))
)

$ErrorActionPreference = 'Stop'

$specPath = Join-Path $RepositoryRoot 'docs/specification.md'
$manifestPath = Join-Path $RepositoryRoot 'conformance/assertions.json'

$lines = [System.IO.File]::ReadAllLines($specPath)

$items = @{}
$inSection26 = $false
$current = $null

foreach ($line in $lines) {
    if ($line -match '^## 26\. Acceptance requirements') { $inSection26 = $true; continue }
    if ($inSection26 -and $line -match '^## ') { break }
    if (-not $inSection26) { continue }

    if ($line -match '^([0-9]+)\.\s+(.+)$') {
        $current = $Matches[1]
        $items[$current] = $Matches[2].Trim()
    }
    elseif ($null -ne $current -and $line -match '^\s+\S') {
        $items[$current] = ($items[$current] + ' ' + $line.Trim())
    }
    elseif ($line.Trim().Length -eq 0) {
        $current = $null
    }
}

if ($items.Count -eq 0) { throw "No Section 26 items found in $specPath." }

$authored = @{}
if (Test-Path $manifestPath) {
    $previous = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    foreach ($entry in $previous.items) { $authored[[int]$entry.item] = $entry }
}

$rendered = foreach ($number in ($items.Keys | Sort-Object { [int]$_ })) {
    $prior = $authored[[int]$number]

    $milestone = 'unassigned'
    $status = 'pending'
    $assertions = @()
    $fixtures = @()
    $gates = @()
    $whyNotAFixture = $null

    if ($null -ne $prior) {
        if ($prior.milestone) { $milestone = $prior.milestone }
        if ($prior.status) { $status = $prior.status }
        if ($prior.assertions) { $assertions = @($prior.assertions) }
        if ($prior.fixtures) { $fixtures = @($prior.fixtures) }
        if ($prior.gates) { $gates = @($prior.gates) }
        if ($prior.whyNotAFixture) { $whyNotAFixture = [string]$prior.whyNotAFixture }
    }

    $entry = [ordered]@{
        item       = [int]$number
        text       = $items[$number]
        milestone  = $milestone
        status     = $status
        assertions = @($assertions)
        fixtures   = @($fixtures)
    }

    # Appendix C.5: an item a fixture cannot discharge names gates instead.
    if ($gates.Count -gt 0) {
        $entry['gates'] = @($gates)
    }

    # An item with no fixture argues why — whether it is discharged by gates or not discharged yet.
    # Restricting this to gate-bearing items hid the uncovered ones, which are the ones worth seeing.
    if ($fixtures.Count -eq 0 -and $whyNotAFixture) {
        $entry['whyNotAFixture'] = $whyNotAFixture
    }

    $entry
}

$document = [ordered]@{
    '$comment'  = 'Item numbers and text are derived from Section 26 of docs/specification.md. Do not edit them by hand; run tools/sync-assertion-manifest.ps1.'
    generatedBy = 'tools/sync-assertion-manifest.ps1'
    statuses    = [ordered]@{
        pending  = 'Not yet owned by a merged milestone. Not enforced by the traceability gate.'
        required = 'Appendix C.5: the fixtures field must name exactly the fixtures that reference this item, and each must assert more than an exit code. Enforced by the traceability gate.'
    }
    gates       = 'Appendix C.5: an item no fixture can discharge names the test or CI job that checks it instead, and says why a fixture cannot. Every name must resolve to something that exists, or the field is an accounting fiction.'
    items       = @($rendered)
}

$json = ($document | ConvertTo-Json -Depth 8).Replace("`r`n", "`n")
if (-not $json.EndsWith("`n")) { $json += "`n" }

[System.IO.File]::WriteAllText($manifestPath, $json, (New-Object System.Text.UTF8Encoding $false))

$required = @($rendered | Where-Object { $_.status -eq 'required' }).Count
Write-Host "Wrote $($rendered.Count) acceptance items to $manifestPath ($required required)."
