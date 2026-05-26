#Requires -Version 5.1
<#
.SYNOPSIS
    Resolve $version$ tokens in the source.extension.vsixmanifest files for the
    supported hosts (SSMS 22, VS 2026) and write the resolved copies to
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
    'AkmlSql.Ssms22', 'AkmlSql.VS2026'
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
}

Write-Host "Resolved $($targets.Count) VSIX manifests @ version $Version"
