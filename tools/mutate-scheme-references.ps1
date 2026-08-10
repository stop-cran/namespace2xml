#Requires -Version 7
<#
.SYNOPSIS
Proves the Section 15.1 step 1 scheme-reference gates go red against a mutated implementation.

.DESCRIPTION
Four conformance cases are the primary oracle; SchemeReferenceResolverTests pins the resolution
semantics and DestinationPathTests pins what a resolved reference contributes to Section 16.2
composition. Every mutation changes a *result* rather than reordering work, because an inert mutant
is not a test gap and a mutation that turns a walk into a loop hangs the harness instead of
reporting. Read a survivor before writing a test for it.

The harness leaves the binaries built from restored source, but a run that is killed part way does
not: verify the file by hand and rebuild before trusting a later --no-build run.
#>
[CmdletBinding()]
param(
    [string] $UnitFilter = 'FullyQualifiedName~SchemeReferenceResolverTests|FullyQualifiedName~DestinationPathTests',
    [string] $CaseFilter = 'FullyQualifiedName~scheme-reference|FullyQualifiedName~empty-scheme-directive'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$resolver = 'src/Namespace2Xml.Core/Scheme/SchemeReferenceResolver.cs'
$composer = 'src/Namespace2Xml.Core/Output/DestinationPath.cs'
$tokens = 'src/Namespace2Xml.Core/Profiles/ValueTokens.cs'

$mutations = @(
    @{
        Name = 'Referenced text is spliced as a literal, so its separators create directories'
        Path = $resolver
        From = 'tokens.Add(new ResolvedReferenceToken(text));'
        To   = 'tokens.Add(new LiteralValueToken(text));'
    },
    @{
        Name = 'A resolved reference is a split point in Section 16.2 step 1'
        Path = $composer
        From = @(
            '                case ResolvedReferenceToken opaque:',
            '                    text.Append(opaque.Text);',
            '                    literal = false;',
            '                    break;') -join "`n"
        To   = @(
            '                case ResolvedReferenceToken opaque:',
            '                    foreach (var c in opaque.Text)',
            '                    {',
            '                        if (c is ''/'' or ''\\'')',
            '                        {',
            '                            Flush();',
            '                        }',
            '                        else',
            '                        {',
            '                            text.Append(c);',
            '                        }',
            '                    }',
            '',
            '                    break;') -join "`n"
    },
    @{
        Name = 'A segment holding referenced text counts as wholly literal, so step 7 rejects it'
        Path = $composer
        From = @(
            '                case ResolvedReferenceToken opaque:',
            '                    text.Append(opaque.Text);',
            '                    literal = false;') -join "`n"
        To   = @(
            '                case ResolvedReferenceToken opaque:',
            '                    text.Append(opaque.Text);') -join "`n"
    },
    @{
        Name = 'A resolved reference is written path for the Section 21.1 rooted test'
        Path = $composer
        From = 'text.Append(token is LiteralValueToken plain ? plain.Text : CaptureMark.ToString());'
        To   = @(
            '            text.Append(token switch',
            '            {',
            '                LiteralValueToken plain => plain.Text,',
            '                ResolvedReferenceToken opaque => opaque.Text,',
            '                _ => CaptureMark.ToString(),',
            '            });') -join "`n"
    },
    @{
        Name = 'A reference reads the first declaration rather than the Section 15.2 winner'
        Path = $resolver
        From = 'winners[new DirectiveKey(new SelectorKey(entry.Selector), entry.Directive)] = entry;'
        To   = 'winners.TryAdd(new DirectiveKey(new SelectorKey(entry.Selector), entry.Directive), entry);'
    },
    @{
        Name = 'Resolved text does not fold into LiteralText, so every other directive sees nothing'
        Path = $tokens
        From = '        [ResolvedReferenceToken resolved] => resolved.Text,'
        To   = '        [ResolvedReferenceToken] => string.Empty,'
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
