# Validates the version 1 schema and historical example, not runtime report behavior.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$schema = Get-Content (Join-Path $root 'docs\publication-report.schema.json') -Raw
$example = Get-Content (Join-Path $root 'docs\publication-report-policy-refusal.example.json') -Raw
$checks = 0

function Copy-Example {
    ConvertFrom-Json $example -AsHashtable
}

function Assert-Candidate {
    param([string] $Name, [object] $Report, [bool] $Expected, [string] $SchemaText = $schema)

    $errors = @()
    $json = ConvertTo-Json $Report -Depth 30 -Compress
    $actual = Test-Json -Json $json -Schema $SchemaText -ErrorAction SilentlyContinue -ErrorVariable errors
    if ($actual -ne $Expected -or ($Expected -and $errors.Count -ne 0)) {
        throw "Candidate schema case '$Name': expected $Expected, got $actual. $($errors -join '; ')"
    }
    $script:checks++
}

$refused = Copy-Example
Assert-Candidate 'policy refusal' $refused $true
if ($example -cne ((ConvertTo-Json $refused -Depth 30 -Compress) + "`n")) {
    throw 'The complete example must be compact JSON with exactly one final LF.'
}
$payload = [Text.Encoding]::UTF8.GetBytes("{`n  `"x`": 1`n}`n")
$digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($payload)).ToLowerInvariant()
if ($refused.outputs[0].byteLength -ne $payload.Length -or $refused.outputs[0].sha256 -cne $digest) {
    throw 'The example does not describe the independently authored 13-byte JSON payload.'
}
$identity = $refused.contract
$bundleBytes = [Text.Encoding]::UTF8.GetBytes($identity.specificationSha256 + "`n" + $identity.registrySha256)
$bundleHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bundleBytes)).ToLowerInvariant()
if ($identity.hash -cne $bundleHash -or -not $identity.revision.EndsWith('+' + $bundleHash.Substring(0, 12), [StringComparison]::Ordinal)) {
    throw 'The example contract identity does not satisfy the proposed bundle-hash relationship.'
}

$empty = Copy-Example
$empty.outputs = @()
Assert-Candidate 'zero-output policy refusal' $empty $true
$empty.outcome = 'success'
$empty.exitCode = 0
Assert-Candidate 'zero-output success' $empty $true
$empty.outcome = 'validationFailed'
$empty.exitCode = 1
$empty.planComplete = $false
Assert-Candidate 'incomplete-plan validation failure' $empty $true

$published = Copy-Example
$published.outcome = 'success'
$published.exitCode = 0
$published.publicationOccurred = $true
$published.sideEffectsPossible = $true
$published.outputs[0].status = 'published'
$published.outputs[0].unattemptedReason = $null
$published.outputs[0].existedBefore = $false
$published.outputs[0].bytesDiffered = $true
Assert-Candidate 'new destination completed' $published $true
$published.outputs[0].existedBefore = $true
Assert-Candidate 'different existing bytes completed' $published $true
$published.outputs[0].status = 'unchanged'
$published.outputs[0].bytesDiffered = $false
Assert-Candidate 'equal-byte rewrite completed' $published $true

$failed = Copy-Example
$failed.outcome = 'publicationFailed'
$failed.outputs[0].status = 'failed'
$failed.outputs[0].failureCode = 'PATH002'
$failed.outputs[0].unattemptedReason = $null
Assert-Candidate 'first comparison failed without mutation' $failed $true
$failed.outputs[0].failureCode = 'PATH001'
Assert-Candidate 'confinement refusal preserves PATH001' $failed $true
$failed.outputs[0].failureCode = 'PATH002'
$failed.sideEffectsPossible = $true
Assert-Candidate 'first write failed with possible partial side effects' $failed $true

$partial = Copy-Example
$partial.outcome = 'publicationFailed'
$partial.publicationOccurred = $true
$partial.sideEffectsPossible = $true
$later = (Copy-Example).outputs[0]
$later.destination = 'c.json'
$later.unattemptedReason = 'previousPublicationFailure'
$failed.outputs[0].destination = 'b.json'
$partial.outputs = @($published.outputs[0], $failed.outputs[0], $later)
Assert-Candidate 'completed then failed then unattempted' $partial $true

$invalidCases = [ordered]@{
    'unknown envelope field' = { param($r) $r.extra = 1 }
    'unknown contract field' = { param($r) $r.contract.extra = 1 }
    'unknown output field' = { param($r) $r.outputs[0].extra = 1 }
    'unknown contribution field' = { param($r) $r.outputs[0].contributions[0].extra = 1 }
    'missing identity' = { param($r) $r.Remove('contract') | Out-Null }
    'unsupported version' = { param($r) $r.schemaVersion = 2 }
    'fractional length' = { param($r) $r.outputs[0].byteLength = 1.5 }
    'negative length' = { param($r) $r.outputs[0].byteLength = -1 }
    'signed 64-bit overflow' = { param($r) $r.outputs[0].byteLength = [decimal]'9223372036854775808' }
    'zero source index' = { param($r) $r.outputs[0].contributions[0].sourceIndex = 0 }
    'uppercase digest' = { param($r) $r.outputs[0].sha256 = $r.outputs[0].sha256.ToUpperInvariant() }
    'unknown format' = { param($r) $r.outputs[0].format = 'ignore' }
    'unrelated failure code' = {
        param($r)
        $r.outcome = 'publicationFailed'
        $r.outputs[0].status = 'failed'
        $r.outputs[0].failureCode = 'REPORT001'
        $r.outputs[0].unattemptedReason = $null
    }
    'fifth output status' = {
        param($r)
        $r.outcome = 'publicationFailed'
        $first = (Copy-Example).outputs[0]
        $first.destination = 'b.json'
        $first.status = 'failed'
        $first.failureCode = 'PATH002'
        $first.unattemptedReason = $null
        $r.outputs[0].status = 'deleted'
        $r.outputs[0].unattemptedReason = $null
        $r.outputs = @($first, $r.outputs[0])
    }
    'traversal destination' = { param($r) $r.outputs[0].destination = '../a.json' }
    'absolute destination' = { param($r) $r.outputs[0].destination = '/a.json' }
    'policy refusal with publication' = { param($r) $r.publicationOccurred = $true }
    'policy refusal with possible mutation' = { param($r) $r.sideEffectsPossible = $true }
    'policy refusal with observations' = { param($r) $r.outputs[0].existedBefore = $true }
    'policy refusal with wrong reason' = { param($r) $r.outputs[0].unattemptedReason = 'previousPublicationFailure' }
    'success with refused output' = { param($r) $r.outcome = 'success'; $r.exitCode = 0 }
    'incomplete plan with entries' = { param($r) $r.outcome = 'validationFailed'; $r.planComplete = $false }
    'publication failure without failed action' = { param($r) $r.outcome = 'publicationFailed' }
}
foreach ($case in $invalidCases.GetEnumerator()) {
    $report = Copy-Example
    & $case.Value $report
    Assert-Candidate $case.Key $report $false
}

# C7: an actual schema defect must make its rejection test go red.
$mutant = ConvertFrom-Json $schema -AsHashtable
$mutant.'$defs'.output.properties.status.enum += 'deleted'
$mutantText = ConvertTo-Json $mutant -Depth 40 -Compress
$deleted = Copy-Example
& $invalidCases['fifth output status'] $deleted
$killed = $false
try {
    Assert-Candidate 'fifth output status' $deleted $false $mutantText
}
catch {
    if (-not $_.Exception.Message.StartsWith("Candidate schema case 'fifth output status': expected False, got True.", [StringComparison]::Ordinal)) {
        throw
    }
    $killed = $true
}
if (-not $killed) {
    throw 'C7 proof failed: admitting a fifth status did not break its rejection test.'
}

$mutant = ConvertFrom-Json $schema -AsHashtable
$mutant.'$defs'.output.allOf[2].then.properties.failureCode.enum = @('PATH002')
$mutantText = ConvertTo-Json $mutant -Depth 40 -Compress
$refusal = Copy-Example
$refusal.outcome = 'publicationFailed'
$refusal.outputs[0].status = 'failed'
$refusal.outputs[0].failureCode = 'PATH001'
$refusal.outputs[0].unattemptedReason = $null
$killed = $false
try {
    Assert-Candidate 'confinement refusal preserves PATH001' $refusal $true $mutantText
}
catch {
    if (-not $_.Exception.Message.StartsWith("Candidate schema case 'confinement refusal preserves PATH001': expected True, got False.", [StringComparison]::Ordinal)) {
        throw
    }
    $killed = $true
}
if (-not $killed) {
    throw 'C7 proof failed: rejecting PATH001 did not break the confinement refusal test.'
}

Write-Output "$checks candidate schema checks passed; the fifth-status and PATH001 schema mutants were killed. No runtime behavior was tested."
