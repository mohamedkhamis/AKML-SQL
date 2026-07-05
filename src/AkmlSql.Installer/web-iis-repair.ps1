<#
.SYNOPSIS
    Spec 030 (closure follow-up). "Repair AKML SQL Web hosting" -- re-runs the IIS site
    provisioning after a first install where IIS was not yet fully functional.

.DESCRIPTION
    web-iis-setup.ps1 deliberately exits 0 even when IIS provisioning fails (IIS problems must
    never abort the installer), so an install performed BEFORE IIS was fully registered silently
    leaves no AkmlSqlWeb site -- the classic "I enabled IIS but the site isn't there" case. This
    script recreates the site once IIS is working, with no arguments to remember: it reads the
    port / mode / web-root chosen at install from HKLM\Software\AKML SQL\Web and hands them to
    web-iis-setup.ps1 (which lives next to this file). It self-elevates and reports the result in
    a console window so a Start-menu shortcut can run it directly.

.NOTES
    Installed to {app}\Support alongside web-iis-setup.ps1. Safe to run repeatedly (idempotent --
    web-iis-setup.ps1 removes and recreates the AkmlSqlWeb site each run).
    ASCII-only on purpose: Windows PowerShell 5.1 reads a no-BOM script as ANSI, so non-ASCII
    characters would corrupt string parsing.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# --- Self-elevate ---------------------------------------------------------------------------
# Creating an IIS site needs admin. If we aren't elevated, relaunch this same script via UAC and
# exit the non-elevated instance.
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    try {
        Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`""
        )
    } catch {
        Write-Host "Elevation was cancelled. Re-run 'Repair AKML SQL Web hosting' and accept the prompt." -ForegroundColor Yellow
        Start-Sleep -Seconds 4
    }
    return
}

function Read-RegString([string]$name, [string]$fallback) {
    try {
        $v = (Get-ItemProperty -Path 'HKLM:\Software\AKML SQL\Web' -Name $name -ErrorAction Stop).$name
        if ($null -ne $v -and "$v" -ne '') { return "$v" }
    } catch { }
    return $fallback
}

Write-Host ''
Write-Host '  AKML SQL - Repair Web hosting' -ForegroundColor Cyan
Write-Host '  ============================='
Write-Host ''

# --- Recover the install-time settings (with sensible fallbacks) ----------------------------
$defaultRoot = Join-Path ${env:ProgramFiles(x86)} 'AKML SQL\Web'
if (-not (Test-Path $defaultRoot)) { $defaultRoot = Join-Path $env:ProgramFiles 'AKML SQL\Web' }

$port = Read-RegString 'IisPort'  '80'
$mode = Read-RegString 'IisMode'  'Localhost'
$root = Read-RegString 'WebRoot'  $defaultRoot
if ($mode -ne 'Lan' -and $mode -ne 'Localhost') { $mode = 'Localhost' }

$setup = Join-Path $PSScriptRoot 'web-iis-setup.ps1'

Write-Host "  Port         : $port"
Write-Host "  Mode         : $mode"
Write-Host "  Web root     : $root"
Write-Host "  Provisioner  : $setup"
Write-Host ''

# --- Preconditions --------------------------------------------------------------------------
if (-not (Test-Path $setup)) {
    Write-Host "  ERROR: web-iis-setup.ps1 not found next to this script. Reinstall AKML SQL Web." -ForegroundColor Red
    Read-Host '  Press Enter to close'
    return
}
if (-not (Test-Path (Join-Path $root 'index.html'))) {
    Write-Host "  WARNING: no web bundle found at '$root' (index.html missing)." -ForegroundColor Yellow
    Write-Host "  The site will be created but will serve nothing until the bundle is present." -ForegroundColor Yellow
    Write-Host ''
}
if (-not (Get-Module -ListAvailable -Name WebAdministration)) {
    Write-Host "  IIS does not appear to be installed (WebAdministration module missing)." -ForegroundColor Yellow
    Write-Host "  Enable 'Internet Information Services' (incl. Management Tools) via Windows Features," -ForegroundColor Yellow
    Write-Host "  then run this repair again." -ForegroundColor Yellow
    Write-Host ''
    Read-Host '  Press Enter to close'
    return
}

# --- Provision ------------------------------------------------------------------------------
Write-Host '  Creating the AkmlSqlWeb IIS site...' -ForegroundColor Gray
& $setup -Port ([int]$port) -PhysicalPath $root -Mode $mode

# --- Report (web-iis-setup.ps1 writes the ok-marker only on success) ------------------------
$marker = Join-Path $env:ProgramData 'AKML SQL Web\iis-site.ok'
$log    = Join-Path $env:ProgramData 'AKML SQL Web\install.log'
Write-Host ''
if (Test-Path $marker) {
    if ($port -eq '80') { $url = 'http://localhost/' } else { $url = "http://localhost:$port/" }
    Write-Host "  SUCCESS - the AkmlSqlWeb site is provisioned." -ForegroundColor Green
    Write-Host "  Browse to: $url" -ForegroundColor Green
} else {
    Write-Host "  The site could not be created. See the log for details:" -ForegroundColor Red
    Write-Host "  $log" -ForegroundColor Red
}
Write-Host ''
Read-Host '  Press Enter to close'
