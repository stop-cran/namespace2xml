#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Exercises every accepted and rejected served-package comparison class.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Package
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression.FileSystem

$comparator = Join-Path $PSScriptRoot 'compare-nuget-package.ps1'
$source = (Resolve-Path -LiteralPath $Package).Path
$root = Join-Path ([IO.Path]::GetTempPath()) "n2x-package-comparator-$([guid]::NewGuid().ToString('n'))"

function Invoke-Comparator {
    param(
        [Parameter(Mandatory)]
        [string] $Served,

        [Parameter(Mandatory)]
        [bool] $ShouldPass,

        [string] $Candidate = $source,

        [string] $FailurePattern
    )

    $output = & pwsh -NoProfile -File $comparator -Candidate $Candidate -Served $Served 2>&1
    $passed = $LASTEXITCODE -eq 0
    if ($passed -ne $ShouldPass) {
        throw "Unexpected comparator result for '$Served': $($output | Out-String)"
    }

    if (-not $ShouldPass -and $FailurePattern -and
        ($output | Out-String) -notmatch $FailurePattern) {
        throw (
            "The comparator rejected '$Served', but not through the expected oracle " +
            "'$FailurePattern': $($output | Out-String)"
        )
    }
}

try {
    [IO.Directory]::CreateDirectory($root) | Out-Null

    $exact = Join-Path $root 'served-exact.nupkg'
    $payloadMutation = Join-Path $root 'served-payload-mutation.nupkg'
    $repositorySigned = Join-Path $root 'served-repository-signed.nupkg'
    $rewrittenWithoutSignature = Join-Path $root 'served-rewritten-without-signature.nupkg'
    $duplicateEntry = Join-Path $root 'served-duplicate-entry.nupkg'
    $duplicateSignature = Join-Path $root 'served-duplicate-signature.nupkg'
    $caseDistinctCandidate = Join-Path $root 'case-distinct-candidate.nupkg'
    $caseDistinctServed = Join-Path $root 'case-distinct-served.nupkg'
    $malformed = Join-Path $root 'served-malformed.nupkg'

    Copy-Item -LiteralPath $source -Destination $exact
    Copy-Item -LiteralPath $source -Destination $payloadMutation
    Copy-Item -LiteralPath $source -Destination $repositorySigned
    Copy-Item -LiteralPath $source -Destination $rewrittenWithoutSignature
    Copy-Item -LiteralPath $source -Destination $duplicateEntry
    Copy-Item -LiteralPath $source -Destination $duplicateSignature
    Copy-Item -LiteralPath $source -Destination $caseDistinctCandidate
    Copy-Item -LiteralPath $source -Destination $caseDistinctServed

    Invoke-Comparator -Served $exact -ShouldPass $true

    $archive = [IO.Compression.ZipFile]::Open(
        $payloadMutation,
        [IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $archive.Entries |
            Where-Object { $_.FullName -cne '.signature.p7s' -and $_.Length -gt 0 } |
            Select-Object -First 1
        if ($null -eq $entry) {
            throw 'The package contains no nonempty payload entry to mutate.'
        }

        $name = $entry.FullName
        $stream = $entry.Open()
        try {
            $memory = [IO.MemoryStream]::new()
            $stream.CopyTo($memory)
            $bytes = $memory.ToArray()
        }
        finally {
            $stream.Dispose()
        }

        $bytes[0] = $bytes[0] -bxor 1
        $entry.Delete()
        $replacement = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
        $replacementStream = $replacement.Open()
        try {
            $replacementStream.Write($bytes, 0, $bytes.Length)
        }
        finally {
            $replacementStream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }

    Invoke-Comparator `
        -Served $payloadMutation `
        -ShouldPass $false `
        -FailurePattern 'differs from the immutable candidate'

    $archive = [IO.Compression.ZipFile]::Open(
        $repositorySigned,
        [IO.Compression.ZipArchiveMode]::Update)
    try {
        $existingSignature = $archive.GetEntry('.signature.p7s')
        if ($null -ne $existingSignature) {
            $existingSignature.Delete()
        }

        $signature = $archive.CreateEntry('.signature.p7s')
        $stream = $signature.Open()
        try {
            $bytes = [Text.Encoding]::UTF8.GetBytes('controlled repository signature')
            $stream.Write($bytes, 0, $bytes.Length)
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }

    Invoke-Comparator -Served $repositorySigned -ShouldPass $true

    $archive = [IO.Compression.ZipFile]::Open(
        $rewrittenWithoutSignature,
        [IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $archive.Entries |
            Where-Object { $_.FullName -cne '.signature.p7s' } |
            Select-Object -First 1
        if ($null -eq $entry) {
            throw 'The package contains no payload entry to rewrite.'
        }

        $entry.LastWriteTime = $entry.LastWriteTime.AddSeconds(2)
    }
    finally {
        $archive.Dispose()
    }

    Invoke-Comparator `
        -Served $rewrittenWithoutSignature `
        -ShouldPass $false `
        -FailurePattern 'no \.signature\.p7s entry'

    $archive = [IO.Compression.ZipFile]::Open(
        $duplicateEntry,
        [IO.Compression.ZipArchiveMode]::Update)
    try {
        $sourceEntry = $archive.Entries |
            Where-Object { $_.FullName -cne '.signature.p7s' } |
            Select-Object -First 1
        if ($null -eq $sourceEntry) {
            throw 'The package contains no payload entry to duplicate.'
        }

        $content = [IO.MemoryStream]::new()
        try {
            $sourceStream = $sourceEntry.Open()
            try {
                $sourceStream.CopyTo($content)
            }
            finally {
                $sourceStream.Dispose()
            }

            $duplicate = $archive.CreateEntry($sourceEntry.FullName)
            $duplicateStream = $duplicate.Open()
            try {
                $content.Position = 0
                $content.CopyTo($duplicateStream)
            }
            finally {
                $duplicateStream.Dispose()
            }
        }
        finally {
            $content.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }

    Invoke-Comparator `
        -Served $duplicateEntry `
        -ShouldPass $false `
        -FailurePattern 'duplicate ZIP entry'

    $archive = [IO.Compression.ZipFile]::Open(
        $duplicateSignature,
        [IO.Compression.ZipArchiveMode]::Update)
    try {
        foreach ($content in @('first signature', 'second signature')) {
            $signature = $archive.CreateEntry('.signature.p7s')
            $stream = $signature.Open()
            try {
                $bytes = [Text.Encoding]::UTF8.GetBytes($content)
                $stream.Write($bytes, 0, $bytes.Length)
            }
            finally {
                $stream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    Invoke-Comparator `
        -Served $duplicateSignature `
        -ShouldPass $false `
        -FailurePattern 'duplicate ZIP entry'

    foreach ($case in @(
        @{ Path = $caseDistinctCandidate; Signature = 'candidate signature' },
        @{ Path = $caseDistinctServed; Signature = 'served signature' }
    )) {
        $archive = [IO.Compression.ZipFile]::Open(
            $case.Path,
            [IO.Compression.ZipArchiveMode]::Update)
        try {
            foreach ($name in @('case-distinct.txt', 'Case-Distinct.txt')) {
                $entry = $archive.CreateEntry($name)
                $stream = $entry.Open()
                try {
                    $bytes = [Text.Encoding]::UTF8.GetBytes($name)
                    $stream.Write($bytes, 0, $bytes.Length)
                }
                finally {
                    $stream.Dispose()
                }
            }

            $signature = $archive.CreateEntry('.signature.p7s')
            $stream = $signature.Open()
            try {
                $bytes = [Text.Encoding]::UTF8.GetBytes($case.Signature)
                $stream.Write($bytes, 0, $bytes.Length)
            }
            finally {
                $stream.Dispose()
            }
        }
        finally {
            $archive.Dispose()
        }
    }

    Invoke-Comparator `
        -Candidate $caseDistinctCandidate `
        -Served $caseDistinctServed `
        -ShouldPass $true

    [IO.File]::WriteAllText($malformed, 'not a ZIP archive', [Text.UTF8Encoding]::new($false))
    Invoke-Comparator -Served $malformed -ShouldPass $false

    Write-Host (
        'Package comparator accepted exact, signature-only, and case-distinct payload cases ' +
        'and rejected every mutation.'
    )
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
