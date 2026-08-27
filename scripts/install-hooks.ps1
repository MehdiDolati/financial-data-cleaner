<#
.SYNOPSIS
    Installs git hooks from scripts/git-hooks/ into .git/hooks/
.DESCRIPTION
    Copies all hook files from the repo's scripts/git-hooks/ directory into
    .git/hooks/, making them executable. Run this after cloning or when hooks
    are updated. Safe to run multiple times.
#>

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$hooksSource = Join-Path $PSScriptRoot "git-hooks"
$hooksTarget = Join-Path $repoRoot ".git" "hooks"

if (-not (Test-Path $hooksSource)) {
    Write-Error "Hooks source directory not found: $hooksSource"
    exit 1
}

if (-not (Test-Path $hooksTarget)) {
    Write-Error ".git/hooks directory not found. Are you in a git repository?"
    exit 1
}

$installed = 0
Get-ChildItem -Path $hooksSource -File | ForEach-Object {
    $dest = Join-Path $hooksTarget $_.Name
    Copy-Item -Path $_.FullName -Destination $dest -Force

    # On Windows, Git Bash handles executable bits via file extension,
    # but we also try to set it for Unix-like environments
    if ($IsLinux -or $IsMacOS) {
        chmod +x $dest
    }

    Write-Host "  ✓ Installed: $($_.Name)" -ForegroundColor Green
    $installed++
}

Write-Host ""
Write-Host "Installed $installed git hook(s) into $hooksTarget" -ForegroundColor Cyan
Write-Host "Hooks will run automatically on the next git operation." -ForegroundColor Gray
