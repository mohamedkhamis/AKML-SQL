#Requires -Version 5.1
<#
.SYNOPSIS
    Resolve $version$ tokens in the source.extension.vsixmanifest files for the
    supported host (SSMS 22) and write the resolved copy to
    src/AkmlSql.Installer/generated/<Target>/extension.vsixmanifest — consumed
    by AkmlSqlSetup.iss [Files] entries.

.DESCRIPTION
    Called from AkmlSqlSetup.iss via `#expr Exec` so installer compilation
    works regardless of the outer build driver (build.ps1, Deploy-Build-
    Release.ps1, or a bare ISCC invocation). Shell extension csprojs have
    CreateVsixContainer=false which disables VSSDK's automatic $version$
    substitution, so we do it here.

.PARAMETER Version
    Version string in the form Major.YY.MMDD.HHmm (all segments ≤ 65535).
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$InstallerDir = $PSScriptRoot                        # …/src/AkmlSql.Installer
$SrcRoot      = Split-Path -Parent $InstallerDir     # …/src

$targets = @(
    'AkmlSql.Ssms22'
)

foreach ($t in $targets) {
    $src    = Join-Path $SrcRoot      "$t\source.extension.vsixmanifest"
    $outDir = Join-Path $InstallerDir "generated\$t"
    $dst    = Join-Path $outDir       'extension.vsixmanifest'

    if (-not (Test-Path -LiteralPath $src)) {
        throw "Source manifest missing: $src"
    }

    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    (Get-Content -LiteralPath $src -Raw) -replace '\$version\$', $Version `
        | Set-Content -LiteralPath $dst -NoNewline -Encoding UTF8

    # The pkgdef's "PID" is the version SSMS shows for AKML SQL (Help > About, installed products).
    # The source keeps a fixed placeholder so a hand copy still registers; the installer ships this
    # copy with the build's version (it used to ship "1.0.0" for every release).
    $pkgSrc = Join-Path $SrcRoot "$t\$t.pkgdef"
    if (Test-Path -LiteralPath $pkgSrc) {
        $pkgText = Get-Content -LiteralPath $pkgSrc -Raw
        if ($pkgText -notmatch '"PID"="[^"]*"') { throw "No `"PID`" value in $pkgSrc" }
        $pkgText -replace '("PID"=")[^"]*(")', "`${1}$Version`${2}" `
            | Set-Content -LiteralPath (Join-Path $outDir "$t.pkgdef") -NoNewline -Encoding UTF8
    }
}

Write-Host "Resolved $($targets.Count) VSIX manifests and pkgdefs @ version $Version"
