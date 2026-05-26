#Requires -Version 5.1
<#
.SYNOPSIS
    Builds all AKML SQL components and produces the installer EXE.

.DESCRIPTION
    Full release build pipeline:
      1. Cleans all shell project obj/bin folders (prevents VSCT CTO cross-contamination)
      2. Restores NuGet packages
      3. Builds each shell extension individually with MSBuild (Release)
      4. Publishes Engine, Updater, Formatter CLI, Analyzer CLI
      5. Runs unit tests
      6. Compiles the Inno Setup installer
      7. Reports output location and file size

.PARAMETER Configuration
    Build configuration. Default: Release

.PARAMETER SkipTests
    Skip running unit tests (faster build)

.PARAMETER SkipInstaller
    Skip Inno Setup compilation (build DLLs only)

.PARAMETER Targets
    Comma-separated list of shell targets to build. Default: all supported
    Valid values: Ssms22, VS2026

.EXAMPLE
    .\Build-Release.ps1
    # Full release build with tests and installer

.EXAMPLE
    .\Build-Release.ps1 -SkipTests
    # Build everything but skip tests

.EXAMPLE
    .\Build-Release.ps1 -Targets Ssms22
    # Build only the SSMS 22 target

.EXAMPLE
    .\Build-Release.ps1 -SkipInstaller
    # Build all DLLs and publish Engine, but don't compile installer
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipTests,

    [switch]$SkipInstaller,

    [string[]]$Targets
)

$ErrorActionPreference = 'Stop'
$sw = [System.Diagnostics.Stopwatch]::StartNew()

# ─────────────────────────────────────────────────────────────────────────────
#  Paths
# ─────────────────────────────────────────────────────────────────────────────

$repoRoot   = Split-Path -Parent $PSScriptRoot   # doc/ is one level below repo root
$srcDir     = Join-Path $repoRoot 'src'
$testDir    = Join-Path $repoRoot 'tests'
$installerDir = Join-Path $srcDir 'AkmlSql.Installer'

# Locate MSBuild — prefer vswhere (handles VS 2022 / VS 2026 / Build Tools / Insiders)
# and fall back to a fixed path list covering VS 2026 (folder "18"), VS 2022, VS 2019.
$msbuild = $null
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $found = & $vswhere -latest -prerelease -products * `
        -requires Microsoft.Component.MSBuild `
        -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null |
        Where-Object { $_ -and (Test-Path $_) } |
        Select-Object -First 1
    if ($found) { $msbuild = $found }
}
if (-not $msbuild) {
    $msbuildCandidates = @(
        # VS 2026 (version folder "18")
        "${env:ProgramFiles}\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
        "${env:ProgramFiles}\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe"
        "${env:ProgramFiles}\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
        "${env:ProgramFiles}\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe"
        # VS 2022
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
        # VS 2019 (x86 install root)
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe"
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    )
    $msbuild = $msbuildCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $msbuild) {
    Write-Error "MSBuild not found. Install Visual Studio 2022/2026 or Build Tools."
    exit 1
}
Write-Host "  Using MSBuild: $msbuild" -ForegroundColor DarkGray

# Locate Inno Setup
$isccCandidates = @(
    "${env:ProgramFiles}\Inno Setup 7\ISCC.exe"
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

# ─────────────────────────────────────────────────────────────────────────────
#  Build version — SAME formula as src/Directory.Build.props so the installer
#  version in Programs & Features, the VSIX manifests, and the AssemblyVersion
#  stamped on every DLL all agree. Formula: 1.YY.MMDD.HHmm (local +02:00).
#
#  Every segment is ≤ 65535, which VSIX/Assembly Version requires. Seeing a
#  mismatched 1.0.0 in P&F was the sign that this wasn't being passed to ISCC.
# ─────────────────────────────────────────────────────────────────────────────
$buildTime  = [System.DateTime]::UtcNow.AddHours(2)
$buildVersion = '1.{0}.{1}.{2}' -f `
    $buildTime.ToString('yy'), `
    $buildTime.ToString('MMdd'), `
    $buildTime.ToString('HHmm')

# All supported shell targets
$allTargets = @('Ssms22', 'VS2026')
if ($Targets -and $Targets.Count -gt 0) {
    $buildTargets = $Targets
} else {
    $buildTargets = $allTargets
}

# ─────────────────────────────────────────────────────────────────────────────
#  Helpers
# ─────────────────────────────────────────────────────────────────────────────

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "===========================================================" -ForegroundColor Cyan
    Write-Host "  $msg" -ForegroundColor Cyan
    Write-Host "===========================================================" -ForegroundColor Cyan
}

function Write-Ok([string]$msg)   { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "  [WARN] $msg" -ForegroundColor Yellow }
function Write-Fail([string]$msg) { Write-Host "  [FAIL] $msg" -ForegroundColor Red }

$script:errors = @()

# ─────────────────────────────────────────────────────────────────────────────
#  Step 1: Clean obj/bin for shell projects
# ─────────────────────────────────────────────────────────────────────────────

Write-Step "Step 1/7: Cleaning shell project obj/bin folders"

foreach ($target in $allTargets) {
    $projDir = Join-Path $srcDir "AkmlSql.$target"
    foreach ($dir in @('obj', 'bin')) {
        $path = Join-Path $projDir $dir
        if (Test-Path $path) {
            Remove-Item -Recurse -Force $path
        }
    }
}
# Also clean Core obj/bin to avoid stale netstandard2.0 artifacts
foreach ($dir in @('obj', 'bin')) {
    $path = Join-Path $srcDir "AkmlSql.Core\$dir"
    if (Test-Path $path) { Remove-Item -Recurse -Force $path }
}
Write-Ok "Cleaned obj/bin for all shell + Core projects"

# ─────────────────────────────────────────────────────────────────────────────
#  Step 2: Restore NuGet packages
# ─────────────────────────────────────────────────────────────────────────────

Write-Step "Step 2/7: Restoring NuGet packages"

foreach ($target in $buildTargets) {
    $csproj = Join-Path $srcDir "AkmlSql.$target\AkmlSql.$target.csproj"
    Write-Host "  Restoring AkmlSql.$target..." -NoNewline
    & $msbuild $csproj -t:Restore -p:Configuration=$Configuration -v:quiet 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Fail " FAILED"
        $script:errors += "NuGet restore failed for AkmlSql.$target"
    } else {
        Write-Host " OK" -ForegroundColor Green
    }
}

# Restore dotnet projects
foreach ($proj in @('AkmlSql.Engine', 'AkmlSql.Updater', 'AkmlSql.Formatter', 'AkmlSql.Analyzer')) {
    $csproj = Join-Path $srcDir "$proj\$proj.csproj"
    if (Test-Path $csproj) {
        Write-Host "  Restoring $proj..." -NoNewline
        dotnet restore $csproj --verbosity quiet 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Fail " FAILED"
            $script:errors += "NuGet restore failed for $proj"
        } else {
            Write-Host " OK" -ForegroundColor Green
        }
    }
}

# ─────────────────────────────────────────────────────────────────────────────
#  Step 3: Build shell extensions (one by one — prevents VSCT CTO cross-contamination)
# ─────────────────────────────────────────────────────────────────────────────

Write-Step "Step 3/7: Building shell extensions ($Configuration)"

foreach ($target in $buildTargets) {
    $csproj = Join-Path $srcDir "AkmlSql.$target\AkmlSql.$target.csproj"
    Write-Host "  Building AkmlSql.$target..." -NoNewline

    $output = & $msbuild $csproj -t:Build -p:Configuration=$Configuration -v:quiet 2>&1
    # Match real MSBuild error codes (e.g. ": error CS0579:") and skip the
    # literal word "Error" inside warning text like NU1900 vulnerability fetch.
    $buildErrors = $output | Select-String ': error [A-Z]+\d+: '

    if ($LASTEXITCODE -ne 0 -or $buildErrors) {
        Write-Fail " FAILED"
        $buildErrors | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        $script:errors += "Build failed for AkmlSql.$target"
    } else {
        Write-Host " OK" -ForegroundColor Green
    }

    # Clean obj after each build to prevent CTO cross-contamination
    $objDir = Join-Path $srcDir "AkmlSql.$target\obj"
    if (Test-Path $objDir) { Remove-Item -Recurse -Force $objDir }
}

# ─────────────────────────────────────────────────────────────────────────────
#  Step 4: Publish Engine, Updater, Formatter CLI, Analyzer CLI
# ─────────────────────────────────────────────────────────────────────────────

Write-Step "Step 4/7: Publishing .NET 10 projects"

# Drain any lingering MSBuild/VBCSCompiler workers from prior runs so they don't
# hold file handles that race with real-time AV scans on the freshly-copied
# obj/Release/.../singlefilehost.exe.
dotnet build-server shutdown 2>&1 | Out-Null

$publishProjects = @(
    @{ Name = 'AkmlSql.Engine';    Rid = 'win-x64'; Desc = 'Engine (out-of-process IntelliSense)' }
    @{ Name = 'AkmlSql.Updater';   Rid = 'win-x64'; Desc = 'Self-contained updater' }
    @{ Name = 'AkmlSql.Formatter'; Rid = 'win-x64'; Desc = 'Formatter CLI (akmlsql-format)' }
    @{ Name = 'AkmlSql.Analyzer';  Rid = 'win-x64'; Desc = 'Analyzer CLI (static analysis)' }
)

foreach ($proj in $publishProjects) {
    $csproj = Join-Path $srcDir "$($proj.Name)\$($proj.Name).csproj"
    if (-not (Test-Path $csproj)) {
        Write-Warn "$($proj.Name) not found, skipping"
        continue
    }

    Write-Host "  Publishing $($proj.Desc)..." -NoNewline

    # Retry up to 3 times — real-time AV (e.g. Bitdefender) holds transient
    # read-locks on the freshly-copied obj/.../singlefilehost.exe that defeat
    # the SDK's in-process RetryUtil.RetryOnIOError. A coarse process-relaunch
    # gives the AV scan queue time to drain.
    $attempt = 0
    $maxAttempts = 3
    while ($true) {
        $attempt++
        $output = dotnet publish $csproj -c $Configuration -r $proj.Rid --verbosity quiet 2>&1
        if ($LASTEXITCODE -eq 0) { break }
        if ($attempt -ge $maxAttempts) {
            Write-Fail " FAILED (after $maxAttempts attempts)"
            $output | Select-String 'error' | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
            $script:errors += "Publish failed for $($proj.Name)"
            break
        }
        Write-Host " retry $attempt..." -NoNewline -ForegroundColor Yellow
        Start-Sleep -Seconds 2
    }
    if ($LASTEXITCODE -eq 0) {
        Write-Host " OK" -ForegroundColor Green
    }

    # Let AV finish scanning this project's output before the next publish
    # touches the same obj/.../win-x64 copy path.
    Start-Sleep -Milliseconds 750
}

# ─────────────────────────────────────────────────────────────────────────────
#  Step 5: Run unit tests
# ─────────────────────────────────────────────────────────────────────────────

if (-not $SkipTests) {
    Write-Step "Step 5/7: Running unit tests"

    $testProjects = @(
        'AkmlSql.Core.Tests'
        'AkmlSql.Formatting.Tests'
        'AkmlSql.Engine.Tests'
    )

    $totalPassed = 0
    $totalFailed = 0

    foreach ($tp in $testProjects) {
        $csproj = Join-Path $testDir "$tp\$tp.csproj"
        if (-not (Test-Path $csproj)) {
            Write-Warn "$tp not found, skipping"
            continue
        }

        Write-Host "  Testing $tp..." -NoNewline
        $ErrorActionPreference = 'SilentlyContinue'
        $output = dotnet test $csproj -c $Configuration --verbosity quiet 2>&1 | ForEach-Object ToString
        $ErrorActionPreference = 'Stop'
        $outputText = $output -join "`n"

        # Match the summary line: "Failed:     N, Passed:   N, ... Total:   N"
        if ($outputText -match 'Failed:\s+(\d+),\s+Passed:\s+(\d+)') {
            $failed = [int]$Matches[1]
            $passed = [int]$Matches[2]
            $totalFailed += $failed
            $totalPassed += $passed
            if ($failed -gt 0) {
                Write-Fail " $passed passed, $failed FAILED"
            } else {
                Write-Host " $passed passed" -ForegroundColor Green
            }
        } elseif ($outputText -match 'Passed:\s+(\d+)') {
            $passed = [int]$Matches[1]
            $totalPassed += $passed
            Write-Host " $passed passed" -ForegroundColor Green
        } else {
            Write-Warn " Could not parse results"
        }
    }

    Write-Host ""
    Write-Host "  Total: $totalPassed passed, $totalFailed failed" -ForegroundColor $(if ($totalFailed -gt 0) { 'Red' } else { 'Green' })
} else {
    Write-Step "Step 5/7: Skipping tests (-SkipTests)"
}

# ─────────────────────────────────────────────────────────────────────────────
#  Step 6: Compile Inno Setup installer
# ─────────────────────────────────────────────────────────────────────────────

if (-not $SkipInstaller) {
    Write-Step "Step 6/7: Compiling Inno Setup installer"

    if (-not $iscc) {
        Write-Warn "Inno Setup not found. Install from https://jrsoftware.org/isinfo.php"
        Write-Warn "Skipping installer compilation."
        $script:errors += "Inno Setup (ISCC.exe) not found"
    } else {
        $issFile = Join-Path $installerDir 'AkmlSqlSetup.iss'
        Write-Host "  Compiling $issFile (version=$buildVersion)..." -NoNewline

        # Pass MyAppVersion via /D so it overrides the #ifndef fallback in the
        # .iss. This is what makes "Programs and Features" show the real build
        # version instead of the 1.0.0 placeholder.
        $output = & $iscc "/DMyAppVersion=$buildVersion" $issFile 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Fail " FAILED"
            $output | Select-String 'Error' | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
            $script:errors += "Inno Setup compilation failed"
        } else {
            Write-Host " OK" -ForegroundColor Green
        }
    }
} else {
    Write-Step "Step 6/7: Skipping installer (-SkipInstaller)"
}

# ─────────────────────────────────────────────────────────────────────────────
#  Step 7: Summary
# ─────────────────────────────────────────────────────────────────────────────

$sw.Stop()
Write-Step "Step 7/7: Build Summary"

Write-Host ""
Write-Host "  Configuration:  $Configuration"
Write-Host "  Targets built:  $($buildTargets -join ', ')"
Write-Host "  Duration:       $([math]::Round($sw.Elapsed.TotalSeconds, 1))s"
Write-Host ""

# Show output artifacts
Write-Host "  Output Artifacts:" -ForegroundColor White
$artifacts = @(
    @{ Path = (Join-Path $installerDir 'Output\AKMLSQLSetup.exe'); Label = 'Installer' }
    @{ Path = (Join-Path $srcDir 'AkmlSql.Engine\bin\Release\net10.0\win-x64\publish\AkmlSql.Engine.exe'); Label = 'Engine' }
    @{ Path = (Join-Path $srcDir 'AkmlSql.Updater\bin\Release\net10.0\win-x64\publish\AkmlSql.Updater.exe'); Label = 'Updater' }
    @{ Path = (Join-Path $srcDir 'AkmlSql.Formatter\bin\Release\net10.0\win-x64\publish\akmlsql-format.exe'); Label = 'Formatter CLI' }
    @{ Path = (Join-Path $srcDir 'AkmlSql.Analyzer\bin\Release\net10.0\win-x64\publish\AkmlSql.Analyzer.exe'); Label = 'Analyzer CLI' }
)

foreach ($a in $artifacts) {
    if (Test-Path $a.Path) {
        $size = [math]::Round((Get-Item $a.Path).Length / 1MB, 1)
        Write-Ok "$($a.Label): $($a.Path) ($size MB)"
    } else {
        Write-Warn "$($a.Label): Not found"
    }
}

# Shell DLLs
Write-Host ""
Write-Host "  Shell Extensions:" -ForegroundColor White
foreach ($target in $buildTargets) {
    $dll = Join-Path $srcDir "AkmlSql.$target\bin\$Configuration\net472\AkmlSql.$target.dll"
    if (Test-Path $dll) {
        $size = [math]::Round((Get-Item $dll).Length / 1KB, 0)
        Write-Ok "AkmlSql.$target.dll ($size KB)"
    } else {
        Write-Warn "AkmlSql.$target.dll: Not found"
    }
}

# Final status
Write-Host ""
if ($script:errors.Count -gt 0) {
    Write-Fail "BUILD COMPLETED WITH $($script:errors.Count) ERROR(S):"
    foreach ($err in $script:errors) {
        Write-Host "    - $err" -ForegroundColor Red
    }
    exit 1
} else {
    Write-Ok "BUILD SUCCEEDED - All components built successfully"
    $installerExe = Join-Path $installerDir 'Output\AKMLSQLSetup.exe'
    if (-not $SkipInstaller -and $iscc -and (Test-Path $installerExe)) {
        Write-Host ""
        Write-Host "  Installer ready at:" -ForegroundColor White
        Write-Host "    $installerExe" -ForegroundColor Green
    }
    exit 0
}
