#Requires -Version 7
<#
.SYNOPSIS
Proves the Section 10.4 native-template gates go red against a mutated implementation.

.DESCRIPTION
The seven new conformance cases are the primary oracle; the unit tests pin the projection shape and
the two refusals. Every mutation changes a *result* rather than reordering work, because an inert
mutant is not a test gap and a mutation that turns a walk into a loop hangs the harness instead of
reporting. Read a survivor before writing a test for it.

The harness leaves the binaries built from restored source, but a run that is killed part way does
not: verify the file by hand and rebuild before trusting a later --no-build run.
#>
[CmdletBinding()]
param(
    [string] $UnitFilter = 'FullyQualifiedName~StructuredProfileReaderTests',
    [string] $CaseFilter = 'FullyQualifiedName~native|FullyQualifiedName~wildcard-key|FullyQualifiedName~asterisk|FullyQualifiedName~names-uninterpreted'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$reader = 'src/Namespace2Xml.Core/Inputs/StructuredProfileReader.cs'

$mutations = @(
    @{
        Name = 'Extracted templates are dropped instead of returned to step 7'
        Path = $reader
        From = 'return new ProfileContribution(overlay, [], projection.Templates);'
        To   = 'return new ProfileContribution(overlay, [], []);'
    },
    @{
        Name = 'A template is named by its carrier rather than by the wildcard key'
        Path = $reader
        From = 'Extract(property.Value, declared);'
        To   = 'Extract(property.Value, path);'
    },
    @{
        Name = 'The substitute mode is read inverted, so * is data and \* is a rule'
        Path = $reader
        From = 'if (Mode(declared).InterpretsNames())'
        To   = 'if (!Mode(declared).InterpretsNames())'
    },
    @{
        Name = 'A names-uninterpreted wildcard key keeps its token instead of being literalized'
        Path = $reader
        From = 'var literal = QualifiedNameLexer.Literalize(new QualifiedName([property.Name]));'
        To   = 'var literal = new QualifiedName([property.Name]);'
    },
    @{
        Name = 'An extracted value is plain text, so Section 12.1 never substitutes its capture'
        Path = $reader
        From = 'text, ValueSyntax.NativeString(QualifiedNameLexer.CaptureForm(name)));'
        To   = 'text, ValueSyntax.NativeString(WildcardSyntax.None));'
    },
    @{
        Name = 'A sequence under a wildcard key is silently skipped rather than refused'
        Path = $reader
        From = @(
            '                case StructuredSequence:',
            '                    Decline(node, path, "a sequence");',
            '                    return;') -join "`n"
        To   = @(
            '                case StructuredSequence:',
            '                    return;') -join "`n"
    },
    @{
        Name = 'An empty mapping under a wildcard key is silently skipped rather than refused'
        Path = $reader
        From = @(
            '                case StructuredMapping:',
            '                    Decline(node, path, "an empty mapping");',
            '                    return;') -join "`n"
        To   = @(
            '                case StructuredMapping:',
            '                    return;') -join "`n"
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

        $killed = $false
        $summaries = @()

        foreach ($suite in @(
            @{ Project = 'tests/Namespace2Xml.UnitTests'; Filter = $UnitFilter },
            @{ Project = 'tests/Namespace2Xml.Conformance'; Filter = $CaseFilter })) {

            $test = & dotnet test (Join-Path $root $suite.Project) --no-build -c Release --nologo --filter $suite.Filter 2>&1
            if ($LASTEXITCODE -ne 0) {
                $killed = $true
            }

            $summaries += ($test | Select-String 'Failed!|Passed!' | Select-Object -First 1).ToString().Trim()
        }

        if ($killed) {
            Write-Host "KILLED    $($mutation.Name)" -ForegroundColor Green
        }
        else {
            Write-Host "SURVIVED  $($mutation.Name)" -ForegroundColor Red
        }

        $summaries | ForEach-Object { Write-Host "          $_" }
    }
    finally {
        [IO.File]::WriteAllText($path, $original, (New-Object Text.UTF8Encoding($false)))
        (Get-Item $path).LastWriteTime = Get-Date
    }
}

Write-Host ''
Write-Host 'Rebuilding from restored source.' -ForegroundColor Cyan
& dotnet build (Join-Path $root 'namespace2xml.slnx') -c Release --nologo -v q | Out-Null
