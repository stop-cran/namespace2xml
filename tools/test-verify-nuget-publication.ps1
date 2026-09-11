#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Proves public NuGet reconciliation retries only 404 and preserves package identity.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Package,

    [Parameter(Mandatory)]
    [string] $SymbolPackage
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$sourcePackage = (Resolve-Path -LiteralPath $Package).Path
$sourceSymbols = (Resolve-Path -LiteralPath $SymbolPackage).Path
$version = '3.0.0'
$packageName = "namespace2xml.$version.nupkg"
$symbolName = "namespace2xml.$version.snupkg"
$root = Join-Path ([IO.Path]::GetTempPath()) (
    'namespace2xml-publication-test-' + [guid]::NewGuid().ToString('n'))
$candidate = Join-Path $root 'candidate'
$verifier = Join-Path $PSScriptRoot 'verify-nuget-publication.ps1'

function Get-FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([Net.IPEndPoint] $listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Start-TestServer {
    param(
        [Parameter(Mandatory)]
        [int] $Port,

        [Parameter(Mandatory)]
        [int[]] $Statuses,

        [string] $PackagePath = $sourcePackage,

        [string] $SymbolPath = $sourceSymbols
    )

    $readyPath = Join-Path $root "server-$Port.ready"
    $job = Start-Job -ArgumentList @(
        $Port,
        ($Statuses -join ','),
        $PackagePath,
        $SymbolPath,
        $readyPath) -ScriptBlock {
        param($Port, $StatusList, $PackagePath, $SymbolPath, $ReadyPath)

        $ErrorActionPreference = 'Stop'
        $Statuses = [int[]] @($StatusList -split ',')
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
        try {
            $listener.Start()
            [IO.File]::WriteAllText($ReadyPath, 'ready')
            foreach ($status in $Statuses) {
                $client = $listener.AcceptTcpClient()
                try {
                    $stream = $client.GetStream()
                    $reader = [IO.StreamReader]::new(
                        $stream,
                        [Text.Encoding]::ASCII,
                        $false,
                        1024,
                        $true)
                    try {
                        $requestLine = $reader.ReadLine()
                        while ($reader.ReadLine()) {}
                    }
                    finally {
                        $reader.Dispose()
                    }

                    $path = ($requestLine -split ' ')[1]
                    Write-Output $path
                    $body = if ($status -eq 200 -and $path -eq '/package') {
                        [IO.File]::ReadAllBytes($PackagePath)
                    }
                    elseif ($status -eq 200 -and $path -eq '/symbols') {
                        [IO.File]::ReadAllBytes($SymbolPath)
                    }
                    else {
                        [Text.Encoding]::UTF8.GetBytes("controlled HTTP $status")
                    }
                    $reason = switch ($status) {
                        200 { 'OK' }
                        404 { 'Not Found' }
                        500 { 'Internal Server Error' }
                        default { 'Controlled' }
                    }
                    $header = (
                        "HTTP/1.1 $status $reason`r`n" +
                        "Content-Length: $($body.Length)`r`n" +
                        "Connection: close`r`n`r`n"
                    )
                    $headerBytes = [Text.Encoding]::ASCII.GetBytes($header)
                    $stream.Write($headerBytes, 0, $headerBytes.Length)
                    $stream.Write($body, 0, $body.Length)
                    $stream.Flush()
                }
                finally {
                    $client.Dispose()
                }
            }
        }
        finally {
            $listener.Stop()
        }
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $readyPath -PathType Leaf)) {
        if ($job.State -in @('Completed', 'Failed', 'Stopped')) {
            $details = Receive-Job -Job $job | Out-String
            Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
            throw "Test server on port $Port stopped before becoming ready: $details"
        }
        if ([DateTime]::UtcNow -ge $deadline) {
            Stop-Job -Job $job -ErrorAction SilentlyContinue
            Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
            throw "Test server on port $Port did not become ready within 10 seconds."
        }
        Start-Sleep -Milliseconds 50
    }

    return $job
}

function Invoke-PublicationVerifier {
    param(
        [Parameter(Mandatory)]
        [int] $Port,

        [Parameter(Mandatory)]
        [bool] $ShouldPass,

        [string] $FailurePattern,

        [string] $Artifact
    )

    $evidence = Join-Path $root ([guid]::NewGuid().ToString('n'))
    $arguments = @(
        '-NoProfile',
        '-File', $verifier,
        '-CandidateDirectory', $candidate,
        '-Version', $version,
        '-EvidenceDirectory', $evidence,
        '-Attempts', '2',
        '-DelaySeconds', '0',
        '-PackageUrl', "http://127.0.0.1:$Port/package",
        '-SymbolUrl', "http://127.0.0.1:$Port/symbols")
    if ($Artifact) {
        $arguments += @('-Artifacts', $Artifact)
    }
    $output = & pwsh @arguments 2>&1
    $passed = $LASTEXITCODE -eq 0
    if ($passed -ne $ShouldPass) {
        throw "Public-verifier case had unexpected result: $($output | Out-String)"
    }
    if (-not $ShouldPass -and $FailurePattern -and
        ($output | Out-String) -notmatch $FailurePattern) {
        throw (
            "Public-verifier failure missed oracle '$FailurePattern': " +
            ($output | Out-String)
        )
    }
}

try {
    [IO.Directory]::CreateDirectory($candidate) | Out-Null
    Copy-Item -LiteralPath $sourcePackage -Destination (Join-Path $candidate $packageName)
    Copy-Item -LiteralPath $sourceSymbols -Destination (Join-Path $candidate $symbolName)

    $existingPackagePort = Get-FreePort
    $existingPackageServer = Start-TestServer -Port $existingPackagePort -Statuses @(200)
    try {
        Invoke-PublicationVerifier `
            -Port $existingPackagePort `
            -ShouldPass $true `
            -Artifact package
        $paths = [string[]] @(
            Wait-Job -Job $existingPackageServer -Timeout 10 |
                Receive-Job)
        if ([string]::Join(',', $paths) -cne '/package') {
            throw "Package-only case received unexpected requests: $($paths -join ', ')"
        }
    }
    finally {
        Stop-Job -Job $existingPackageServer -ErrorAction SilentlyContinue
        Remove-Job -Job $existingPackageServer -Force -ErrorAction SilentlyContinue
    }

    $mismatchPort = Get-FreePort
    $mismatchServer = Start-TestServer `
        -Port $mismatchPort `
        -Statuses @(200) `
        -PackagePath $sourceSymbols
    try {
        Invoke-PublicationVerifier `
            -Port $mismatchPort `
            -ShouldPass $false `
            -FailurePattern 'Served package has .* payload entries' `
            -Artifact package
        $paths = [string[]] @(
            Wait-Job -Job $mismatchServer -Timeout 10 |
                Receive-Job)
        if ([string]::Join(',', $paths) -cne '/package') {
            throw "Package-mismatch case received unexpected requests: $($paths -join ', ')"
        }
    }
    finally {
        Stop-Job -Job $mismatchServer -ErrorAction SilentlyContinue
        Remove-Job -Job $mismatchServer -Force -ErrorAction SilentlyContinue
    }

    $retryPort = Get-FreePort
    $retryServer = Start-TestServer -Port $retryPort -Statuses @(404, 200, 200)
    try {
        Invoke-PublicationVerifier -Port $retryPort -ShouldPass $true
        $paths = [string[]] @(
            Wait-Job -Job $retryServer -Timeout 10 |
                Receive-Job)
        if ([string]::Join(',', $paths) -cne '/package,/package,/symbols') {
            throw "404 retry case received unexpected requests: $($paths -join ', ')"
        }
    }
    finally {
        Stop-Job -Job $retryServer -ErrorAction SilentlyContinue
        Remove-Job -Job $retryServer -Force -ErrorAction SilentlyContinue
    }

    $failurePort = Get-FreePort
    $failureServer = Start-TestServer -Port $failurePort -Statuses @(500)
    try {
        Invoke-PublicationVerifier `
            -Port $failurePort `
            -ShouldPass $false `
            -FailurePattern 'HTTP 500'
        $paths = [string[]] @(
            Wait-Job -Job $failureServer -Timeout 10 |
                Receive-Job)
        if ([string]::Join(',', $paths) -cne '/package') {
            throw "HTTP 500 case retried unexpectedly: $($paths -join ', ')"
        }
    }
    finally {
        Stop-Job -Job $failureServer -ErrorAction SilentlyContinue
        Remove-Job -Job $failureServer -Force -ErrorAction SilentlyContinue
    }

    $transportPort = Get-FreePort
    Invoke-PublicationVerifier `
        -Port $transportPort `
        -ShouldPass $false `
        -FailurePattern 'transport failure'

    Write-Host (
        'Public NuGet verifier accepted requested existing bytes, rejected a pre-publication ' +
        'mismatch, retried HTTP 404 to exact identity, and failed immediately on HTTP 500 and ' +
        'transport errors.'
    )
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
