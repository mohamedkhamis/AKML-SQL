<#
.SYNOPSIS
    Build script for AKML-SQL solution.

.DESCRIPTION
    Builds the AKML-SQL solution including Core service and VSIX extension.

.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Debug

.PARAMETER Clean
    Clean before building.

.PARAMETER Restore
    Restore NuGet packages before building.

.EXAMPLE
    .\build.ps1 -Configuration Release -Clean
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    
    [switch]$Clean,
    [switch]$Restore
)

$ErrorActionPreference = "Stop"
$SolutionDir = Split-Path -Parent $PSScriptRoot
$SolutionFile = Join-Path $SolutionDir "AKML-SQL.sln"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  AKML-SQL Build Script" -ForegroundColor Cyan
Write-Host "  Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check for dotnet CLI
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet CLI not found. Please install .NET SDK 8.0 or later."
    exit 1
}

# Display .NET version
$dotnetVersion = dotnet --version
Write-Host "Using .NET SDK: $dotnetVersion" -ForegroundColor Gray

# Clean if requested
if ($Clean) {
    Write-Host "`n[1/4] Cleaning solution..." -ForegroundColor Yellow
    dotnet clean $SolutionFile -c $Configuration --verbosity minimal
    
    # Remove bin and obj directories
    Get-ChildItem -Path $SolutionDir -Include bin,obj -Recurse -Directory | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "  Clean completed." -ForegroundColor Green
} else {
    Write-Host "`n[1/4] Skipping clean (use -Clean to enable)" -ForegroundColor Gray
}

# Restore packages
if ($Restore -or $Clean) {
    Write-Host "`n[2/4] Restoring NuGet packages..." -ForegroundColor Yellow
    dotnet restore $SolutionFile
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Package restore failed!"
        exit 1
    }
    Write-Host "  Restore completed." -ForegroundColor Green
} else {
    Write-Host "`n[2/4] Skipping restore (use -Restore to enable)" -ForegroundColor Gray
}

# Build Core project first (required by VSIX)
Write-Host "`n[3/4] Building Core service..." -ForegroundColor Yellow
$CoreProject = Join-Path $SolutionDir "src\AKML.SQL.Core\AKML.SQL.Core.csproj"
dotnet build $CoreProject -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Error "Core build failed!"
    exit 1
}
Write-Host "  Core build completed." -ForegroundColor Green

# Build entire solution
Write-Host "`n[4/4] Building solution..." -ForegroundColor Yellow
dotnet build $SolutionFile -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Error "Solution build failed!"
    exit 1
}
Write-Host "  Solution build completed." -ForegroundColor Green

# Output locations
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  Build Successful!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Output locations:" -ForegroundColor White
Write-Host "  Core Service: src\AKML.SQL.Core\bin\$Configuration\net8.0\" -ForegroundColor Gray
Write-Host "  VSIX Package: src\AKML.SQL.SSMS\bin\$Configuration\" -ForegroundColor Gray
Write-Host "  Shared Lib:   src\AKML.SQL.Shared\bin\$Configuration\netstandard2.0\" -ForegroundColor Gray
Write-Host ""
