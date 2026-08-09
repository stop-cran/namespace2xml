#Requires -Version 7
<#
.SYNOPSIS
Proves the Section 15 structured-scheme gates go red against a mutated implementation.

.DESCRIPTION
Every mutation below changes a *result*, not just a control path, because a mutation that only
reorders work can hang the harness instead of reporting. Read a survivor before writing a test for
it: an inert mutant is not a test gap.
#>
[CmdletBinding()]
param(
    [string] $Filter = 'FullyQualifiedName~StructuredSchemeReaderTests|FullyQualifiedName~TransformationTests'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$reader = 'src/Namespace2Xml.Core/Scheme/StructuredSchemeReader.cs'
$phase = 'src/Namespace2Xml.Core/Pipeline/Steps/SchemePhase.cs'

$mutations = @(
    @{
        Name = 'An empty mapping is a path, so a container value is walked past in silence'
        Path = $reader
        From = 'if (node is StructuredMapping { Properties.IsEmpty: false } mapping)'
        To   = 'if (node is StructuredMapping mapping)'
    },
    @{
        Name = 'The ordering counter stops varying, so sibling directives tie'
        Path = $reader
        From = 'var key = StableOrderingKey.FromSource(sourceOrdinal, ++ordinal);'
        To   = 'var key = StableOrderingKey.FromSource(sourceOrdinal, ++ordinal / 100);'
    },
    @{
        Name = 'The first name part is taken as the directive rather than the last'
        Path = $reader
        From = 'if (path[^1] is not OrdinaryPart { LiteralText: { } directiveName })'
        To   = 'if (path[0] is not OrdinaryPart { LiteralText: { } directiveName })'
    },
    @{
        Name = 'An empty string value is accepted'
        Path = $reader
        From = 'if (lexed.Value.LiteralText is { Length: 0 })'
        To   = 'if (lexed.Value.LiteralText is { Length: 9999 })'
    },
    @{
        Name = 'A typed scalar is read as its raw text rather than its canonical text'
        Path = $reader
        From = 'value = Literal(payload.ToCanonicalText());'
        To   = 'value = Literal(payload.ToString() ?? string.Empty);'
    },
    @{
        Name = 'A JSON or YAML scheme is refused alongside XML'
        Path = $phase
        From = 'if (SourceLoader.StructuredFormat(path) == "XML")'
        To   = 'if (SourceLoader.StructuredFormat(path) is not null)'
    }
)

foreach ($mutation in $mutations) {
    $path = Join-Path $root $mutation.Path
    $original = [IO.File]::ReadAllText($path)

    if (-not $original.Contains($mutation.From)) {
        Write-Host "SKIP  $($mutation.Name) -- MUTATION TEXT NOT FOUND" -ForegroundColor Yellow
        continue
    }

    try {
        $mutated = $original.Replace($mutation.From, $mutation.To)
        [IO.File]::WriteAllText($path, $mutated, (New-Object Text.UTF8Encoding($false)))

        $build = & dotnet build (Join-Path $root 'namespace2xml.slnx') -c Release --nologo -v q 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "BUILD-FAIL $($mutation.Name)" -ForegroundColor Yellow
            $build | Select-String ': error' | Select-Object -First 3 | ForEach-Object { "    $_" }
            continue
        }

        $test = & dotnet test (Join-Path $root 'tests/Namespace2Xml.UnitTests') --no-build -c Release --nologo --filter $Filter 2>&1
        $summary = ($test | Select-String 'Failed!|Passed!' | Select-Object -First 1).ToString().Trim()

        if ($LASTEXITCODE -eq 0) {
            Write-Host "SURVIVED  $($mutation.Name)" -ForegroundColor Red
            Write-Host "          $summary"
        }
        else {
            Write-Host "KILLED    $($mutation.Name)" -ForegroundColor Green
            Write-Host "          $summary"
        }
    }
    finally {
        [IO.File]::WriteAllText($path, $original, (New-Object Text.UTF8Encoding($false)))
        (Get-Item $path).LastWriteTime = Get-Date
    }
}

Write-Host ''
Write-Host 'Rebuilding from restored source.' -ForegroundColor Cyan
& dotnet build (Join-Path $root 'namespace2xml.slnx') -c Release --nologo -v q | Out-Null
