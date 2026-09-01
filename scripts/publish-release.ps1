#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes a built AKMLSQLSetup.exe as a downloadable release on the product site.

.DESCRIPTION
    Turns the artifact that doc\Deploy-Build-Release.ps1 produces into a release the download
    page will actually offer:

      1. Reads the version stamped into the installer (or takes -Version).
      2. Copies it into the downloads folder as AKMLSQLSetup-<version>.exe.
      3. Computes the SHA-256 the download page displays for verification.
      4. Adds the entry to src\AkmlSql.Site\wwwroot\releases.json.

    The site derives "latest" itself -- ReleasesManifest orders by releasedAt then version -- so
    this only has to add the entry, never reorder the file.

    Idempotent, with one deliberate exception: if a file for this version already exists and its
    hash differs from the installer being published, the script STOPS. Two different binaries
    sharing one version number is the kind of thing that is invisible until someone reports a
    checksum mismatch, so it is treated as an error rather than quietly overwritten. Pass -Force
    if replacing it is genuinely what you want.

    Nothing here deploys the site. Run scripts\deploy-site-iis.ps1 afterwards to publish the
    updated manifest -- kept separate so the manifest edit can be reviewed and committed first.

.PARAMETER Version
    Override the version. By default it is read from the installer's ProductVersion.

.PARAMETER NotesSummary
    One-line summary shown on the download page.

.PARAMETER Force
    Replace an existing artifact for the same version even if its hash differs.

.EXAMPLE
    powershell -NoProfile -File scripts\publish-release.ps1
#>
[CmdletBinding()]
param(
    [string] $RepoRoot,
    [string] $InstallerPath,
    [string] $DownloadsFolder = 'C:\inetpub\akml.khamis.work-downloads',
    [string] $Version,
    [string[]] $SupportedHosts = @('SSMS 22', 'VS 2026'),
    [string] $NotesSummary,
    [string] $ReleaseNotesUrl = 'https://github.com/mohamedkhamis/AKML-SQL/releases',
    [string] $MinimumOsVersion = '10.0',
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

# Resolved here rather than as a param default: $PSScriptRoot is not reliably populated while the
# param block is being bound, so the tidy-looking default silently becomes an empty string and the
# script dies on Join-Path. (scripts\deploy-site-iis.ps1 has the same latent issue; it only works
# because callers pass -RepoRoot.)
if (-not $RepoRoot) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepoRoot = (Resolve-Path (Join-Path $scriptDir '..')).Path
}

function Say([string] $msg) { Write-Host "  $msg" }
function Ok ([string] $msg) { Write-Host "  [OK] $msg"   -ForegroundColor Green }
function Warn([string] $msg) { Write-Host "  [WARN] $msg" -ForegroundColor Yellow }

if (-not $InstallerPath) {
    $InstallerPath = Join-Path $RepoRoot 'src\AkmlSql.Installer\Output\AKMLSQLSetup.exe'
}
if (-not (Test-Path $InstallerPath)) {
    throw "Installer not found at $InstallerPath. Run doc\Deploy-Build-Release.ps1 first."
}

$manifestPath = Join-Path $RepoRoot 'src\AkmlSql.Site\wwwroot\releases.json'
if (-not (Test-Path $manifestPath)) { throw "releases.json not found at $manifestPath" }

# --- 1. Version ------------------------------------------------------------
if (-not $Version) {
    $Version = (Get-Item $InstallerPath).VersionInfo.ProductVersion
    if ($Version) { $Version = $Version.Trim() }
}
if (-not $Version) {
    throw 'Could not read a version from the installer. Pass -Version explicitly.'
}
# The repo's format is 1.YY.MMDD.HHmm; anything else means the /D override did not reach ISCC.
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Version '$Version' is not the expected 1.YY.MMDD.HHmm shape."
}
# Size formatted first: "Say '...' -f (...)" would bind -f as a parameter of Say, not as the
# format operator.
$sizeMb = '{0:N1}' -f ((Get-Item $InstallerPath).Length / 1MB)
Say "Version:   $Version"
Say "Installer: $InstallerPath ($sizeMb MB)"

# --- 2. Hash ---------------------------------------------------------------
$hash = (Get-FileHash -Path $InstallerPath -Algorithm SHA256).Hash.ToLowerInvariant()
Say "SHA-256:   $hash"

# --- 3. Copy into the downloads folder -------------------------------------
New-Item -ItemType Directory -Force -Path $DownloadsFolder | Out-Null
$artifactName = "AKMLSQLSetup-$Version.exe"
$artifactPath = Join-Path $DownloadsFolder $artifactName

if (Test-Path $artifactPath) {
    $existingHash = (Get-FileHash -Path $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($existingHash -eq $hash) {
        Ok "$artifactName already published with an identical hash -- nothing to copy."
    }
    elseif (-not $Force) {
        throw ("$artifactName already exists with a DIFFERENT hash.`n" +
               "  existing: $existingHash`n" +
               "  new:      $hash`n" +
               "Two different binaries must not share a version. Rebuild to get a new version, " +
               "or pass -Force if replacing it is intended.")
    }
    else {
        Copy-Item $InstallerPath $artifactPath -Force
        Warn "Replaced an existing $artifactName (-Force)."
    }
}
else {
    Copy-Item $InstallerPath $artifactPath
    Ok "Copied to $artifactPath"
}

# --- 4. Manifest -----------------------------------------------------------
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

if (-not $NotesSummary) {
    $existingLatest = $manifest.releases | Select-Object -First 1
    $NotesSummary = if ($existingLatest) { $existingLatest.notesSummary } else { 'AKML SQL for SSMS 22 and Visual Studio 2026.' }
}

$entry = [ordered]@{
    version          = $Version
    releasedAt       = (Get-Date).ToString('yyyy-MM-dd')
    supportedHosts   = @($SupportedHosts)
    downloadUrl      = "downloads/$artifactName"
    sha256Hash       = $hash
    releaseNotesUrl  = $ReleaseNotesUrl
    notesSummary     = $NotesSummary
    minimumOsVersion = $MinimumOsVersion
}

# Replace any existing entry for this version rather than adding a duplicate; the site would
# otherwise render the same version twice.
$others = @($manifest.releases | Where-Object { $_.version -ne $Version })
$manifest.releases = @([pscustomobject]$entry) + $others
$manifest.generatedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

# Written by hand rather than with ConvertTo-Json. Two reasons, both about the fact that this file
# is committed and reviewed: PowerShell 5.1's formatter uses its own indentation and value
# alignment, so the first run would reformat every existing line and bury a one-entry addition in a
# whole-file diff; and Set-Content -Encoding UTF8 emits a BOM, which this file has never had.
# The schema is small and fixed, so emitting it directly is cheap and keeps future diffs to the
# lines that actually changed.
function ConvertTo-JsonString([string] $value) {
    if ($null -eq $value) { return 'null' }
    $escaped = $value -replace '\\', '\\' -replace '"', '\"' -replace "`r", '\r' -replace "`n", '\n' -replace "`t", '\t'
    return '"' + $escaped + '"'
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('{')
[void]$sb.AppendLine('  ' + (ConvertTo-JsonString 'product') + ': ' + (ConvertTo-JsonString $manifest.product) + ',')
[void]$sb.AppendLine('  ' + (ConvertTo-JsonString 'generatedAt') + ': ' + (ConvertTo-JsonString $manifest.generatedAt) + ',')
[void]$sb.AppendLine('  "releases": [')

$rendered = @()
foreach ($r in $manifest.releases) {
    $hosts = ($r.supportedHosts | ForEach-Object { ConvertTo-JsonString $_ }) -join ', '
    $lines = @(
        '    {'
        '      "version": ' + (ConvertTo-JsonString $r.version) + ','
        '      "releasedAt": ' + (ConvertTo-JsonString $r.releasedAt) + ','
        '      "supportedHosts": [ ' + $hosts + ' ],'
        '      "downloadUrl": ' + (ConvertTo-JsonString $r.downloadUrl) + ','
        '      "sha256Hash": ' + (ConvertTo-JsonString $r.sha256Hash) + ','
        '      "releaseNotesUrl": ' + (ConvertTo-JsonString $r.releaseNotesUrl) + ','
        '      "notesSummary": ' + (ConvertTo-JsonString $r.notesSummary) + ','
        '      "minimumOsVersion": ' + (ConvertTo-JsonString $r.minimumOsVersion)
        '    }'
    )
    $rendered += ($lines -join "`n")
}
[void]$sb.AppendLine(($rendered -join ",`n"))
[void]$sb.AppendLine('  ]')
[void]$sb.Append('}')

# UTF8Encoding($false) = no BOM, matching the file as it has always been committed.
[System.IO.File]::WriteAllText($manifestPath, $sb.ToString() + "`n", (New-Object System.Text.UTF8Encoding $false))
Ok "releases.json now lists $($manifest.releases.Count) release(s); newest is $Version"

Write-Host ''
Write-Host '  Next: review + commit releases.json, then deploy the site:' -ForegroundColor White
Write-Host '    powershell -NoProfile -File scripts\deploy-site-iis.ps1' -ForegroundColor Gray
exit 0
