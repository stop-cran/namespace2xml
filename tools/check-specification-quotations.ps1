#Requires -Version 7
<#
.SYNOPSIS
    Verifies that every quotation of docs/specification.md elsewhere in the repository is still
    verbatim.

.DESCRIPTION
    The specification is the contract, so the surrounding prose quotes it constantly: fixture
    rationales, KNOWN-LIMITS entries, the format guides and the agent instructions all reproduce
    normative sentences so a reader can see the rule without leaving the page.

    Amending the specification silently invalidates every one of those copies. Nothing else in this
    repository notices. The failure is worse than an ordinary stale comment because the quotation is
    presented as normative text and is indistinguishable, to a reader, from the clause itself -- the
    document goes on asserting a rule that the contract no longer contains.

    This gate reads two quotation forms:

      * a run of consecutive markdown blockquote lines, in every scanned file;
      * an inline span in double quotes, of at least -MinimumLength characters, in the files listed
        in $inlineChecked.

    The inline form is checked in only some files because the house style uses double quotes for
    paraphrase and emphasis as well as for quotation, and no mechanical rule separates the two:
    measured over the whole tree, one inline span in five is deliberately not verbatim, and neither
    span length nor an adjacent section citation predicts which. KNOWN-LIMITS.md is checked because
    a stale claim there is the failure that has already reached a reader -- its Section 1.9 shipped
    in 3.0.0-preview.2 asserting a defect that was fixed -- and because it is short enough for its
    exemptions to be genuinely reviewed rather than rubber-stamped. Fixture rationales rely on the
    blockquote form for the same purpose, which is why that form is checked everywhere.

    Each quotation is compared against the specification with whitespace collapsed, so re-wrapping
    to a different column is not a failure; case-insensitively, because embedding a sentence
    mid-sentence lowercases its first letter; ignoring trailing sentence punctuation, because
    quoting a clause and closing it with a full stop is ordinary usage; and treating an ellipsis as
    an elision whose fragments must appear in order.

    Prose that is deliberately not a verbatim quotation is listed in the exemption file with a
    reason. An exemption matches by its opening text, so shortening a long worked example to its
    first sentence is enough to record it, while editing the opening of an exempted span re-arms
    the check rather than inheriting the old decision.

.PARAMETER ExemptionPath
    The JSON file listing spans that are deliberately not verbatim specification text.

.PARAMETER MinimumLength
    The shortest inline double-quoted span to check. Short spans are ordinary English punctuation
    far more often than they are quotations.

.PARAMETER ListUnexempted
    Emit the failures as exemption-file entries, ready to paste after review. Does not change the
    exit code.
#>
[CmdletBinding()]
param(
    [string] $ExemptionPath = 'tools/quotation-exemptions.json',
    [int] $MinimumLength = 25,
    [switch] $ListUnexempted
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $repoRoot
try {
    $specPath = 'docs/specification.md'
    if (-not (Test-Path $specPath)) {
        Write-Error "The specification is missing: $specPath"
    }

    function ConvertTo-Collapsed {
        param([string] $Text)
        # A quotation re-wrapped to a different column is the same quotation, and so is one that
        # reflows a bulleted list into running prose, adds emphasis to the phrase under discussion,
        # or respells a nested double quote as a single one -- which quoting a sentence that itself
        # contains a quotation requires. List markers, emphasis and quote characters are therefore
        # removed from both sides rather than compared. Non-breaking and zero-width characters are
        # folded too: they are invisible in a diff and would otherwise produce a failure no reader
        # could see.
        $t = $Text -replace "[\u00a0\u2007\u202f]", ' '
        $t = $t -replace "[\u200b\u200c\u200d\ufeff]", ''
        $t = $t -replace '(?m)^\s*[-*+]\s+', ''
        $t = $t -replace '[*"''\u2018\u2019\u201c\u201d]', ''
        return ($t -replace '\s+', ' ').Trim()
    }

    $specification = ConvertTo-Collapsed ([IO.File]::ReadAllText((Join-Path $repoRoot $specPath)))

    function Test-Quotation {
        param([string] $Text)
        # An elision is an honest quotation of two spans, so each fragment must appear, and in
        # order. This is stricter than skipping elided quotations and stricter than comparing the
        # fragments independently: it is the claim the ellipsis actually makes. Both the bare and
        # the bracketed spellings are recognized.
        #
        # The comparison ignores case because embedding a sentence mid-sentence lowercases its
        # first letter, and ignores trailing sentence punctuation because quoting a clause and
        # closing it with a full stop is ordinary usage. Neither is the signal this gate exists to
        # catch, which is a sentence whose words have stopped existing.
        $elision = '\s*[\[\(]?\s*(?:…|\.\.\.)\s*[\]\)]?\s*'
        $fragments = @(($Text.TrimEnd('.', ',', ';', ':') -split $elision) |
                Where-Object { $_.Length -gt 0 })
        $at = 0
        foreach ($fragment in $fragments) {
            $index = $specification.IndexOf($fragment, $at, [StringComparison]::OrdinalIgnoreCase)
            if ($index -lt 0) { return $false }
            $at = $index + $fragment.Length
        }
        return $fragments.Count -gt 0
    }

    $exemptions = [System.Collections.Generic.List[object]]::new()
    if (Test-Path $ExemptionPath) {
        $parsed = [IO.File]::ReadAllText((Join-Path $repoRoot $ExemptionPath)) | ConvertFrom-Json
        foreach ($entry in $parsed.exemptions) {
            if ([string]::IsNullOrWhiteSpace($entry.reason)) {
                Write-Error ("An exemption without a reason is a silenced failure: {0}" -f $entry.text)
            }
            $exemptions.Add([pscustomobject]@{
                    File = $entry.file
                    Text = ConvertTo-Collapsed $entry.text
                })
        }
    }

    function Test-Exempt {
        param([string] $File, [string] $Text)
        foreach ($entry in $exemptions) {
            if ($entry.File -ne $File) { continue }
            if ($Text.StartsWith($entry.Text, [StringComparison]::OrdinalIgnoreCase)) { return $true }
        }
        return $false
    }

    # Every tree that quotes the contract at a reader. The specification quotes itself; the
    # changelog records what past releases said, which is history rather than a live claim; and the
    # generated documents quote the corpus and the registry, which have gates of their own.
    $generated = @('specification.md', 'migration-2.x-to-3.0.md', 'diagnostics.md')
    $files = @()
    $files += Get-ChildItem -Path 'docs' -Filter '*.md' -File |
        Where-Object { $generated -notcontains $_.Name } |
        ForEach-Object { 'docs/{0}' -f $_.Name }
    $files += Get-ChildItem -Path 'conformance' -Directory |
        ForEach-Object { 'conformance/{0}/legacy.md' -f $_.Name } |
        Where-Object { Test-Path $_ }
    $files += @('KNOWN-LIMITS.md', 'CONTRIBUTING.md', 'AGENTS.md', 'README.md',
        '.github/copilot-instructions.md',
        # The collection ships this one to consumers who may never see the repository, which makes
        # it the copy of the contract most able to drift unnoticed and the one most worth checking.
        'ansible/docs/specification-summary.md') | Where-Object { Test-Path $_ }

    # See the description: the inline form is only separable from paraphrase where the exemptions
    # can be reviewed one by one.
    $inlineChecked = @('KNOWN-LIMITS.md')

    function Get-Quotation {
        param([string] $Path, [bool] $IncludeInline)

        $raw = [IO.File]::ReadAllText((Join-Path $repoRoot $Path))
        $lines = $raw -split "\r?\n"
        $found = [System.Collections.Generic.List[object]]::new()

        # Blockquote runs, taken from the raw lines so that a quotation containing a fenced example
        # survives intact.
        $buffer = [System.Collections.Generic.List[string]]::new()
        $start = 0
        for ($i = 0; $i -le $lines.Count; $i++) {
            $line = if ($i -lt $lines.Count) { $lines[$i] } else { '' }
            if ($line -match '^\s*>\s?(.*)$') {
                if ($buffer.Count -eq 0) { $start = $i + 1 }
                $buffer.Add($Matches[1])
                continue
            }
            if ($buffer.Count -gt 0) {
                $found.Add([pscustomobject]@{
                        Kind = 'blockquote'
                        Line = $start
                        Text = ConvertTo-Collapsed ($buffer -join "`n")
                    })
                $buffer.Clear()
            }
        }

        if (-not $IncludeInline) { return $found }

        # Inline spans. Fenced blocks are removed and inline code is masked rather than deleted:
        # the specification carries the same backticks, so deleting them would compare a mangled
        # quotation against intact contract text, and deleting a quote character inside a code
        # sample would invert the pairing of everything after it.
        $prose = $raw -replace '(?ms)^\s*```.*?^\s*```\s*$', ''
        $prose = [regex]::Replace($prose, '(?s)`[^`\r\n]*`', {
                param($m) $m.Value -replace '"', "`u{0001}" })

        # Quotations are paired across a whole paragraph, because prose here is hard-wrapped and a
        # quotation that spans two lines would otherwise invert the parity of the rest of the file.
        foreach ($paragraph in ($prose -split "(?:\r?\n){2,}")) {
            $segments = $paragraph -split '"'
            if ($segments.Count -lt 3 -or $segments.Count % 2 -eq 0) { continue }
            $line = 1 + (($prose.Substring(0, $prose.IndexOf($paragraph)) -split "\r?\n").Count - 1)
            for ($s = 1; $s -lt $segments.Count; $s += 2) {
                $text = (ConvertTo-Collapsed $segments[$s]) -replace "`u{0001}", '"'
                if ($text.Length -ge $MinimumLength) {
                    $found.Add([pscustomobject]@{ Kind = 'inline'; Line = $line; Text = $text })
                }
            }
        }

        return $found
    }

    $checked = 0
    $exempted = 0
    $failures = [System.Collections.Generic.List[object]]::new()

    foreach ($file in $files) {
        foreach ($quotation in (Get-Quotation -Path $file -IncludeInline ($inlineChecked -contains $file))) {
            if ($quotation.Text.Length -lt $MinimumLength) { continue }
            $checked++
            if (Test-Quotation $quotation.Text) { continue }
            if (Test-Exempt -File $file -Text $quotation.Text) {
                $exempted++
                continue
            }
            $failures.Add([pscustomobject]@{
                    File = $file
                    Line = $quotation.Line
                    Kind = $quotation.Kind
                    Text = $quotation.Text
                })
        }
    }

    if ($failures.Count -eq 0) {
        Write-Host ("{0} quotations of the specification are verbatim ({1} exempted)." -f
            ($checked - $exempted), $exempted)
        exit 0
    }

    Write-Host ("{0} of {1} quotations do not appear in {2}:" -f
        $failures.Count, $checked, $specPath)
    Write-Host ''
    foreach ($failure in $failures) {
        $shown = if ($failure.Text.Length -gt 160) { $failure.Text.Substring(0, 157) + '...' }
        else { $failure.Text }
        Write-Host ("::error file={0},line={1}::{2} quotation is not verbatim specification text" -f
            $failure.File, $failure.Line, $failure.Kind)
        Write-Host ("  {0}" -f $shown)
        Write-Host ''
    }

    if ($ListUnexempted) {
        Write-Host 'As exemption entries:'
        Write-Host ''
        $entries = $failures | ForEach-Object {
            [ordered]@{ file = $_.File; text = $_.Text; reason = 'TODO' }
        }
        Write-Host (ConvertTo-Json @($entries) -Depth 4)
    }

    Write-Host 'Either restore the quotation to the specification wording, or record it in'
    Write-Host "$ExemptionPath with the reason it is not verbatim."
    exit 1
}
finally {
    Pop-Location
}
