<#
.SYNOPSIS
    Verify that the symbols published for a release can actually reach their source.

.DESCRIPTION
    The release workflow asserts that the symbol package exists and contains a .pdb. That is
    presence, not function: a .pdb full of paths nobody can resolve satisfies it. Debug symbols
    are only worth shipping if the path from a stack trace back to source is real, so this script
    walks that path and refuses to assume any part of it.

    For each portable PDB in the published symbol package it reads the SourceLink document map and
    every document the compiler recorded, then settles each document one of two ways:

      * the document carries embedded source in the PDB, in which case no fetch is needed; or
      * the document resolves through the SourceLink map to a URL, which must answer 200 and must
        return bytes whose hash equals the checksum the compiler wrote down.

    The checksum comparison is the point. A URL that answers 200 proves only that something is
    there; comparing against the compiler's own record proves the bytes served are the bytes that
    were built, which is the claim a debugger relies on.

    Every document must be settled one way or the other, and at least one must be settled by fetch,
    so that a package which embedded everything - or one whose document table is empty - cannot
    pass by having nothing to check.

.PARAMETER Version
    The published version to verify, without a leading "v". Defaults to the newest v3.* tag.

.PARAMETER PackageId
    The NuGet package identifier. Defaults to namespace2xml.

.PARAMETER SymbolSource
    Base address symbol packages are downloaded from.

.PARAMETER SymbolPackage
    A local .snupkg to verify instead of downloading one. Use this to check a package before it is
    published; its SourceLink commit must already be pushed, or every fetch will answer 404.

.NOTES
    One request is made per source document, which for this package is a burst of well over a
    hundred. An unauthenticated host answers some of them with 429 once the burst is long enough,
    and reporting that as "cannot be traced back to source" would accuse a perfectly good package.
    Transient statuses are therefore retried with backoff, and a 404 - which is a real answer, not
    a refusal to answer - is not. Set GH_TOKEN or GITHUB_TOKEN to raise the limit.

.EXAMPLE
    pwsh -NoProfile -File tools/verify-sourcelink.ps1
    pwsh -NoProfile -File tools/verify-sourcelink.ps1 -Version 3.0.0-preview.4
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $PackageId = 'namespace2xml',
    [string] $SymbolSource = 'https://globalcdn.nuget.org/symbol-packages',
    [string] $SymbolPackage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Resolve the curl *executable*, not whatever "curl" happens to name. On Windows PowerShell "curl"
# is an alias for Invoke-WebRequest, which is why the call sites used to say "curl.exe" outright;
# on Linux and macOS there is no such file and the release job died before it fetched anything.
# -CommandType Application skips aliases and functions, so asking for both names in order is safe
# on every host: Windows finds curl.exe, everything else falls through to curl.
function Resolve-CurlExecutable {
    foreach ($name in @('curl.exe', 'curl')) {
        $found = Get-Command -Name $name -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($found) { return $found.Source }
    }

    throw 'curl was not found on PATH. This script needs it to fetch source documents and to read ' +
          'the HTTP status of each fetch without treating a 404 as a failure to answer.'
}

$curl = Resolve-CurlExecutable

# Portable PDB custom debug information kinds, from the portable PDB specification.
$sourceLinkKind = [Guid] 'CC110556-A091-4D38-9FEC-25AB9A351A6A'
$embeddedKind = [Guid] '0E8A571B-6926-466E-B4AD-8AB04611F5FE'
$sha256Algorithm = [Guid] '8829D00F-11B8-4213-878B-770E8597AC16'

if (-not $Version -and -not $SymbolPackage) {
    $newest = (git tag --list 'v3.*' --sort=-creatordate | Select-Object -First 1)
    if (-not $newest) { throw 'No v3.* tag found, and no -Version was given.' }
    $Version = $newest.Substring(1)
    Write-Host "No -Version given; verifying the newest v3.* tag, $newest."
}

$work = Join-Path ([IO.Path]::GetTempPath()) "verify-sourcelink-$([Guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Path $work -Force | Out-Null

# A status the host may answer under load, or an empty status meaning curl never got one at all.
# None of these is an answer about the document, so none of them is evidence against the package.
$transient = @('000', '408', '425', '429', '500', '502', '503', '504')

$authorization = @()
$token = if ($env:GH_TOKEN) { $env:GH_TOKEN } elseif ($env:GITHUB_TOKEN) { $env:GITHUB_TOKEN } else { $null }
if ($token) {
    $authorization = @('-H', "authorization: Bearer $token")
    Write-Host 'Using a token from the environment to raise the request limit.'
}

function Get-Document {
    param([string] $Url, [string] $Path)

    $status = '000'
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        $status = & $curl -sS @authorization -o $Path -w '%{http_code}' $Url
        if ($transient -notcontains $status) { return $status }
        Start-Sleep -Seconds ([Math]::Pow(2, $attempt))
    }

    return $status
}

try {
    if ($SymbolPackage) {
        $archive = Join-Path $work 'symbols.zip'
        Copy-Item -LiteralPath $SymbolPackage -Destination $archive
        Write-Host "Verifying local symbol package $SymbolPackage."
    }
    else {
        $url = "$SymbolSource/$PackageId.$Version.snupkg"
        $archive = Join-Path $work 'symbols.zip'
        Write-Host "Downloading $url"
        & $curl -sS --fail -o $archive $url
        if ($LASTEXITCODE -ne 0) { throw "Could not download the symbol package for $PackageId $Version." }
    }

    $extracted = Join-Path $work 'symbols'
    Expand-Archive -LiteralPath $archive -DestinationPath $extracted -Force

    $pdbs = @(Get-ChildItem -LiteralPath $extracted -Recurse -File -Filter *.pdb)
    if ($pdbs.Count -eq 0) { throw 'The symbol package contains no .pdb file.' }

    $fetched = 0
    $embedded = 0
    $failures = [System.Collections.Generic.List[string]]::new()
    $scratch = Join-Path $work 'document'

    foreach ($pdb in $pdbs) {
        Write-Host ''
        Write-Host "$($pdb.Name)"

        $stream = [IO.File]::OpenRead($pdb.FullName)
        try {
            $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream)
            $reader = $provider.GetMetadataReader()

            $map = $null
            $embeddedDocuments = [System.Collections.Generic.HashSet[string]]::new()

            foreach ($handle in $reader.CustomDebugInformation) {
                $information = $reader.GetCustomDebugInformation($handle)
                $kind = $reader.GetGuid($information.Kind)

                if ($kind -eq $sourceLinkKind) {
                    $json = [Text.Encoding]::UTF8.GetString($reader.GetBlobBytes($information.Value))
                    $map = ($json | ConvertFrom-Json).documents
                }
                elseif ($kind -eq $embeddedKind -and $information.Parent.Kind -eq 'Document') {
                    $document = $reader.GetDocument([System.Reflection.Metadata.DocumentHandle] $information.Parent)
                    [void] $embeddedDocuments.Add($reader.GetString($document.Name))
                }
            }

            if (-not $map) {
                $failures.Add("$($pdb.Name): carries no SourceLink document map.")
                continue
            }

            $patterns = @($map.PSObject.Properties | ForEach-Object {
                    [pscustomobject] @{ Prefix = $_.Name.TrimEnd('*'); Replacement = $_.Value.TrimEnd('*') }
                })
            foreach ($pattern in $patterns) {
                Write-Host "  map $($pattern.Prefix)* -> $($pattern.Replacement)*"
            }

            foreach ($documentHandle in $reader.Documents) {
                $document = $reader.GetDocument($documentHandle)
                $name = $reader.GetString($document.Name)

                if ($embeddedDocuments.Contains($name)) {
                    $embedded++
                    continue
                }

                $checksum = $reader.GetBlobBytes($document.Hash)
                if ($checksum.Length -eq 0) {
                    $failures.Add("$name : the compiler recorded no checksum, so nothing can be compared against.")
                    continue
                }
                if ($reader.GetGuid($document.HashAlgorithm) -ne $sha256Algorithm) {
                    $failures.Add("$name : checksum algorithm is not SHA-256.")
                    continue
                }

                $pattern = $patterns | Where-Object { $name.StartsWith($_.Prefix, [StringComparison]::Ordinal) } | Select-Object -First 1
                if (-not $pattern) {
                    $failures.Add("$name : neither embedded in the PDB nor matched by any SourceLink prefix.")
                    continue
                }

                $target = $pattern.Replacement + $name.Substring($pattern.Prefix.Length)
                $status = Get-Document -Url $target -Path $scratch
                if ($status -ne '200') {
                    $note = if ($transient -contains $status) { ' (still transient after five attempts)' } else { '' }
                    $failures.Add("$name : $target answered $status$note.")
                    continue
                }

                $expected = (($checksum | ForEach-Object { $_.ToString('x2') }) -join '')
                $actual = (Get-FileHash -LiteralPath $scratch -Algorithm SHA256).Hash.ToLowerInvariant()
                if ($actual -cne $expected) {
                    $failures.Add("$name : served bytes hash $actual, the compiler recorded $expected.")
                    continue
                }

                $fetched++
            }
        }
        finally {
            $stream.Dispose()
        }
    }

    Write-Host ''
    Write-Host "Settled by fetch, checksum matching the compiler record : $fetched"
    Write-Host "Settled by source embedded in the PDB                    : $embedded"
    Write-Host "Unsettled                                                : $($failures.Count)"

    foreach ($failure in $failures) { Write-Host "  $failure" }

    if ($failures.Count -gt 0) {
        Write-Error "$($failures.Count) document(s) in $PackageId $Version cannot be traced back to source."
        exit 1
    }

    # A package that embedded every document, or whose document table is empty, would otherwise
    # report a confident green having proved nothing about SourceLink at all.
    if ($fetched -eq 0) {
        Write-Error 'No document was settled by fetching it, so this run says nothing about SourceLink.'
        exit 1
    }

    Write-Host ''
    Write-Host "Every document in $PackageId $Version reaches its source."
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
