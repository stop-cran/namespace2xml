#Requires -Version 7
<#
.SYNOPSIS
Proves the Section 12.1 capture-substitution gates go red against a mutated implementation.

.DESCRIPTION
Runs both suites, because the four new conformance cases are the primary oracle here and the unit
tests only pin the residue and the refusal wording. Every mutation changes a *result*: a mutation
that merely reorders work can hang the harness rather than report, and an inert mutant is not a
test gap. Read a survivor before writing a test for it.
#>
[CmdletBinding()]
param(
    [string] $UnitFilter = 'FullyQualifiedName~TransformationTests|FullyQualifiedName~SchemeCompilerTests',
    [string] $CaseFilter = 'FullyQualifiedName~capture|FullyQualifiedName~asterisk'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$compiler = 'src/Namespace2Xml.Core/Scheme/SchemeCompiler.cs'
$transformer = 'src/Namespace2Xml.Core/Overlay/ViewTransformer.cs'
$substitution = 'src/Namespace2Xml.Core/Overlay/WildcardRule.cs'

$mutations = @(
    @{
        Name = 'An instance option is bound from an empty capture set rather than the instance''s own'
        Path = $compiler
        From = 'WildcardSubstitution.Apply(winner.Entry.Value, captures))]),'
        To   = 'WildcardSubstitution.Apply(winner.Entry.Value, WildcardCaptures.Empty))]),'
    },
    @{
        Name = 'A key rule names the field its template spells rather than the one its match produced'
        Path = $transformer
        From = 'rule.KeyField is { } template ? WildcardSubstitution.Apply(template, captures) : null;'
        To   = 'rule.KeyField?.LiteralText;'
    },
    @{
        Name = 'A value with more substitutions than captures cycles instead of repeating the last'
        Path = $substitution
        From = 'captures.Positional[Math.Min(next, captures.Positional.Length - 1)]);'
        To   = 'captures.Positional[next % captures.Positional.Length]);'
    },
    @{
        Name = 'A literal asterisk in a root value stays a wildcard token'
        Path = $compiler
        From = 'return QualifiedNameLexer.Literalize(lexed.Name);'
        To   = 'return lexed.Name;'
    },
    @{
        Name = 'The key directive is deferred again rather than substituted'
        Path = $compiler
        From = 'directive == SchemeDirective.Type;'
        To   = 'directive is SchemeDirective.Type or SchemeDirective.Key;'
    },
    @{
        Name = 'A wildcard-valued directive is also compiled at scheme-compile time'
        Path = $compiler
        From = 'private static bool Pending(SchemeEntry entry) => entry.Value.ContainsWildcard;'
        To   = 'private static bool Pending(SchemeEntry entry) => entry.Value.ContainsReference;'
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
