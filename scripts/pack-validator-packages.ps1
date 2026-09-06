# Packs the validator web integration boundary into versioned local/private
# NuGet packages (T025, tasks.md Phase 2).
#
# Usage:
#   ./scripts/pack-validator-packages.ps1 [-Version 1.0.0] [-OutputDir ./artifacts/validator-packages]
#
# The output folder is a local feed: point a nuget.config at it (e.g. the
# Certus website does) and reference the packages by id and version.

param(
    [string]$Version = "1.0.0",
    [string]$OutputDir = "$PSScriptRoot/../artifacts/validator-packages"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$output = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))

Write-Host "Packing validator packages $Version into $output"

# The version is applied at pack time so the checked-in csproj default stays
# 1.0.0 while the host can consume any build.
$application = Join-Path $repoRoot "src/Validator.Application/Validator.Application.csproj"
$infrastructure = Join-Path $repoRoot "src/Validator.Infrastructure/Validator.Infrastructure.csproj"

foreach ($project in @($application, $infrastructure)) {
    if (-not (Test-Path $project)) {
        throw "Project not found: $project"
    }
}

New-Item -ItemType Directory -Force -Path $output | Out-Null

dotnet pack $application -c Release -p:PackageVersion=$Version -o $output
if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed for Validator.Application" }

dotnet pack $infrastructure -c Release -p:PackageVersion=$Version -o $output
if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed for Validator.Infrastructure" }

Write-Host ""
Write-Host "Packages written to $output:"
Get-ChildItem $output -Filter "*.nupkg" |
    ForEach-Object { Write-Host "  $($_.Name)" }

Write-Host ""
Write-Host "Local feed usage (nuget.config):"
Write-Host "  <packageSources>"
Write-Host "    <add key=""validator-local"" value=""$output"" />"
Write-Host "  </packageSources>"