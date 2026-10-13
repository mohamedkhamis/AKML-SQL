#Requires -Version 5.1
<#
.SYNOPSIS
Build and run the web Playwright tests, then open the HTML results in your browser.
.DESCRIPTION
Requires Windows, the .NET 10 SDK, and Microsoft Edge. SQL tests use localhost:1433
with Windows authentication. Reconnect tests require the installed AkmlSqlWebEngine
service and administrator rights, and briefly stop/restart that service. Keep ports
5000 and 11434 free. Missing prerequisites can cause tests to skip or fail; see the report.
Tests run serially because their fixtures share ports. Chromium and Firefox are
installed into Playwright's browser cache; Edge uses the existing system installation.
Screenshot generators are excluded unless requested: they overwrite repository images
and the site tour needs Northwind and AKML_DEPLOYED_WEB_URL (your deployed IIS URL).
Native keyboard tests require -IncludeNative and an unlocked, idle Windows desktop.
.EXAMPLE
.\scripts\run-playwright.ps1
.EXAMPLE
.\scripts\run-playwright.ps1 -Headed
.EXAMPLE
.\scripts\run-playwright.ps1 -Headed -IncludeNative -IncludeScreenshots
.EXAMPLE
.\scripts\run-playwright.ps1 -Filter 'FullyQualifiedName~ResultsGridTests'
#>
[CmdletBinding()]
param(
    [switch]$Headed,
    [switch]$IncludeNative,
    [switch]$IncludeScreenshots,
    [string]$Filter,
    [switch]$SkipBuild,
    [switch]$SkipBrowserInstall,
    [switch]$NoOpenReport,
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ResultsDirectory) {
    $ResultsDirectory = Join-Path $env:LOCALAPPDATA ('AKML SQL\playwright-results\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
$null = New-Item -ItemType Directory -Path $ResultsDirectory -Force
$oldHeaded = $env:AKML_PLAYWRIGHT_HEADED
$oldNative = $env:AKML_NATIVE_KEY_ACCEPTANCE
$exitCode = 1

function Invoke-DotNetChecked([string[]]$CommandArgs) {
    & dotnet @CommandArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($CommandArgs -join ' ') failed (exit $LASTEXITCODE)." }
}

Push-Location $repoRoot
try {
    $null = Get-Command dotnet -ErrorAction Stop
    if ((Test-Path (Join-Path $ResultsDirectory 'playwright.trx')) -or
        (Test-Path (Join-Path $ResultsDirectory 'playwright.html'))) {
        throw 'The results directory already contains a report. Choose a new directory for this run.'
    }
    $env:AKML_PLAYWRIGHT_HEADED = if ($Headed) { '1' } else { '0' }
    $env:AKML_NATIVE_KEY_ACCEPTANCE = if ($IncludeNative) { '1' } else { '0' }
    $project = 'tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj'
    if (-not $SkipBuild) {
        foreach ($path in @('src/AkmlSql.Engine/AkmlSql.Engine.csproj', 'src/AkmlSql.Web/AkmlSql.Web.csproj', $project)) {
            Invoke-DotNetChecked -CommandArgs @('build', $path, '-c', 'Debug', '--nologo')
        }
    }

    if (-not $SkipBrowserInstall) {
        # Use the bundled driver so PowerShell 7 and a separate Node install are unnecessary.
        $driver = Join-Path $repoRoot 'tests/AkmlSql.Web.E2E.Tests/bin/Debug/net10.0/.playwright'
        $node = Join-Path $driver 'node/win32_x64/node.exe'
        $cli = Join-Path $driver 'package/cli.js'
        if (-not (Test-Path $node)) { throw 'Playwright driver not found. Run without -SkipBuild first.' }
        & $node $cli install chromium firefox
        if ($LASTEXITCODE -ne 0) { throw "Playwright browser installation failed (exit $LASTEXITCODE)." }
    }

    $filters = @()
    if (-not $IncludeScreenshots) {
        $filters += 'FullyQualifiedName!~SiteScreenshotTour&FullyQualifiedName!~WorkspaceScreenshotTour'
    }
    if ($Filter) { $filters += "($Filter)" }
    $testArgs = @('test', $project, '-c', 'Debug', '--no-build', '--nologo',
        '--results-directory', $ResultsDirectory,
        '--logger', 'trx;LogFileName=playwright.trx',
        '--logger', 'html;LogFileName=playwright.html',
        '--logger', 'console;verbosity=normal')
    if ($filters.Count) { $testArgs += @('--filter', ($filters -join '&')) }
    $testArgs += @('--', 'xUnit.ParallelizeTestCollections=false')
    Write-Host "Results: $ResultsDirectory"
    Write-Host 'Running serially. Reconnect tests may briefly restart AkmlSqlWebEngine.'
    & dotnet @testArgs
    $exitCode = $LASTEXITCODE

    $trxPath = Join-Path $ResultsDirectory 'playwright.trx'
    if (Test-Path $trxPath) {
        [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
        $counts = $trx.TestRun.ResultSummary.Counters
        Write-Host "Passed: $($counts.passed)  Failed: $($counts.failed)  Skipped: $($counts.notExecuted)"
        if ([int]$counts.total -eq 0) { $exitCode = 1; Write-Warning 'No tests matched the filter.' }
    } else {
        $exitCode = 1
        Write-Warning 'No test result file was produced.'
    }
    $report = Join-Path $ResultsDirectory 'playwright.html'
    if (Test-Path $report) {
        Write-Host "HTML report: $report"
        if (-not $NoOpenReport) {
            try { Start-Process -FilePath $report }
            catch { Write-Warning "Could not open the report automatically. Open $report in your browser." }
        }
    }
} catch {
    Write-Error $_ -ErrorAction Continue
    $exitCode = 1
} finally {
    $env:AKML_PLAYWRIGHT_HEADED = $oldHeaded
    $env:AKML_NATIVE_KEY_ACCEPTANCE = $oldNative
    Pop-Location
}
exit $exitCode
