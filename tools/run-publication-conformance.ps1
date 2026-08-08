<#
.SYNOPSIS
    Runs the conformance cases that assert Section 21 publication invariants, first and alone.

.DESCRIPTION
    Section 21 governs paths a successful run never takes. An implementation that has silently
    lost output-root confinement, the validation gate, or the refusal to follow a link still
    produces a byte-identical tree for every fixture that does not attack it, so the whole rest of
    the suite stays green. These cases are therefore run on their own, before the main suite, so
    that a regression in them is the first thing that fails rather than one red line among four
    hundred.

    Cases are selected by the acceptance items they claim in their own requirements.txt rather
    than by a hand-maintained list here, so a new confinement fixture joins this job by claiming
    item 29 and nothing needs to be remembered.

    The selected cases are verified by name against the tests the filter actually matched. A test
    filter that matches nothing exits zero and reports success, which would turn this gate into a
    no-op precisely when a fixture is renamed -- the failure mode a first-failing job exists to
    avoid.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [int[]] $Items = @(29, 30)
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = Split-Path -Parent $PSScriptRoot
$corpus = Join-Path $repository 'conformance'

$selected = [System.Collections.Generic.List[string]]::new()

foreach ($directory in Get-ChildItem -Path $corpus -Directory | Sort-Object -Property Name -CaseSensitive) {
    $requirements = Join-Path $directory.FullName 'requirements.txt'

    if (-not (Test-Path -LiteralPath $requirements)) {
        continue
    }

    $claimed = Get-Content -LiteralPath $requirements |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -ne '' } |
        ForEach-Object { [int] $_ }

    if (@($claimed | Where-Object { $Items -contains $_ }).Count -gt 0) {
        $selected.Add($directory.Name)
    }
}

if ($selected.Count -eq 0) {
    Write-Error "no conformance case claims any of acceptance items $($Items -join ', '); the publication invariants have no coverage."
    exit 1
}

Write-Host "Section 21 cases selected by acceptance items $($Items -join ', '):"
foreach ($name in $selected) {
    Write-Host "  $name"
}

$filter = ($selected | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'

Push-Location $repository

try {
    # Verify by name rather than by count. Each case contributes several test methods, so a total
    # says little, and the failure this guards against is a renamed or deleted fixture silently
    # dropping out of the filter -- which a count comparison catches only when it happens to change
    # the total. Listing the matched tests and checking that every selected case appears is exact.
    $listed = & dotnet test (Join-Path 'tests' 'Namespace2Xml.Conformance') `
        --no-build -c $Configuration --filter $filter --list-tests 2>&1 | Out-String

    $missing = @($selected | Where-Object { $listed -notmatch [regex]::Escape($_) })

    if ($missing.Count -gt 0) {
        Write-Error "the test filter did not match these Section 21 cases: $($missing -join ', ')"
        exit 1
    }

    & dotnet test (Join-Path 'tests' 'Namespace2Xml.Conformance') `
        --no-build -c $Configuration --filter $filter

    if ($LASTEXITCODE -ne 0) {
        Write-Error 'a Section 21 conformance case failed.'
        exit 1
    }

    Write-Host "all $($selected.Count) Section 21 conformance cases passed."
}
finally {
    Pop-Location
}
