#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Proves release-boundary tag revalidation detects remote tag mutation.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Join-Path ([IO.Path]::GetTempPath()) (
    'namespace2xml-release-tag-' + [guid]::NewGuid().ToString('n'))
$repository = Join-Path $root 'repository'
$remote = Join-Path $root 'origin.git'
$gnupg = Join-Path $root 'gnupg'
$validator = Join-Path $PSScriptRoot 'verify-release-tag.ps1'
$previousGnuPgHome = $env:GNUPGHOME
$previousGitHubSha = $env:GITHUB_SHA

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments)] [string[]] $Arguments)

    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed."
    }
}

function Invoke-Validator {
    param(
        [Parameter(Mandatory)]
        [string] $TagObject,

        [Parameter(Mandatory)]
        [string] $TagTarget,

        [Parameter(Mandatory)]
        [string] $Fingerprint,

        [Parameter(Mandatory)]
        [bool] $ShouldPass,

        [string] $FailurePattern
    )

    $output = & pwsh -NoProfile -File $validator `
        -Tag v3.0.0 `
        -ExpectedTagObject $TagObject `
        -ExpectedTagTarget $TagTarget `
        -SigningFingerprint $Fingerprint 2>&1 |
        Out-String
    $passed = $LASTEXITCODE -eq 0
    if ($passed -ne $ShouldPass) {
        throw "Unexpected tag-validator result.`n$output"
    }
    if (-not $ShouldPass -and $output -notmatch $FailurePattern) {
        throw "Tag validator failed outside '$FailurePattern'.`n$output"
    }
}

try {
    [IO.Directory]::CreateDirectory($root) | Out-Null
    [IO.Directory]::CreateDirectory($repository) | Out-Null
    [IO.Directory]::CreateDirectory($gnupg) | Out-Null
    $env:GNUPGHOME = $gnupg

    & gpg --batch --passphrase '' --quick-gen-key `
        'namespace2xml release test <release-test@namespace2xml.invalid>' `
        ed25519 sign 0
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to generate the temporary release-signing key.'
    }
    $fingerprint = (
        & gpg --batch --with-colons --fingerprint |
            Select-String '^fpr:' |
            Select-Object -First 1
    ).Line.Split(':')[9].ToUpperInvariant()
    $gpgProgram = (Get-Command gpg -ErrorAction Stop).Source

    Invoke-Git init --bare $remote
    Invoke-Git -C $repository init
    Invoke-Git -C $repository config user.name 'namespace2xml release test'
    Invoke-Git -C $repository config user.email 'release-test@namespace2xml.invalid'
    Invoke-Git -C $repository config user.signingkey $fingerprint
    Invoke-Git -C $repository config gpg.program $gpgProgram
    Invoke-Git -C $repository remote add origin $remote

    [IO.File]::WriteAllText(
        (Join-Path $repository 'payload.txt'),
        "candidate`n",
        [Text.UTF8Encoding]::new($false))
    Invoke-Git -C $repository add payload.txt
    Invoke-Git -C $repository commit -m candidate
    Invoke-Git -C $repository tag --sign v3.0.0 -m 'stable candidate'
    Invoke-Git -C $repository push origin HEAD
    Invoke-Git -C $repository push origin refs/tags/v3.0.0

    Push-Location $repository
    try {
        $tagTarget = (& git rev-parse 'v3.0.0^{commit}').Trim()
        $tagObject = (& git rev-parse refs/tags/v3.0.0).Trim()
        $env:GITHUB_SHA = $tagTarget
        Invoke-Validator $tagObject $tagTarget $fingerprint $true

        Invoke-Git tag --force --sign v3.0.0 -m 'mutated stable candidate' $tagTarget
        $mutatedObject = (& git rev-parse refs/tags/v3.0.0).Trim()
        if ($mutatedObject -ceq $tagObject) {
            throw 'The controlled tag-object mutation did not change the object ID.'
        }
        Invoke-Git push --force origin refs/tags/v3.0.0

        Invoke-Validator $tagObject $tagTarget $fingerprint $false 'tag object changed'
        Invoke-Validator $mutatedObject $tagTarget $fingerprint $true
        Invoke-Validator $mutatedObject ('2' * 40) $fingerprint $false 'tag target changed'
        Invoke-Validator $mutatedObject $tagTarget ('A' * 40) $false 'not signed by'
    }
    finally {
        Pop-Location
    }

    Write-Host (
        'Release-tag validator accepted the sealed signed identity and rejected tag-object, ' +
        'target, and signer mutations.'
    )
}
finally {
    $env:GNUPGHOME = $previousGnuPgHome
    $env:GITHUB_SHA = $previousGitHubSha
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
