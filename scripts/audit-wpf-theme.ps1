#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Static audit for WPF chrome-color literals in the AKML shared shell project.

.DESCRIPTION
    Scans src/AkmlSql.Shell.Shared/**/*.cs (excluding Ui/Theme/) for chrome color literals
    that should be migrated to ThemeTokens + SetResourceReference. Supports an explicit
    allow-list of file:line patterns for justified semantic constants (e.g., a destructive-red
    constant intentionally defined as a literal).

    Exits 0 if zero unjustified hits remain; exits 1 (with file:line:hit listing) otherwise.

    Patterns flagged:
      - Color.FromRgb(...) / Color.FromArgb(...)
      - Brushes.<ColorName>
      - #XXXXXX hex color literals (6 hex digits)

.PARAMETER AllowList
    Optional path to a text file with one regex per line. Lines matching any allow-list regex
    are excluded from the hit count. Useful for justified semantic constants.

.EXAMPLE
    ./scripts/audit-wpf-theme.ps1
    ./scripts/audit-wpf-theme.ps1 -AllowList scripts/audit-wpf-theme.allow

.NOTES
    Spec 016 / T016. Authoritative gate for SC-003.
#>
param(
    [string]$Root = "src/AkmlSql.Shell.Shared",
    [string]$AllowList = ""
)
$ErrorActionPreference = 'Stop'

# Resolve script-relative paths to repo root.
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$scanRoot = Join-Path $repoRoot $Root

if (-not (Test-Path $scanRoot)) {
    Write-Error "Scan root not found: $scanRoot"
    exit 2
}

# Patterns that flag a chrome-color literal.
$patterns = @(
    'Color\.From(Rgb|Argb)\(',
    'Brushes\.[A-Z][a-zA-Z]+',
    '#[0-9A-Fa-f]{6}\b'
)

# Load allow-list (one regex per line; lines starting with # are comments).
$allowRegexes = @()
if ($AllowList -and (Test-Path $AllowList)) {
    $allowRegexes = Get-Content $AllowList | Where-Object {
        $_.Trim().Length -gt 0 -and -not $_.Trim().StartsWith('#')
    }
}

function Test-Allowed {
    param([string]$Line)
    foreach ($r in $allowRegexes) {
        if ($Line -match $r) { return $true }
    }
    return $false
}

# Walk *.cs under scanRoot, excluding the Ui/Theme/ design system home.
$files = Get-ChildItem -Path $scanRoot -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '[\\/]Ui[\\/]Theme[\\/]' }

$hits = @()
foreach ($file in $files) {
    $lineNum = 0
    foreach ($line in Get-Content $file.FullName) {
        $lineNum++
        foreach ($p in $patterns) {
            if ($line -match $p) {
                if (-not (Test-Allowed -Line $line)) {
                    $rel = $file.FullName.Substring($repoRoot.Length + 1).Replace('\','/')
                    $hits += [pscustomobject]@{
                        File = $rel
                        Line = $lineNum
                        Match = $matches[0]
                        Text = $line.Trim()
                    }
                }
                break  # one hit per line is enough
            }
        }
    }
}

if ($hits.Count -eq 0) {
    Write-Host "audit-wpf-theme: PASS (zero chrome-color literals outside Ui/Theme/)" -ForegroundColor Green
    exit 0
}

Write-Host "audit-wpf-theme: FAIL ($($hits.Count) unjustified chrome-color literals)" -ForegroundColor Red
Write-Host ""

# Group by file for readable output.
$grouped = $hits | Group-Object File | Sort-Object { $_.Group.Count } -Descending
foreach ($g in $grouped) {
    Write-Host "  $($g.Name) ($($g.Group.Count) hits)" -ForegroundColor Yellow
    foreach ($h in $g.Group | Sort-Object Line) {
        Write-Host ("    {0}:{1}  {2}" -f $h.Line, $h.Match, $h.Text)
    }
}

Write-Host ""
Write-Host "Migrate these to ThemeTokens.<key> with SetResourceReference. See:" -ForegroundColor Cyan
Write-Host "  specs/016-wpf-theme-refresh/contracts/theme-tokens.md" -ForegroundColor Cyan
Write-Host "  specs/016-wpf-theme-refresh/quickstart.md (sections 2 and 3)" -ForegroundColor Cyan
exit 1
