<#
.SYNOPSIS
    Publish AKML-SQL for distribution.

.DESCRIPTION
    Creates release packages for Core service and VSIX extension.

.PARAMETER Version
    Version number for the release. Default: 1.0.0

.PARAMETER OutputDir
    Output directory for packages. Default: .\publish

.EXAMPLE
    .\publish.ps1 -Version "1.0.0"
#>

param(
    [string]$Version = "1.0.0",
    [string]$OutputDir = "publish"
)

$ErrorActionPreference = "Stop"
$SolutionDir = Split-Path -Parent $PSScriptRoot
$PublishDir = Join-Path $SolutionDir $OutputDir
$CorePublishDir = Join-Path $PublishDir "Core"
$VsixPublishDir = Join-Path $PublishDir "VSIX"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  AKML-SQL Publisher" -ForegroundColor Cyan
Write-Host "  Version: $Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Clean publish directory
if (Test-Path $PublishDir) {
    Remove-Item -Path $PublishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $PublishDir | Out-Null
New-Item -ItemType Directory -Path $CorePublishDir | Out-Null
New-Item -ItemType Directory -Path $VsixPublishDir | Out-Null

# Publish Core service (self-contained)
Write-Host "[1/3] Publishing Core service..." -ForegroundColor Yellow
$CoreProject = Join-Path $SolutionDir "src\AKML.SQL.Core\AKML.SQL.Core.csproj"

dotnet publish $CoreProject `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    -o $CorePublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Core publish failed!"
    exit 1
}
Write-Host "  Core published to: $CorePublishDir" -ForegroundColor Green

# Build VSIX
Write-Host "`n[2/3] Building VSIX package..." -ForegroundColor Yellow
$VsixProject = Join-Path $SolutionDir "src\AKML.SQL.SSMS\AKML.SQL.SSMS.csproj"

# Note: VSIX build requires Visual Studio SDK, may need msbuild
dotnet build $VsixProject -c Release -p:Version=$Version

if ($LASTEXITCODE -ne 0) {
    Write-Warning "VSIX build failed. You may need to build with Visual Studio."
}

# Copy VSIX to publish directory
$VsixSource = Join-Path $SolutionDir "src\AKML.SQL.SSMS\bin\Release\*.vsix"
$VsixFiles = Get-ChildItem -Path $VsixSource -ErrorAction SilentlyContinue
if ($VsixFiles) {
    Copy-Item -Path $VsixFiles.FullName -Destination $VsixPublishDir
    Write-Host "  VSIX copied to: $VsixPublishDir" -ForegroundColor Green
} else {
    Write-Host "  VSIX not found (build with Visual Studio)" -ForegroundColor Yellow
}

# Create distribution package
Write-Host "`n[3/3] Creating distribution package..." -ForegroundColor Yellow
$ZipName = "AKML-SQL-v$Version.zip"
$ZipPath = Join-Path $PublishDir $ZipName

# Create a distribution folder with both Core and VSIX
$DistDir = Join-Path $PublishDir "AKML-SQL-v$Version"
New-Item -ItemType Directory -Path $DistDir | Out-Null

# Copy Core files
Copy-Item -Path "$CorePublishDir\*" -Destination (Join-Path $DistDir "Core") -Recurse -Force
New-Item -ItemType Directory -Path (Join-Path $DistDir "Core") -Force | Out-Null
Copy-Item -Path "$CorePublishDir\*" -Destination (Join-Path $DistDir "Core") -Force

# Copy docs
$DocsDir = Join-Path $SolutionDir "docs"
if (Test-Path $DocsDir) {
    Copy-Item -Path $DocsDir -Destination $DistDir -Recurse
}

# Copy README
$ReadmePath = Join-Path $SolutionDir "README.md"
if (Test-Path $ReadmePath) {
    Copy-Item -Path $ReadmePath -Destination $DistDir
}

# Create ZIP
Compress-Archive -Path $DistDir -DestinationPath $ZipPath -Force
Write-Host "  Package created: $ZipPath" -ForegroundColor Green

# Summary
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  Publish Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Output:" -ForegroundColor White
Write-Host "  Core Service: $CorePublishDir" -ForegroundColor Gray
Write-Host "  VSIX Package: $VsixPublishDir" -ForegroundColor Gray
Write-Host "  Distribution: $ZipPath" -ForegroundColor Gray
Write-Host ""
