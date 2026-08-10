<#
.SYNOPSIS
    Asserts that every issue link in KNOWN-LIMITS.md has the state its annotation claims.

.DESCRIPTION
    KNOWN-LIMITS.md records what is *not* verified, so nothing in the ordinary verification loop
    re-checks its claims and an entry can outlive the defect it describes. That happened: the entry
    now numbered 1.9 was published in 3.0.0-preview.2 asserting a defect that had already been
    fixed, and no gate noticed.

    An entry almost always stops being true at the moment the issue that owns it is closed, so the
    state of the linked issues is a cheap proxy for the state of the file. This script enforces one
    rule, in both directions:

      * a link written plainly, as [#59](...), must name an OPEN issue -- the entry owes a
        resolution, so its issue must still own one;
      * a link written [#58 (closed)](...) must name a CLOSED issue -- the annotation is a claim
        about history, and a reopened issue makes it false.

    The annotation is what lets a *(resolved)* entry keep pointing at the work that resolved it,
    and it tells a reader which links are live work without following any of them.

.PARAMETER RepositoryRoot
    Repository root. Defaults to the parent of the directory holding this script.

.PARAMETER Repository
    The GitHub repository to query, as owner/name.

.PARAMETER RequireGh
    Fail when the GitHub CLI is unavailable or unauthenticated, instead of skipping. CI passes this
    so that a missing credential cannot turn the gate into a silent no-op.
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $Repository = 'stop-cran/namespace2xml',
    [switch] $RequireGh
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$limitsPath = Join-Path $RepositoryRoot 'KNOWN-LIMITS.md'
if (-not (Test-Path -LiteralPath $limitsPath)) {
    Write-Error "KNOWN-LIMITS.md not found at $limitsPath"
    exit 1
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($null -eq $gh) {
    if ($RequireGh) {
        Write-Error 'The GitHub CLI is required for this check and was not found on PATH.'
        exit 1
    }
    Write-Host 'gh not found; skipping the KNOWN-LIMITS.md issue-state check.'
    exit 0
}

$text = [IO.File]::ReadAllText($limitsPath)

# [#59](https://github.com/owner/name/issues/59) or [#58 (closed)](...same...). The link text and
# the URL must agree on the number, which also catches a copy-edit that renumbered only one of them.
$pattern = '\[#(?<label>\d+)(?<annotation>\s+\(closed\))?\]\(https://github\.com/' +
    [regex]::Escape($Repository) + '/issues/(?<number>\d+)\)'

$links = [regex]::Matches($text, $pattern)
if ($links.Count -eq 0) {
    Write-Error 'No issue links found in KNOWN-LIMITS.md. The check would pass vacuously; verify the link syntax.'
    exit 1
}

$failures = [System.Collections.Generic.List[string]]::new()
# A typed dictionary rather than [ordered]@{}: an OrderedDictionary indexed by an integer resolves
# the integer as a position, not as a key, and silently reads the wrong entry.
$expectations = [System.Collections.Generic.Dictionary[int, bool]]::new()

foreach ($link in $links) {
    $label = [int] $link.Groups['label'].Value
    $number = [int] $link.Groups['number'].Value
    if ($label -ne $number) {
        $failures.Add("Link text #$label points at issue $number.")
        continue
    }

    $expectClosed = $link.Groups['annotation'].Success
    if ($expectations.ContainsKey($number)) {
        if ($expectations[$number] -ne $expectClosed) {
            $failures.Add("Issue #$number is annotated inconsistently: both plainly and as (closed).")
        }
        continue
    }

    $expectations[$number] = $expectClosed
}

# Every distinct issue is queried once, so an entry cited twice costs one request rather than two.
foreach ($number in ($expectations.Keys | Sort-Object)) {
    $expectClosed = $expectations[$number]

    $raw = gh issue view $number --repo $Repository --json number,state,title 2>&1
    if ($LASTEXITCODE -ne 0) {
        $message = ($raw | Out-String).Trim()
        if (-not $RequireGh -and $message -match 'authentication|gh auth login|HTTP 401') {
            Write-Host 'gh is not authenticated; skipping the KNOWN-LIMITS.md issue-state check.'
            exit 0
        }

        $failures.Add("Could not read issue #${number}: $message")
        continue
    }

    $issue = $raw | ConvertFrom-Json
    $isClosed = $issue.state -eq 'CLOSED'

    if ($expectClosed -and -not $isClosed) {
        $failures.Add(
            "#$number is annotated (closed) but is $($issue.state). " +
            'Reopening it means the entry citing it owes a resolution again: drop the annotation. ' +
            "($($issue.title))")
    }
    elseif (-not $expectClosed -and $isClosed) {
        $failures.Add(
            "#$number is cited as live work but is CLOSED. " +
            'Re-verify the entry against the current build: delete it if the limit is gone, mark ' +
            'the heading *(resolved)* and annotate the link (closed) if a released build published ' +
            'the claim, or file the issue that owns what remains. ' +
            "($($issue.title))")
    }
}

if ($failures.Count -gt 0) {
    Write-Host "KNOWN-LIMITS.md issue-state check failed:`n"
    foreach ($failure in $failures) {
        Write-Host "  - $failure"
    }
    Write-Host ''
    exit 1
}

Write-Host "KNOWN-LIMITS.md issue-state check passed for $($expectations.Count) issue(s)."
