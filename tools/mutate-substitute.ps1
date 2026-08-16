#Requires -Version 7
<#
.SYNOPSIS
Proves the Section 16.7 gates go red against a mutated implementation.
#>
[CmdletBinding()]
param(
    [string] $Filter = 'FullyQualifiedName~SubstituteModeTests'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$mutations = @(
    @{
        Name = 'For scans forward, so an earlier declaration wins'
        Path = 'src/Namespace2Xml.Core/Inputs/SubstituteMode.cs'
        From = 'for (var i = this.declarations.Length - 1; i >= 0; i--)'
        To   = 'for (var i = 0; i < this.declarations.Length; i++)'
    },
    @{
        Name = 'A name is never literalized'
        Path = 'src/Namespace2Xml.Core/Inputs/NamespaceProfileReader.cs'
        From = '            : QualifiedNameLexer.Literalize(lexedName.Name);'
        To   = '            : lexedName.Name;'
    },
    @{
        Name = 'A value is lexed with interpretation under every mode'
        Path = 'src/Namespace2Xml.Core/Inputs/NamespaceProfileReader.cs'
        From = '                : ValueSyntax.ProfileUninterpreted);'
        To   = '                : ValueSyntax.Profile(QualifiedNameLexer.CaptureForm(name)));'
    },
    @{
        Name = 'keyOnly is rejected instead of accepted as a deprecated alias'
        Path = 'src/Namespace2Xml.Core/Inputs/SubstituteMode.cs'
        From = '            ["keyonly"] = (SubstituteMode.Key, SchemeAlias.KeyOnly),'
        To   = ''
    },
    @{
        Name = 'A pathless declaration matches nothing'
        Path = 'src/Namespace2Xml.Core/Inputs/SubstituteMode.cs'
        From = @('            if (pattern is null', '                || (subject is not null && WildcardMatch.TryMatch(pattern, subject, out _)))') -join "`n"
        To   = '            if (pattern is not null && subject is not null && WildcardMatch.TryMatch(pattern, subject, out _))'
    },
    @{
        Name = 'A native string is lexed under Key and None'
        Path = 'src/Namespace2Xml.Core/Inputs/StructuredProfileReader.cs'
        From = '            if (!Mode(path).InterpretsValues())'
        To   = '            if (Mode(path).InterpretsValues())'
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
