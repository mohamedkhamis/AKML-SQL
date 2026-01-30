<#
.SYNOPSIS
    Run tests for AKML-SQL solution.

.DESCRIPTION
    Runs all unit tests and generates coverage report.

.PARAMETER Filter
    Test filter expression. Example: "FullyQualifiedName~SqlParser"

.PARAMETER Coverage
    Generate code coverage report.

.EXAMPLE
    .\test.ps1 -Coverage
#>

param(
    [string]$Filter,
    [switch]$Coverage
)

$ErrorActionPreference = "Stop"
$SolutionDir = Split-Path -Parent $PSScriptRoot
$TestResultsDir = Join-Path $SolutionDir "TestResults"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  AKML-SQL Test Runner" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Ensure test results directory exists
if (-not (Test-Path $TestResultsDir)) {
    New-Item -ItemType Directory -Path $TestResultsDir | Out-Null
}

# Build test arguments
$testArgs = @(
    "test"
    (Join-Path $SolutionDir "AKML-SQL.sln")
    "--configuration", "Debug"
    "--logger", "trx;LogFileName=TestResults.trx"
    "--results-directory", $TestResultsDir
    "--verbosity", "normal"
)

if ($Filter) {
    $testArgs += "--filter", $Filter
    Write-Host "Filter: $Filter" -ForegroundColor Gray
}

if ($Coverage) {
    $testArgs += "--collect:XPlat Code Coverage"
    Write-Host "Coverage: Enabled" -ForegroundColor Gray
}

Write-Host "`nRunning tests..." -ForegroundColor Yellow
Write-Host ""

# Run tests
& dotnet @testArgs

$exitCode = $LASTEXITCODE

# Summary
Write-Host ""
if ($exitCode -eq 0) {
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  All Tests Passed!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
} else {
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "  Some Tests Failed!" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
}

Write-Host ""
Write-Host "Results saved to: $TestResultsDir" -ForegroundColor Gray

exit $exitCode
