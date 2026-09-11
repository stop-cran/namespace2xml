#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Revalidates the immutable signed tag identity at a release write boundary.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Tag,

    [Parameter(Mandatory)]
    [string] $ExpectedTagObject,

    [Parameter(Mandatory)]
    [string] $ExpectedTagTarget,

    [Parameter(Mandatory)]
    [string] $SigningFingerprint
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Tag -notmatch '^v3\.[0-9]+\.[0-9]+$') {
    throw "Release tag '$Tag' is not a stable v3 semantic-version tag."
}
if ($ExpectedTagObject -notmatch '^[0-9a-f]{40}$' -or
    $ExpectedTagTarget -notmatch '^[0-9a-f]{40}$') {
    throw 'Expected tag object and target must be full lowercase SHA-1 object IDs.'
}
if ($SigningFingerprint -notmatch '^[0-9A-F]{40}$') {
    throw 'The signing fingerprint must be a full uppercase OpenPGP fingerprint.'
}

$tagRef = "refs/tags/$Tag"
& git fetch --force --no-tags origin "+${tagRef}:${tagRef}"
if ($LASTEXITCODE -ne 0) {
    throw "Failed to fetch the exact remote tag '$tagRef'."
}

$type = (& git cat-file -t $tagRef).Trim()
if ($LASTEXITCODE -ne 0 -or $type -cne 'tag') {
    throw "Release tag '$Tag' is absent or is not annotated."
}

$tagObject = (& git rev-parse $tagRef).Trim()
if ($LASTEXITCODE -ne 0 -or $tagObject -cne $ExpectedTagObject) {
    throw (
        "Release tag object changed: expected $ExpectedTagObject, observed $tagObject."
    )
}

$tagTarget = (& git rev-parse "$Tag^{commit}").Trim()
if ($LASTEXITCODE -ne 0 -or $tagTarget -cne $ExpectedTagTarget) {
    throw (
        "Release tag target changed: expected $ExpectedTagTarget, observed $tagTarget."
    )
}
if ($tagTarget -cne $env:GITHUB_SHA) {
    throw "Release tag target $tagTarget differs from workflow commit $env:GITHUB_SHA."
}

$verification = (& git verify-tag --raw -- $Tag 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) {
    throw "Release tag '$Tag' no longer has a valid signature.`n$verification"
}
$validSignature = [regex]::Escape("[GNUPG:] VALIDSIG $SigningFingerprint ")
if ($verification -notmatch $validSignature) {
    throw (
        "Release tag '$Tag' is not signed by $SigningFingerprint.`n$verification"
    )
}

Write-Host "Release tag identity remains $tagObject -> $tagTarget."
