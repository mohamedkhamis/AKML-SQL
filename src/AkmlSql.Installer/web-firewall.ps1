#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Spec 021 (web edition) -- M4 task T089. Adds or removes a Windows Firewall
    inbound rule for the engine bridge port.

.PARAMETER Port
    The TCP port to allow / remove.

.PARAMETER Action
    "Add" (install) or "Remove" (uninstall).

.NOTES
    Idempotent. Re-run safely replaces an existing rule.
#>
param(
    [Parameter(Mandatory = $true)] [int] $Port,
    [Parameter(Mandatory = $true)] [ValidateSet('Add', 'Remove')] [string] $Action
)

$ErrorActionPreference = 'Stop'
$logRoot = Join-Path $env:ProgramData 'AKML SQL Web'
$logFile = Join-Path $logRoot 'install.log'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

function Log {
    param([string] $msg)
    $line = '[{0}] [firewall] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $msg
    Add-Content -Path $logFile -Value $line -Encoding UTF8
}

$ruleName = 'AKML SQL Web Engine'

try {
    if ($Action -eq 'Add') {
        # Drop any existing rule with the same name first (re-run case).
        & netsh advfirewall firewall delete rule "name=$ruleName" 2>$null | Out-Null

        $result = & netsh advfirewall firewall add rule `
            "name=$ruleName" `
            "dir=in" `
            "action=allow" `
            "protocol=TCP" `
            "localport=$Port" `
            "profile=any" `
            "description=Allow inbound connections to the AKML SQL Web Engine bridge" 2>&1
        Log "Added firewall rule. Result: $result"
    }
    else {
        # Action = Remove
        $result = & netsh advfirewall firewall delete rule "name=$ruleName" 2>&1
        Log "Removed firewall rule. Result: $result"
    }
    exit 0
}
catch {
    Log "ERROR: $_"
    Write-Host "Firewall setup failed: $_"
    exit 1
}
