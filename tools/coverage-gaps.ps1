<#
.SYNOPSIS
    Reports uncovered lines and uncovered branches from a Coverlet coverage.json file.

.DESCRIPTION
    Run this as a FILE (-File), never as an inline -Command one-liner. Inline
    one-liners containing $_ , '\' , '(' get mangled by cmd.exe quote handling,
    which can leave PowerShell sitting at its ">>" continuation prompt forever.

.EXAMPLE
    powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tools\coverage-gaps.ps1

.EXAMPLE
    powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tools\coverage-gaps.ps1 -Path tests\Validator.Domain.Tests\coverage.json
#>
[CmdletBinding()]
param(
    [string] $Path,
    [string] $SearchRoot = 'tests',
    [switch] $IncludeCovered
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- locate file
if ($Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Coverage file not found: $Path" }
    $file = Get-Item -LiteralPath $Path
}
else {
    $candidates = @(
        Get-ChildItem -Path $SearchRoot -Recurse -Filter 'coverage.json' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending
    )
    if ($candidates.Count -eq 0) { throw "No coverage.json found under '$SearchRoot'" }
    $file = $candidates[0]
    if ($candidates.Count -gt 1) {
        Write-Output "Found $($candidates.Count) coverage.json files; using the newest."
    }
}

Write-Output "Coverage file : $($file.FullName)"
Write-Output "Last written  : $($file.LastWriteTime)"
Write-Output ''

$json = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json

# ---------------------------------------------------------------- walk report
$totalLines      = 0
$coveredLines    = 0
$totalBranches   = 0
$coveredBranches = 0
$rows            = New-Object 'System.Collections.Generic.List[object]'

foreach ($module in $json.PSObject.Properties) {
    foreach ($document in $module.Value.PSObject.Properties) {

        $sourceName = Split-Path -Path $document.Name -Leaf

        foreach ($class in $document.Value.PSObject.Properties) {
            foreach ($method in $class.Value.PSObject.Properties) {

                $uncoveredLines = New-Object 'System.Collections.Generic.List[int]'

                $lines = $method.Value.Lines
                if ($null -ne $lines) {
                    foreach ($line in $lines.PSObject.Properties) {
                        $totalLines++
                        if ([int64]$line.Value -gt 0) {
                            $coveredLines++
                        }
                        else {
                            $uncoveredLines.Add([int]$line.Name)
                        }
                    }
                }

                $branchTotal     = 0
                $branchUncovered = 0
                foreach ($branch in @($method.Value.Branches)) {
                    if ($null -eq $branch) { continue }
                    $branchTotal++
                    $totalBranches++
                    if ([int64]$branch.Hits -gt 0) { $coveredBranches++ }
                    else { $branchUncovered++ }
                }

                if (-not $IncludeCovered -and $uncoveredLines.Count -eq 0 -and $branchUncovered -eq 0) {
                    continue
                }

                # "System.Char Ns.Type::get_Unit()" -> "get_Unit"
                $short = $method.Name
                $sep = $short.IndexOf('::')
                if ($sep -ge 0) { $short = $short.Substring($sep + 2) }
                $paren = $short.IndexOf('(')
                if ($paren -ge 0) { $short = $short.Substring(0, $paren) }

                $sorted = @($uncoveredLines | Sort-Object)

                $rows.Add([pscustomobject]@{
                    Source          = $sourceName
                    Method          = $short
                    UncoveredLines  = ($sorted -join ',')
                    LineGapCount    = $sorted.Count
                    BranchUncovered = $branchUncovered
                    BranchTotal     = $branchTotal
                })
            }
        }
    }
}

# -------------------------------------------------------------------- results
if ($rows.Count -eq 0) {
    Write-Output 'No uncovered lines or branches. Full coverage.'
}
else {
    foreach ($group in ($rows | Group-Object Source | Sort-Object Name)) {
        Write-Output "=== $($group.Name)"
        foreach ($row in $group.Group) {
            $parts = New-Object 'System.Collections.Generic.List[string]'
            if ($row.LineGapCount -gt 0) {
                $parts.Add("lines[$($row.UncoveredLines)]")
            }
            if ($row.BranchUncovered -gt 0) {
                $parts.Add("branches $($row.BranchUncovered)/$($row.BranchTotal) uncovered")
            }
            Write-Output ("    {0} -> {1}" -f $row.Method, ($parts -join '  '))
        }
        Write-Output ''
    }
}

function Format-Percent {
    param([int64] $Covered, [int64] $Total)
    if ($Total -eq 0) { return 'n/a' }
    return ('{0:N2}%' -f (100.0 * $Covered / $Total))
}

Write-Output '---------------- summary ----------------'
Write-Output ("Lines    : {0}/{1} covered ({2})  gaps={3}" -f `
    $coveredLines, $totalLines, (Format-Percent $coveredLines $totalLines), ($totalLines - $coveredLines))
Write-Output ("Branches : {0}/{1} covered ({2})  gaps={3}" -f `
    $coveredBranches, $totalBranches, (Format-Percent $coveredBranches $totalBranches), ($totalBranches - $coveredBranches))
Write-Output ("Methods with gaps : {0}" -f $rows.Count)
