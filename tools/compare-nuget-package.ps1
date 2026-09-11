#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Compares a served NuGet package with the immutable release candidate.

.DESCRIPTION
    Requires byte identity unless nuget.org added or replaced the package-signature entry. In that
    case every other ZIP entry must still have the same name, length, and SHA-256. This accounts
    for repository countersigning without allowing a changed payload to pass as the candidate.

.PARAMETER Candidate
    The exact .nupkg or .snupkg produced and retained by the release workflow.

.PARAMETER Served
    The package downloaded from nuget.org.

.PARAMETER EvidencePath
    Optional output path for the comparison evidence. JSON is written to stdout when omitted.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Candidate,

    [Parameter(Mandatory)]
    [string] $Served,

    [string] $EvidencePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-Sha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-ArchiveInventory([string] $Path) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)

    try {
        $seenNames = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        $entries = [Collections.Generic.Dictionary[string, object]]::new(
            [StringComparer]::Ordinal)
        $signature = $null
        $signatureCount = 0

        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName

            if (-not $seenNames.Add($name)) {
                throw "Package '$Path' contains duplicate ZIP entry '$name'."
            }

            $stream = $entry.Open()
            try {
                $hash = [Security.Cryptography.SHA256]::HashData($stream)
            }
            finally {
                $stream.Dispose()
            }

            $record = [ordered] @{
                length = $entry.Length
                sha256 = [Convert]::ToHexString($hash).ToLowerInvariant()
            }

            if ([string]::Equals(
                $name,
                '.signature.p7s',
                [StringComparison]::OrdinalIgnoreCase)) {
                $signatureCount++
                if ($signatureCount -ne 1) {
                    throw "Package '$Path' contains more than one .signature.p7s entry."
                }
                $signature = $record
            }
            else {
                $entries.Add($name, $record)
            }
        }

        return [ordered] @{
            entries = $entries
            signature = $signature
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Assert-PayloadEqual {
    param(
        [Parameter(Mandatory)] $CandidateInventory,
        [Parameter(Mandatory)] $ServedInventory
    )

    $candidateNames = [string[]] @($CandidateInventory.entries.Keys)
    $servedNames = [string[]] @($ServedInventory.entries.Keys)

    [Array]::Sort($candidateNames, [StringComparer]::Ordinal)
    [Array]::Sort($servedNames, [StringComparer]::Ordinal)

    if ($candidateNames.Count -ne $servedNames.Count) {
        throw (
            "Served package has $($servedNames.Count) payload entries; " +
            "candidate has $($candidateNames.Count)."
        )
    }

    for ($index = 0; $index -lt $candidateNames.Count; $index++) {
        if ($candidateNames[$index] -cne $servedNames[$index]) {
            throw (
                "Served payload entry '$($servedNames[$index])' differs from candidate entry " +
                "'$($candidateNames[$index])' at sorted index $index."
            )
        }

        $name = $candidateNames[$index]
        $candidateEntry = $CandidateInventory.entries[$name]
        $servedEntry = $ServedInventory.entries[$name]

        if (
            $candidateEntry.length -ne $servedEntry.length -or
            $candidateEntry.sha256 -cne $servedEntry.sha256
        ) {
            throw "Served payload entry '$name' differs from the immutable candidate."
        }
    }
}

$candidatePath = (Resolve-Path -LiteralPath $Candidate).Path
$servedPath = (Resolve-Path -LiteralPath $Served).Path
$candidateHash = Get-Sha256 $candidatePath
$servedHash = Get-Sha256 $servedPath
$candidateInventory = Get-ArchiveInventory $candidatePath
$servedInventory = Get-ArchiveInventory $servedPath
$comparison = 'byte-identical'

if ($candidateHash -cne $servedHash) {
    Assert-PayloadEqual $candidateInventory $servedInventory

    if ($null -eq $servedInventory.signature) {
        throw (
            'Package bytes differ even though payload entries agree, and the served package has ' +
            'no .signature.p7s entry to account for the difference.'
        )
    }

    $comparison = 'payload-identical-after-repository-signing'
}

$evidence = [ordered] @{
    candidate = [ordered] @{
        path = $candidatePath
        length = (Get-Item -LiteralPath $candidatePath).Length
        sha256 = $candidateHash
        signature = $candidateInventory.signature
    }
    served = [ordered] @{
        path = $servedPath
        length = (Get-Item -LiteralPath $servedPath).Length
        sha256 = $servedHash
        signature = $servedInventory.signature
    }
    payloadEntryCount = $candidateInventory.entries.Count
    comparison = $comparison
}

$json = $evidence | ConvertTo-Json -Depth 8

if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
    [Console]::Out.WriteLine($json)
}
else {
    $parent = Split-Path -Parent $EvidencePath
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        [IO.Directory]::CreateDirectory($parent) | Out-Null
    }

    [IO.File]::WriteAllText(
        $EvidencePath,
        $json + "`n",
        [Text.UTF8Encoding]::new($false)
    )
}
