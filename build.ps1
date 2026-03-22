#Requires -Version 5.1
<#
.SYNOPSIS
    AKML-SQL full build script. Builds all projects and produces the installer EXE.
.DESCRIPTION
    Builds Core, Formatting, Engine, Updater, Formatter CLI, all 6 shell extensions,
    runs tests, and compiles the Inno Setup installer.
.PARAMETER SkipTests
    Skip running unit tests.
.PARAMETER SkipShell
    Skip building shell extension projects (MSBuild).
.PARAMETER InstallerOnly
    Only build the Inno Setup installer (assumes all projects already built).
.PARAMETER Configuration
    Build configuration. Default: Release.
.EXAMPLE
    .\build.ps1
    .\build.ps1 -SkipTests
    .\build.ps1 -InstallerOnly
#>
param(
    [switch]$SkipTests,
    [switch]$SkipShell,
    [switch]$InstallerOnly,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot

# --- Tool paths ---
$MSBuild = "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
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
        & $MSBuild "$Root\$Project" -t:Restore -p:Configuration=$Configuration -v:quiet -nologo
        if ($LASTEXITCODE -ne 0) { return }
        & $MSBuild "$Root\$Project" -t:Build -p:Configuration=$Configuration -v:minimal -nologo
    }
}

# --- Installer-only shortcut ---
if ($InstallerOnly) {
    Invoke-Build "Installer (Inno Setup)" {
        & $ISCC "$Root\src\AkmlSql.Installer\AkmlSqlSetup.iss"
    }
    $Exe = "$Root\src\AkmlSql.Installer\Output\AKMLSQLSetup.exe"
    Write-Host "`nInstaller ready: $Exe" -ForegroundColor Yellow
    exit 0
}

$TotalSw = [System.Diagnostics.Stopwatch]::StartNew()

# --- .NET projects ---
Invoke-Build "Core library" {
    dotnet build "$Root\src\AkmlSql.Core\AkmlSql.Core.csproj" -c $Configuration -v quiet --nologo
}

Invoke-Build "Formatting library" {
    dotnet build "$Root\src\AkmlSql.Formatting\AkmlSql.Formatting.csproj" -c $Configuration -v quiet --nologo
}

Invoke-Build "Engine (publish)" {
    dotnet publish "$Root\src\AkmlSql.Engine\AkmlSql.Engine.csproj" -c $Configuration -r win-x64 -v quiet --nologo
}

Invoke-Build "Updater (publish)" {
    dotnet publish "$Root\src\AkmlSql.Updater\AkmlSql.Updater.csproj" -c $Configuration -v quiet --nologo
}

Invoke-Build "Formatter CLI (publish)" {
    dotnet publish "$Root\src\AkmlSql.Formatter\AkmlSql.Formatter.csproj" -c $Configuration -v quiet --nologo
}

# --- Shell extensions (MSBuild, one at a time) ---
if (-not $SkipShell) {
    Build-Shell "src\AkmlSql.Ssms20\AkmlSql.Ssms20.csproj"
    Build-Shell "src\AkmlSql.Ssms21\AkmlSql.Ssms21.csproj"
    Build-Shell "src\AkmlSql.Ssms22\AkmlSql.Ssms22.csproj"
    Build-Shell "src\AkmlSql.VS2019\AkmlSql.VS2019.csproj"
    Build-Shell "src\AkmlSql.VS2022\AkmlSql.VS2022.csproj"
    Build-Shell "src\AkmlSql.VS2026\AkmlSql.VS2026.csproj"
}

# --- Tests ---
if (-not $SkipTests) {
    Invoke-Build "Tests: Core" {
        dotnet test "$Root\tests\AkmlSql.Core.Tests\AkmlSql.Core.Tests.csproj" -c $Configuration -v quiet --nologo
    }
    Invoke-Build "Tests: Engine" {
        dotnet test "$Root\tests\AkmlSql.Engine.Tests\AkmlSql.Engine.Tests.csproj" -c $Configuration -v quiet --nologo
    }
    Invoke-Build "Tests: Formatting" {
        dotnet test "$Root\tests\AkmlSql.Formatting.Tests\AkmlSql.Formatting.Tests.csproj" -c $Configuration -v quiet --nologo
    }
}

# --- Installer ---
Invoke-Build "Installer (Inno Setup)" {
    & $ISCC "$Root\src\AkmlSql.Installer\AkmlSqlSetup.iss"
}

$TotalSw.Stop()
$Exe = "$Root\src\AkmlSql.Installer\Output\AKMLSQLSetup.exe"
$Size = [math]::Round((Get-Item $Exe).Length / 1MB, 2)

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  BUILD COMPLETE ($([math]::Round($TotalSw.Elapsed.TotalSeconds, 1))s)" -ForegroundColor Green
Write-Host "  Output: $Exe" -ForegroundColor Yellow
Write-Host "  Size:   $Size MB" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Green
