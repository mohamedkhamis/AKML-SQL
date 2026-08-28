#Requires -Version 5.1
<#
.SYNOPSIS
    Generate src/AkmlSql.Site/wwwroot/docs-metadata.json (per-doc git freshness dates).

.DESCRIPTION
    Enumerates doc/**/*.md and, for each file that survives the exclusion list,
    resolves two dates from git history:

        added   = author date of the commit that ADDED the file
                  (git log --follow --diff-filter=A, oldest entry; --follow is
                  best-effort across renames)
        updated = author date of the latest commit touching the file (git log -1)

    Files with no git history (untracked / uncommitted, e.g. newly written docs)
    fall back to today's date for both fields. Dates are yyyy-MM-dd.

    The exclusion list is READ FROM src/AkmlSql.Site/appsettings.json
    (Docs:Exclusions) -- the single source of truth shared with the site's
    Docs/DocsCatalog.cs (folder-prefix / filename-wildcard semantics mirrored
    below) and the csproj DocsSource Removes.

    Output shape (keys are doc/-relative paths with forward slashes):
        {
          "generatedAt": "2026-08-28T16:00:00Z",
          "docs": {
            "topics/getting-started.md": { "added": "2026-08-28", "updated": "2026-08-28" }
          }
        }

    The site (src/AkmlSql.Site/Docs/DocsMetadata.cs) loads this file to derive the
    New/Updated badges and tolerates a missing file, so builds without git keep the
    committed copy. The file itself is committed so "dotnet run" works without
    regenerating.

.PARAMETER RepoRoot
    Repository root. Defaults to the script's parent folder. Pass it explicitly when
    invoking via powershell.exe -File: under Windows PowerShell 5.1 the
    $PSScriptRoot-based default can resolve empty (see build.ps1).

.PARAMETER OutputPath
    Override the output file. Default: src/AkmlSql.Site/wwwroot/docs-metadata.json.
    Relative paths resolve against RepoRoot.

.EXAMPLE
    powershell -NoProfile -File scripts/generate-docs-metadata.ps1 -RepoRoot .
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    throw "RepoRoot is empty -- pass -RepoRoot explicitly"
}
$RepoRoot = (Resolve-Path $RepoRoot).Path

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $outFile = Join-Path $RepoRoot 'src/AkmlSql.Site/wwwroot/docs-metadata.json'
}
elseif ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $outFile = $OutputPath
}
else {
    $outFile = Join-Path $RepoRoot $OutputPath
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "git not found on PATH -- cannot resolve doc freshness dates"
}

# --- Exclusion list (single source of truth: appsettings.json Docs:Exclusions) ---
$settingsPath = Join-Path $RepoRoot 'src/AkmlSql.Site/appsettings.json'
if (-not (Test-Path $settingsPath)) {
    throw "appsettings.json not found at $settingsPath"
}
# JavaScriptSerializer instead of ConvertFrom-Json: the PS 5.1 cmdlet throws on the
# empty-string key ("") in Docs:SectionTitles.
Add-Type -AssemblyName System.Web.Extensions
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$settings = $serializer.DeserializeObject((Get-Content $settingsPath -Raw))
$exclusions = @()
if ($settings -and $settings.ContainsKey('Docs') -and
    $settings['Docs'] -and $settings['Docs'].ContainsKey('Exclusions')) {
    $exclusions = @($settings['Docs']['Exclusions'])
}

# Mirrors DocsCatalog.IsExcluded (src/AkmlSql.Site/Docs/DocsCatalog.cs):
#   - trailing '/' -> folder prefix, matched at the root or any depth segment
#   - otherwise    -> wildcard (* ?) full-string match on the filename OR the path
# Matching is case-insensitive; paths are compared with forward slashes.
function Test-DocExcluded {
    param([string]$RelativePath, [array]$Exclusions)
    $fileName = ($RelativePath -split '/')[-1]
    foreach ($entry in $Exclusions) {
        if ([string]::IsNullOrWhiteSpace($entry)) { continue }
        $pattern = ($entry.Trim() -replace '\\', '/')
        if ($pattern.EndsWith('/')) {
            if ($RelativePath.StartsWith($pattern, [System.StringComparison]::OrdinalIgnoreCase) -or
                $RelativePath.IndexOf("/$pattern", [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                return $true
            }
        }
        elseif ($fileName -like $pattern -or $RelativePath -like $pattern) {
            return $true
        }
    }
    return $false
}

# --- git dates ---------------------------------------------------------------------
# Untracked / uncommitted files produce no history -> today's date for both fields.
$today = (Get-Date).ToString('yyyy-MM-dd')

# Outputs matching date lines one per pipeline item (callers wrap in @()).
function Get-GitDateLines {
    param([string[]]$GitArgs, [string]$RepoRelativePath)
    $lines = @(& git -C $RepoRoot @GitArgs -- $RepoRelativePath 2>$null)
    if ($LASTEXITCODE -ne 0) { return }
    $lines | Where-Object { $_ -match '^\d{4}-\d{2}-\d{2}' }
}

# Outputs exactly two items: added date, then updated date (callers wrap in @()).
function Get-DocDates {
    param([string]$RepoRelativePath)
    # Added: the oldest "file added" commit (log prints newest first -> last line).
    $addedLines = @(Get-GitDateLines -GitArgs @('log', '--follow', '--diff-filter=A', '--format=%aI') -RepoRelativePath $RepoRelativePath)
    if ($addedLines.Count -gt 0) { $addedLines[-1].Substring(0, 10) } else { $today }
    # Updated: the newest commit touching the file.
    $updatedLines = @(Get-GitDateLines -GitArgs @('log', '-1', '--format=%aI') -RepoRelativePath $RepoRelativePath)
    if ($updatedLines.Count -gt 0) { $updatedLines[0].Substring(0, 10) } else { $today }
}

# --- Enumerate + emit ----------------------------------------------------------------
$docsRoot = Join-Path $RepoRoot 'doc'
if (-not (Test-Path $docsRoot)) {
    throw "docs folder not found at $docsRoot"
}

$files = Get-ChildItem -Path $docsRoot -Recurse -Filter '*.md' -File |
    Sort-Object { $_.FullName.ToLowerInvariant() }

$docs = [ordered]@{}
$skipped = 0
foreach ($file in $files) {
    $relative = $file.FullName.Substring($docsRoot.Length).TrimStart('\', '/') -replace '\\', '/'
    if (Test-DocExcluded -RelativePath $relative -Exclusions $exclusions) {
        $skipped++
        continue
    }
    $dates = @(Get-DocDates -RepoRelativePath ("doc/" + $relative))
    $docs[$relative] = [ordered]@{ added = $dates[0]; updated = $dates[1] }
}

$payload = [ordered]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
    docs        = $docs
}

$outDir = Split-Path -Parent $outFile
if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}
$json = $payload | ConvertTo-Json -Depth 5
# UTF-8 without BOM (the site parses this with System.Text.Json).
[System.IO.File]::WriteAllText($outFile, $json, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "docs-metadata.json: $($docs.Count) docs ($skipped excluded) -> $outFile"
