<#
.SYNOPSIS
    Makes the AKML SQL web engine service restart itself if it ever stops unexpectedly.

.DESCRIPTION
    The browser cannot start a Windows service, so "wake the engine up" has to be the operating
    system's job. Out of the box the service ships with NO recovery actions configured -- `sc
    qfailure AkmlSqlWebEngine` reports an empty action list -- which means a crash is permanent
    until somebody notices and starts it by hand. That is the worst version of the problem the
    web edition's auto-connect is trying to solve: the page retries forever against something
    that is never coming back.

    This configures the standard three-strike policy:

        first failure   restart after 5 seconds
        second failure  restart after 10 seconds
        later failures  restart after 60 seconds
        failure count   resets after a day without incident

    The delays climb so a service that is genuinely broken -- a bad config, a port already taken --
    does not spin in a tight restart loop filling the event log, while a one-off crash recovers
    almost immediately.

    Safe to re-run: it sets the same policy every time.

    Note that Windows only counts a service ENDING UNEXPECTEDLY as a failure. A clean `sc stop`,
    or a stop from the Services console, is a deliberate act and is deliberately not undone.

.PARAMETER ServiceName
    Defaults to the web engine service installed by the AKML SQL installer.

.EXAMPLE
    powershell -NoProfile -File scripts\configure-engine-recovery.ps1
#>
[CmdletBinding()]
param(
    [string] $ServiceName = 'AkmlSqlWebEngine'
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
    Write-Host "Service '$ServiceName' is not installed -- nothing to configure." -ForegroundColor Yellow
    exit 2
}

# Requires elevation: changing a service's failure policy is an admin operation.
# Kept on one statement: Windows PowerShell will not continue a line onto a leading '.', so the
# tidier-looking wrapped form is a parse error.
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host 'This needs an elevated PowerShell (service failure policy is an admin setting).' -ForegroundColor Red
    exit 1
}

Write-Host "Before:" -ForegroundColor DarkGray
sc.exe qfailure $ServiceName | Write-Host

# reset=86400 -> the failure counter clears after a day without an incident.
# actions=restart/5000/restart/10000/restart/60000 -> delays in milliseconds.
$null = sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/60000
if ($LASTEXITCODE -ne 0) { throw "sc.exe failure returned $LASTEXITCODE" }

# Also restart on a non-crash exit code, which is how a .NET host that shuts itself down on an
# unhandled exception usually terminates. Without this, the most likely real failure is the one
# Windows would NOT treat as a failure.
$null = sc.exe failureflag $ServiceName 1
if ($LASTEXITCODE -ne 0) { throw "sc.exe failureflag returned $LASTEXITCODE" }

Write-Host ""
Write-Host "After:" -ForegroundColor DarkGray
sc.exe qfailure $ServiceName | Write-Host

Write-Host ""
Write-Host "Recovery configured for '$ServiceName'." -ForegroundColor Green
exit 0
