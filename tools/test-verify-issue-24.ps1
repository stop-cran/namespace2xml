#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Proves the issue #24 package gate rejects a valid structural mutation.

.PARAMETER Package
    Exact .nupkg file, or a directory containing exactly one namespace2xml .nupkg.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Package
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$verifier = Join-Path $PSScriptRoot 'verify-issue-24.ps1'
$output = & pwsh -NoProfile -File $verifier -Package $Package -TestMutation AppenderIdentity 2>&1 |
    Out-String
$exitCode = $LASTEXITCODE

if ($exitCode -eq 0) {
    throw 'The issue #24 verifier accepted a mutated appender identity.'
}

if ($output -notmatch 'ISSUE24_MUTATION_APPLIED: AppenderIdentity') {
    throw "The expected failure occurred before the deliberate mutation was applied.`n$output"
}

Write-Host 'C7 mutation killed: the issue #24 verifier rejected the mutated appender identity.'
