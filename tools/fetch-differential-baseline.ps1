<#
.SYNOPSIS
    Downloads and verifies the namespace2xml 2.4.0 differential baseline package.

.DESCRIPTION
    Appendix C.6 of docs/specification.md pins the baseline by URL, SHA-256 and byte size. This
    script fetches that package and refuses to hand back a path unless both match, so a lane that
    consumes its output is comparing against the artifact the specification names rather than
    whatever nuget.org served today.

    The identity constants are transcribed from the specification deliberately rather than read out
    of it. A script that parsed the pin would agree with the specification by construction and could
    not disagree with it, which is the one thing a second copy is for.

.PARAMETER Destination
    Directory to write the package into. Defaults to a temporary directory.

.PARAMETER Force
    Re-download even when a verified package is already present.

.OUTPUTS
    The full path of the verified package.
#>
[CmdletBinding()]
param(
    [string] $Destination = (Join-Path ([IO.Path]::GetTempPath()) 'n2x-differential-baseline'),
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$packageUrl = 'https://api.nuget.org/v3-flatcontainer/namespace2xml/2.4.0/namespace2xml.2.4.0.nupkg'
$expectedSha256 = '92472F4F191A8FC32B81CE30A8F3E2FC97CF99C968F635155172F111EE65C3ED'
$expectedSize = 1095996

function Test-Baseline([string] $path) {
    $actualSize = (Get-Item -LiteralPath $path).Length
    if ($actualSize -ne $expectedSize) {
        return "size $actualSize bytes, expected $expectedSize"
    }

    $actualSha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actualSha256 -ne $expectedSha256) {
        return "SHA-256 $actualSha256, expected $expectedSha256"
    }

    return $null
}

if (-not (Test-Path -LiteralPath $Destination)) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
}

$packagePath = Join-Path $Destination 'namespace2xml.2.4.0.nupkg'

if ((Test-Path -LiteralPath $packagePath) -and -not $Force) {
    $problem = Test-Baseline $packagePath
    if (-not $problem) {
        Write-Verbose "Reusing verified baseline at $packagePath."
        Write-Output $packagePath
        return
    }

    Write-Verbose "Existing package rejected ($problem); re-downloading."
}

Write-Verbose "Downloading $packageUrl."
Invoke-WebRequest -Uri $packageUrl -OutFile $packagePath -MaximumRedirection 5

$problem = Test-Baseline $packagePath
if ($problem) {
    throw "The downloaded package is not the baseline Appendix C.6 pins: $problem. Refusing to use it."
}

Write-Output $packagePath
