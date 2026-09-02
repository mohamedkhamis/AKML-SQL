#Requires -Version 5.1
<#
.SYNOPSIS
    AKML-SQL full build script. Builds all projects and produces the installer EXE.
.DESCRIPTION
    Builds Core, Formatting, Engine, Updater, Formatter CLI, the SSMS 22 and
    VS 2026 shell extensions, runs tests, and compiles the Inno Setup installer.
.PARAMETER SkipTests
    Skip running unit tests.
.PARAMETER SkipShell
    Skip building shell extension projects (MSBuild).
.PARAMETER InstallerOnly
    Only build the Inno Setup installer (assumes all projects already built).
.PARAMETER DeploySite
    After the installer is built, deploy the product site to IIS via
    scripts\deploy-site-iis.ps1, staging the new exe as a release on the site
    (versioned download + releases.json entry). Requires an elevated shell.
.PARAMETER Configuration
    Build configuration. Default: Release.
.EXAMPLE
    .\build.ps1
    .\build.ps1 -SkipTests
    .\build.ps1 -InstallerOnly
    .\build.ps1 -DeploySite
#>
param(
    [switch]$SkipTests,
    [switch]$SkipShell,
    [switch]$InstallerOnly,
    [switch]$DeploySite,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot

# --- Compute build version (UTC+2, matching Directory.Build.props formula) ---
# 4 segments so every part fits in a ushort (VSIX / AssemblyVersion require ≤ 65535).
$_now        = [System.DateTime]::UtcNow.AddHours(2)
$BuildYear   = $_now.ToString("yy")
$BuildDate   = $_now.ToString("MMdd")
$BuildTime   = $_now.ToString("HHmm")
$Version     = "1.$BuildYear.$BuildDate.$BuildTime"

# --- Tool paths ---
# MSBuild: prefer VS 2022 Enterprise (the canonical build host); fall back to
# vswhere discovery so the build also runs on VS 2026 / 18.x dev machines instead
# of being pinned to one edition/path. Shell extensions still require full MSBuild
# (not `dotnet build`) per CLAUDE.md — vswhere returns exactly that.
$MSBuild = "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path $MSBuild)) {
    $_vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $_vswhere) {
        $_found = & $_vswhere -latest -prerelease -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($_found) { $MSBuild = $_found }
    }
}
$ISCC    = "C:\Program Files\Inno Setup 7\ISCC.exe"

# --- Validate tools ---
function Assert-Tool([string]$Path, [string]$Name) {
    if (-not (Test-Path $Path)) {
        Write-Host "ERROR: $Name not found at $Path" -ForegroundColor Red
        exit 1
    }
}

Assert-Tool $MSBuild "MSBuild"
Assert-Tool $ISCC    "Inno Setup 7"

Write-Host "Build version : $Version" -ForegroundColor Magenta

# --- Helpers ---
$script:StepNum = 0
function Write-Step([string]$Message) {
    $script:StepNum++
    Write-Host "`n[$script:StepNum] $Message" -ForegroundColor Cyan
}

function Invoke-Build([string]$Description, [scriptblock]$Command) {
    Write-Step $Description
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    & $Command
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $Description (exit code $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    $sw.Stop()
    Write-Host "  Done ($([math]::Round($sw.Elapsed.TotalSeconds, 1))s)" -ForegroundColor Green
}

function Build-Shell([string]$Project) {
    $Name = [System.IO.Path]::GetFileNameWithoutExtension($Project)
    Invoke-Build "Shell: $Name" {
        # Clean obj/bin first. The SSMS22 + VS2026 shell builds otherwise cross-
        # contaminate via STALE VSCT state: a non-clean obj makes one project's
        # MergeWithCTO read the OTHER project's CTO (VSSDK1307 "Could not read cto
        # data from ...AkmlSql<other>.cto" — the contamination CLAUDE.md warns
        # about; node reuse is NOT the cause — disabling it does not help, and the
        # failing direction flips run-to-run). A clean obj/bin makes each project's
        # VSCT compile resolve its OWN .cto. (-nodeReuse:false kept as cheap insurance.)
        $ProjDir = Split-Path -Parent "$Root\$Project"
        Remove-Item -Recurse -Force "$ProjDir\obj","$ProjDir\bin" -ErrorAction SilentlyContinue
        & $MSBuild "$Root\$Project" -t:Restore -p:Configuration=$Configuration -p:Version=$Version -v:quiet -nologo -nodeReuse:false
        if ($LASTEXITCODE -ne 0) { return }
        & $MSBuild "$Root\$Project" -t:Build -p:Configuration=$Configuration -p:Version=$Version -v:minimal -nologo -nodeReuse:false
    }
}

# ISCC now runs preprocess-manifests.ps1 itself via #expr Exec, so build.ps1
# doesn't need a duplicate step. Keeping Update-VsixManifests as a thin wrapper
# means an early failure surfaces before we start the heavy .NET builds.
function Update-VsixManifests([string]$VersionText) {
    Invoke-Build "Resolve VSIX manifests ($VersionText)" {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass `
            -File "$Root\src\AkmlSql.Installer\preprocess-manifests.ps1" `
            -Version $VersionText
    }
}

# --- Installer-only shortcut ---
if ($InstallerOnly) {
    Update-VsixManifests $Version
    Invoke-Build "Installer (Inno Setup)" {
        & $ISCC "$Root\src\AkmlSql.Installer\AkmlSqlSetup.iss" "/DMyAppVersion=$Version"
    }
    $Exe = "$Root\src\AkmlSql.Installer\Output\AKMLSQLSetup.exe"
    Write-Host "`nInstaller ready: $Exe" -ForegroundColor Yellow
    exit 0
}

$TotalSw = [System.Diagnostics.Stopwatch]::StartNew()

# --- Spec 021 (web edition) pre-build gates ---
# Theme CSS must be in sync with docs/theme-tokens.json so the WPF surface and the
# web edition cannot drift apart visually. Fails the build on drift.
Invoke-Build "Gate: theme CSS drift check" {
    # Pass -RepoRoot explicitly: under powershell.exe (5.1) -File, the script's
    # default `Split-Path -Parent $PSScriptRoot` evaluates $PSScriptRoot as empty
    # and the gate dies on "Cannot bind argument ... empty string".
    & powershell.exe -NoProfile -ExecutionPolicy Bypass `
        -File "$Root\scripts\generate-theme-css.ps1" -CheckOnly -RepoRoot "$Root"
}

# Spec 034 (product site): the site serves its OWN copies of the three theme files under
# src\AkmlSql.Site\wwwroot\css\themes — run the same drift gate against that output folder
# so the site's themes cannot drift from docs/theme-tokens.json either (C2).
Invoke-Build "Gate: site theme CSS drift check" {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass `
        -File "$Root\scripts\generate-theme-css.ps1" -CheckOnly -RepoRoot "$Root" `
        -OutputFolder "src\AkmlSql.Site\wwwroot\css\themes"
}

# Spec 034 (product site): regenerate wwwroot/docs-metadata.json (per-doc git
# added/updated dates behind the New/Updated badges) before publishing the site.
# Non-fatal: without git the committed copy of docs-metadata.json is used as-is.
Write-Step "Docs metadata (git dates)"
try {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass `
        -File "$Root\scripts\generate-docs-metadata.ps1" -RepoRoot "$Root"
    if ($LASTEXITCODE -ne 0) { throw "generator exit code $LASTEXITCODE" }
    Write-Host "  Done" -ForegroundColor Green
}
catch {
    Write-Host "  WARNING: docs metadata not regenerated ($_) -- using committed copy" -ForegroundColor Yellow
}

# Spec 034 (product site) — Blazor static SSR, framework-dependent (no -r). Published
# right after the theme gate: the gate covers the exact theme files this site serves.
Invoke-Build "Product site (publish)" {
    dotnet publish "$Root\src\AkmlSql.Site\AkmlSql.Site.csproj" -c $Configuration -p:Version=$Version -v quiet --nologo
}

# --- .NET projects ---
Invoke-Build "Core library" {
    dotnet build "$Root\src\AkmlSql.Core\AkmlSql.Core.csproj" -c $Configuration -p:Version=$Version -v quiet --nologo
}

Invoke-Build "Formatting library" {
    dotnet build "$Root\src\AkmlSql.Formatting\AkmlSql.Formatting.csproj" -c $Configuration -p:Version=$Version -v quiet --nologo
}

Invoke-Build "Engine (publish)" {
    dotnet publish "$Root\src\AkmlSql.Engine\AkmlSql.Engine.csproj" -c $Configuration -r win-x64 -p:Version=$Version -v quiet --nologo
}

Invoke-Build "Updater (publish)" {
    dotnet publish "$Root\src\AkmlSql.Updater\AkmlSql.Updater.csproj" -c $Configuration -p:Version=$Version -v quiet --nologo
}

Invoke-Build "Formatter CLI (publish)" {
    dotnet publish "$Root\src\AkmlSql.Formatter\AkmlSql.Formatter.csproj" -c $Configuration -p:Version=$Version -v quiet --nologo
}

Invoke-Build "Analyzer CLI (publish)" {
    dotnet publish "$Root\src\AkmlSql.Analyzer\AkmlSql.Analyzer.csproj" -c $Configuration -r win-x64 -p:Version=$Version -v quiet --nologo
}

# Web edition (Blazor WASM) — framework-dependent, so NO -r win-x64 (that would
# wrongly make the WASM app self-contained). The installer's web component
# (web-installer.iss [Files]) sources the published wwwroot; this MUST run before
# the Inno Setup step below or ISCC fails with "Source file not found".
Invoke-Build "Web edition (publish)" {
    dotnet publish "$Root\src\AkmlSql.Web\AkmlSql.Web.csproj" -c $Configuration -p:Version=$Version -v quiet --nologo
}

# --- Shell extensions (MSBuild, one at a time) ---
if (-not $SkipShell) {
    Build-Shell "src\AkmlSql.Ssms22\AkmlSql.Ssms22.csproj"
    Build-Shell "src\AkmlSql.VS2026\AkmlSql.VS2026.csproj"
}

# --- Tests ---
if (-not $SkipTests) {
    Invoke-Build "Tests: Core" {
        dotnet test "$Root\tests\AkmlSql.Core.Tests\AkmlSql.Core.Tests.csproj" -c $Configuration -p:Version=$Version -v quiet --nologo
    }
    Invoke-Build "Tests: Engine" {
        dotnet test "$Root\tests\AkmlSql.Engine.Tests\AkmlSql.Engine.Tests.csproj" -c $Configuration -p:Version=$Version -v quiet --nologo
    }
    Invoke-Build "Tests: Formatting" {
        dotnet test "$Root\tests\AkmlSql.Formatting.Tests\AkmlSql.Formatting.Tests.csproj" -c $Configuration -p:Version=$Version -v quiet --nologo
    }
    # Spec 021 (web edition) — bUnit component tests
    Invoke-Build "Tests: Web (bUnit)" {
        dotnet test "$Root\tests\AkmlSql.Web.Tests\AkmlSql.Web.Tests.csproj" -c $Configuration -p:Version=$Version -v quiet --nologo
    }
    # Spec 034 (product site) — bUnit component tests
    Invoke-Build "Tests: Site (bUnit)" {
        dotnet test "$Root\tests\AkmlSql.Site.Tests\AkmlSql.Site.Tests.csproj" -c $Configuration -p:Version=$Version -v quiet --nologo
    }
}

# --- Installer ---
Update-VsixManifests $Version
Invoke-Build "Installer (Inno Setup)" {
    & $ISCC "$Root\src\AkmlSql.Installer\AkmlSqlSetup.iss" "/DMyAppVersion=$Version"
}

# --- Optional: deploy product site with the freshly built release ---
if ($DeploySite) {
    Invoke-Build "Deploy product site (IIS)" {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$Root\scripts\deploy-site-iis.ps1" -RepoRoot $Root
        if ($LASTEXITCODE -ne 0) { throw "deploy-site-iis.ps1 failed (exit $LASTEXITCODE)" }
    }
}

$TotalSw.Stop()
$Exe = "$Root\src\AkmlSql.Installer\Output\AKMLSQLSetup.exe"
$Size = [math]::Round((Get-Item $Exe).Length / 1MB, 2)

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  BUILD COMPLETE ($([math]::Round($TotalSw.Elapsed.TotalSeconds, 1))s)" -ForegroundColor Green
Write-Host "  Version: $Version" -ForegroundColor Magenta
Write-Host "  Output:  $Exe" -ForegroundColor Yellow
Write-Host "  Size:    $Size MB" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Green
