<#
.SYNOPSIS
    Reports which public Domain/Application types still lack a documentation
    comment, so T032 can be finished against facts rather than guesswork.
#>
[CmdletBinding()]
param(
    [switch] $ListFiles,

    # Restrict the report to the files this feature added, which is what T032
    # actually asks about; pre-existing types are a separate concern.
    [switch] $FeatureOnly,

    [string] $BaseRef = 'origin/main'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

$checklist = Join-Path $repoRoot 'specs/002-detailed-error-report/checklists/requirements.md'
if (Test-Path -LiteralPath $checklist) {
    $lines = Get-Content -LiteralPath $checklist
    $total = ($lines | Select-String -Pattern '^\s*- \[[ Xx]\]').Count
    $done = ($lines | Select-String -Pattern '^\s*- \[[Xx]\]').Count
    Write-Output "checklist requirements.md: total=$total completed=$done incomplete=$($total - $done)"
}

$roots = @(
    (Join-Path $repoRoot 'src/Validator.Domain'),
    (Join-Path $repoRoot 'src/Validator.Application')
)

$typePattern = '^\s*public\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+|ref\s+)*(class|record|interface|enum|struct)\s+([A-Za-z0-9_]+)'

$featureFiles = $null
if ($FeatureOnly) {
    $added = & git --no-pager diff --name-only --diff-filter=A "$BaseRef...HEAD" -- 'src/Validator.Domain' 'src/Validator.Application'
    $featureFiles = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $added) {
        if (-not [string]::IsNullOrWhiteSpace($path)) {
            $featureFiles.Add((Join-Path $repoRoot ($path -replace '/', '\'))) | Out-Null
        }
    }
    Write-Output "restricted to $($featureFiles.Count) files added since $BaseRef"
}

$xmlDocumented = 0
$lineDocumented = 0
$undocumented = [System.Collections.Generic.List[string]]::new()

foreach ($root in $roots) {
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -Filter *.cs) {
        if ($null -ne $featureFiles -and -not $featureFiles.Contains($file.FullName)) { continue }

        $lines = Get-Content -LiteralPath $file.FullName
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $match = [regex]::Match($lines[$i], $typePattern)
            if (-not $match.Success) { continue }

            # Walk back over attributes to find whether the declaration is
            # preceded by an XML documentation comment.
            $j = $i - 1
            while ($j -ge 0 -and ($lines[$j].Trim() -eq '' -or $lines[$j].Trim().StartsWith('['))) { $j-- }

            $preceding = if ($j -ge 0) { $lines[$j].Trim() } else { '' }

            if ($preceding.StartsWith('///')) {
                $xmlDocumented++
            }
            elseif ($preceding.StartsWith('//')) {
                $lineDocumented++
            }
            else {
                $relative = $file.FullName.Substring($repoRoot.Length + 1)
                $undocumented.Add("$relative : $($match.Groups[2].Value)")
            }
        }
    }
}

Write-Output "public types with '///' XML docs      : $xmlDocumented"
Write-Output "public types with '//' rationale docs : $lineDocumented"
Write-Output "public types with no documentation    : $($undocumented.Count)"

if ($ListFiles) {
    Write-Output ''
    $undocumented | Sort-Object | ForEach-Object { Write-Output "  $_" }
}
