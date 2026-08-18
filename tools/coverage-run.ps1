<#
.SYNOPSIS
    Runs every test project and merges Coverlet results for Validator.Domain and
    Validator.Application into one report.

.DESCRIPTION
    Coverage for these two projects is produced by more than one test project:
    the unit suites exercise them directly, and the CLI/Infrastructure suites
    exercise them through the real pipeline. Measuring a single suite therefore
    understates the truth, so this script merges all of them before reporting.

    Run this as a FILE (-File), never as an inline -Command one-liner.

.EXAMPLE
    powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tools\coverage-run.ps1

.EXAMPLE
    powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tools\coverage-run.ps1 -LineThreshold 99.6 -BranchThreshold 99.0

#>
# Thresholds are decimals, not whole numbers: the gate sits a fraction below the
# measured figure, so rounding them to integers would silently demand 100% and
# fail a run that is actually within tolerance.
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [double] $LineThreshold = 0,
    [double] $BranchThreshold = 0,
    [string] $OutputDirectory = 'artifacts/coverage'
)


$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot

$outDir = Join-Path $repoRoot $OutputDirectory
if (Test-Path -LiteralPath $outDir) { Remove-Item -LiteralPath $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

# Coverlet treats a trailing slash as "write into this directory", which keeps
# the json and cobertura outputs in separate files instead of one overwriting
# the other. Forward slashes avoid MSBuild swallowing a trailing backslash.
$outPrefix = ($outDir -replace '\\', '/') + '/'
$mergedJson = Join-Path $outDir 'coverage.json'


# Coverlet needs the comma inside Include escaped for MSBuild.
$include = '[Validator.Domain]*%2c[Validator.Application]*'

$projects = @(
    'tests/Validator.Domain.Tests/Validator.Domain.Tests.csproj',
    'tests/Validator.Application.Tests/Validator.Application.Tests.csproj',
    'tests/Validator.Infrastructure.Tests/Validator.Infrastructure.Tests.csproj',
    'tests/Validator.Cli.Tests/Validator.Cli.Tests.csproj'
)

for ($i = 0; $i -lt $projects.Count; $i++) {
    $project = $projects[$i]
    $isLast = ($i -eq $projects.Count - 1)

    # Only the last run emits the human-readable formats and applies the gate,
    # because earlier runs hold partial totals while merging is still ongoing.
    # MSBuild treats a bare comma as a property separator, so it stays escaped.
    $formats = if ($isLast) { 'json%2ccobertura' } else { 'json' }

    $arguments = @(
        'test', $project,
        '-c', $Configuration,
        '--nologo',
        '/p:CollectCoverage=true',
        "/p:Include=$include",
        "/p:CoverletOutput=$outPrefix",
        "/p:CoverletOutputFormat=$formats"
    )

    if ($i -gt 0) { $arguments += "/p:MergeWith=$mergedJson" }

    # Line and branch are gated separately because they sit at different levels,
    # and a single shared number would quietly weaken whichever one is higher.
    if ($isLast -and ($LineThreshold -gt 0 -or $BranchThreshold -gt 0)) {
        $arguments += "/p:Threshold=$LineThreshold%2c$BranchThreshold"
        $arguments += '/p:ThresholdType=line%2cbranch'
        $arguments += '/p:ThresholdStat=total'
    }

    Write-Output "==> $project"
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        Write-Output "FAILED: $project (exit $LASTEXITCODE)"
        exit $LASTEXITCODE
    }
}

Write-Output ''
Write-Output "Merged coverage: $mergedJson"
