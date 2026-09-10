<#
.SYNOPSIS
    Generates stable clause anchors, the specification contents, and the clause manifest.

.DESCRIPTION
    docs/specification.md is the sole authored contract. This first-stage generator owns its
    navigation markup and emits spec/specification-navigation.json as the section model consumed
    by tests and later generators. The manifest is generated evidence, not a second authored
    section inventory.
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = [IO.Path]::GetFullPath($RepositoryRoot)
$specificationPath = Join-Path $root 'docs/specification.md'
$manifestPath = Join-Path $root 'spec/specification-navigation.json'
$beginMarker = '<!-- BEGIN GENERATED SPECIFICATION CONTENTS -->'
$endMarker = '<!-- END GENERATED SPECIFICATION CONTENTS -->'
$utf8 = New-Object Text.UTF8Encoding $false, $true

function Read-Utf8LfText([string] $Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        throw "$Path must be UTF-8 without a byte-order mark."
    }

    $text = $utf8.GetString($bytes)
    if ($text.Contains("`r", [StringComparison]::Ordinal)) {
        throw "$Path must use LF line endings."
    }
    if (-not $text.EndsWith("`n", [StringComparison]::Ordinal)) {
        throw "$Path must end with one LF."
    }

    return $text
}

function Get-FenceMask([string[]] $Lines) {
    $mask = [Collections.Generic.List[bool]]::new($Lines.Length)
    $fenceCharacter = $null
    $fenceLength = 0

    foreach ($line in $Lines) {
        if ($null -eq $fenceCharacter) {
            $opening = [regex]::Match($line, '^[ ]{0,3}(?<fence>`{3,}|~{3,})(?<info>.*)$')
            if ($opening.Success) {
                $fenceCharacter = $opening.Groups['fence'].Value.Substring(0, 1)
                $fenceLength = $opening.Groups['fence'].Value.Length
                $mask.Add($true)
                continue
            }

            $mask.Add($false)
            continue
        }

        $mask.Add($true)
        $closingPattern = '^[ ]{{0,3}}{0}{{{1},}}[ ]*$' -f
            [regex]::Escape($fenceCharacter), $fenceLength
        if ([regex]::IsMatch($line, $closingPattern)) {
            $fenceCharacter = $null
            $fenceLength = 0
        }
    }

    if ($null -ne $fenceCharacter) {
        throw "$specificationPath contains an unclosed Markdown fence."
    }

    return ,$mask.ToArray()
}

function Get-OldGeneratedAnchors {
    $anchors = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    if (-not (Test-Path $manifestPath -PathType Leaf)) {
        return ,$anchors
    }

    try {
        $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "$manifestPath is not valid generated navigation JSON: $($_.Exception.Message)"
    }

    if ($manifest.generatedBy -ne 'tools/sync-specification-navigation.ps1' -or
        $null -eq $manifest.clauses) {
        throw "$manifestPath is not a specification-navigation manifest."
    }

    foreach ($clause in $manifest.clauses) {
        $anchor = [string]$clause.anchor
        if ($anchor -notmatch '^spec-(?:[0-9]+(?:-[0-9]+)*|[a-z](?:-[0-9]+)*)$') {
            throw "$manifestPath contains the invalid generated anchor '$anchor'."
        }
        if (-not $anchors.Add($anchor)) {
            throw "$manifestPath contains the duplicate generated anchor '$anchor'."
        }
    }

    return ,$anchors
}

function Get-Clause([string] $Line, [int] $LineNumber) {
    $heading = [regex]::Match(
        $Line,
        '^(?<hash>#{2,})[ ](?<text>.*?\S)(?:[ ]+#+)?[ ]*$')
    if (-not $heading.Success) {
        return $null
    }

    $level = $heading.Groups['hash'].Value.Length
    $visible = $heading.Groups['text'].Value
    $section = $null

    $appendix = [regex]::Match(
        $visible,
        '^Appendix[ ](?<section>[A-Z])\.(?:[ ]+.+)?$')
    if ($appendix.Success) {
        if ($level -ne 2) {
            throw "${specificationPath}:${LineNumber}: top-level appendices must be H2 headings."
        }
        $section = $appendix.Groups['section'].Value
    }
    else {
        $subsection = [regex]::Match(
            $visible,
            '^(?<section>[A-Z]\.[0-9]+(?:\.[0-9]+)*)(?:\.)?(?:[ ]+.+)?$')
        if ($subsection.Success) {
            $section = $subsection.Groups['section'].Value
        }
        else {
            $decimal = [regex]::Match(
                $visible,
                '^(?<section>[0-9]+(?:\.[0-9]+)*)(?:\.)?(?:[ ]+.+)?$')
            if ($decimal.Success) {
                $section = $decimal.Groups['section'].Value
            }
        }
    }

    if ($null -eq $section) {
        return $null
    }

    if ($level -gt 4) {
        throw "${specificationPath}:${LineNumber}: clauses must use H2 through H4 headings."
    }

    $depth = $section.Split('.').Length
    $expectedLevel = $depth + 1
    if ($level -ne $expectedLevel) {
        throw "${specificationPath}:${LineNumber}: clause '$section' must be H$expectedLevel, not H$level."
    }

    $anchor = 'spec-' + $section.ToLowerInvariant().Replace('.', '-')
    return [pscustomobject][ordered]@{
        Section = $section
        Anchor = $anchor
        Level = $level
        Heading = $visible
        Line = $LineNumber
    }
}

$text = Read-Utf8LfText $specificationPath
$lines = [regex]::Split($text, "`n")
$oldAnchors = Get-OldGeneratedAnchors
$fenceMask = Get-FenceMask $lines
$oldAnchorCounts = @{}
$withoutAnchors = [Collections.Generic.List[string]]::new($lines.Length)

for ($index = 0; $index -lt $lines.Length; $index++) {
    $line = $lines[$index]
    $anchor = [regex]::Match($line, '^<a id="(?<id>spec-[a-z0-9-]+)"></a>$')
    if (-not $fenceMask[$index] -and $anchor.Success -and $oldAnchors.Contains($anchor.Groups['id'].Value)) {
        $id = $anchor.Groups['id'].Value
        $oldAnchorCounts[$id] = 1 + [int]($oldAnchorCounts[$id])
        if ($oldAnchorCounts[$id] -gt 1) {
            throw "${specificationPath}: generated anchor '$id' occurs more than once."
        }
        continue
    }
    $withoutAnchors.Add($line)
}

$withoutAnchorLines = $withoutAnchors.ToArray()
$withoutAnchorFenceMask = Get-FenceMask $withoutAnchorLines
$begin = @()
$end = @()
for ($index = 0; $index -lt $withoutAnchors.Count; $index++) {
    if (-not $withoutAnchorFenceMask[$index] -and $withoutAnchors[$index] -ceq $beginMarker) {
        $begin += $index
    }
    if (-not $withoutAnchorFenceMask[$index] -and $withoutAnchors[$index] -ceq $endMarker) {
        $end += $index
    }
}
if ($begin.Count -ne 1 -or $end.Count -ne 1 -or $begin[0] -ge $end[0]) {
    throw ("$specificationPath must contain exactly one well-ordered generated contents block; " +
        "found $($begin.Count) begin marker(s) and $($end.Count) end marker(s).")
}

$contentless = [Collections.Generic.List[string]]::new($withoutAnchors.Count)
for ($index = 0; $index -le $begin[0]; $index++) {
    $contentless.Add($withoutAnchors[$index])
}
for ($index = $end[0]; $index -lt $withoutAnchors.Count; $index++) {
    $contentless.Add($withoutAnchors[$index])
}

$contentlessLines = $contentless.ToArray()
$fenceMask = Get-FenceMask $contentlessLines
$reservedAnchor = [regex]::new(
    '<a\b[^>]*\bid\s*=\s*["''](?<id>spec-[^"'']+)["''][^>]*>',
    [Text.RegularExpressions.RegexOptions]::IgnoreCase)
for ($index = 0; $index -lt $contentlessLines.Length; $index++) {
    if (-not $fenceMask[$index] -and $reservedAnchor.IsMatch($contentlessLines[$index])) {
        $id = $reservedAnchor.Match($contentlessLines[$index]).Groups['id'].Value
        throw "${specificationPath}:$($index + 1): reserved anchor '$id' is not owned by the generator."
    }
}

$clauses = [Collections.Generic.List[object]]::new()
$clausesByLine = @{}
$sections = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$anchors = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

for ($index = 0; $index -lt $contentlessLines.Length; $index++) {
    if ($fenceMask[$index]) {
        continue
    }

    $clause = Get-Clause $contentlessLines[$index] ($index + 1)
    if ($null -eq $clause) {
        continue
    }
    if (-not $sections.Add($clause.Section)) {
        throw "${specificationPath}:$($index + 1): duplicate specification clause '$($clause.Section)'."
    }
    if (-not $anchors.Add($clause.Anchor)) {
        throw "${specificationPath}:$($index + 1): duplicate generated anchor '$($clause.Anchor)'."
    }

    if ($clause.Section.Contains('.', [StringComparison]::Ordinal)) {
        $parent = $clause.Section.Substring(0, $clause.Section.LastIndexOf('.'))
        if (-not $sections.Contains($parent)) {
            throw "${specificationPath}:$($index + 1): clause '$($clause.Section)' precedes or lacks parent '$parent'."
        }
    }

    $clauses.Add($clause)
    $clausesByLine[$index] = $clause
}

if ($clauses.Count -eq 0) {
    throw "$specificationPath contains no numbered or appendix clauses."
}
if ($begin[0] -gt $clauses[0].Line - 1) {
    throw "$specificationPath must place the generated contents block before the first clause."
}

$contents = [Collections.Generic.List[string]]::new()
$contents.Add('## Contents')
$contents.Add('')
foreach ($clause in $clauses) {
    $indent = '  ' * ($clause.Level - 2)
    $linkText = $clause.Heading.Replace('\', '\\').Replace('[', '\[').Replace(']', '\]')
    $contents.Add("$indent- [$linkText](#$($clause.Anchor))")
}

$output = [Collections.Generic.List[string]]::new(
    $contentlessLines.Length + $contents.Count + $clauses.Count)
for ($index = 0; $index -lt $contentlessLines.Length; $index++) {
    if ($clausesByLine.ContainsKey($index)) {
        $output.Add("<a id=""$($clausesByLine[$index].Anchor)""></a>")
    }
    $output.Add($contentlessLines[$index])
    if ($index -eq $begin[0]) {
        foreach ($line in $contents) {
            $output.Add($line)
        }
    }
}

$outputText = $output -join "`n"
if (-not $outputText.EndsWith("`n", [StringComparison]::Ordinal)) {
    throw 'Internal error: generated specification lost its final LF.'
}

$manifestObject = [ordered]@{
    generatedBy = 'tools/sync-specification-navigation.ps1'
    clauses = @($clauses | ForEach-Object {
        [ordered]@{
            section = $_.Section
            anchor = $_.Anchor
            level = $_.Level
            heading = $_.Heading
        }
    })
}
$manifestText = (($manifestObject | ConvertTo-Json -Depth 4) -replace "`r`n", "`n").TrimEnd() + "`n"

[IO.Directory]::CreateDirectory((Split-Path -Parent $manifestPath)) | Out-Null
[IO.File]::WriteAllText($specificationPath, $outputText, $utf8)
[IO.File]::WriteAllText($manifestPath, $manifestText, $utf8)

Write-Host "Wrote docs/specification.md navigation and spec/specification-navigation.json ($($clauses.Count) clauses)."
