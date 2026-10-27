<#
.SYNOPSIS
    Applies the web hosting firewall plan. Dot-source for Get-FirewallPlan only.
#>
param(
    [switch] $Apply,
    [ValidateSet('Service', 'IIS', 'None')] [string] $WebHost = 'Service',
    [ValidateSet('Localhost', 'Lan')] [string] $Exposure = 'Localhost',
    [int] $EnginePort = 47291,
    [int] $HttpsPort = 443,
    # Retained while the existing installer and uninstall logs still call this interface.
    [int] $Port,
    [ValidateSet('Add', 'Remove')] [string] $Action,
    [string] $RuleName = 'AKML SQL Web Engine'
)

function Get-FirewallPlan {
    param(
        [ValidateSet('Service', 'IIS', 'None')] [string] $WebHost,
        [ValidateSet('Localhost', 'Lan')] [string] $Exposure,
        [int] $EnginePort,
        [int] $HttpsPort
    )
    [pscustomobject]@{
        Name = 'AKML SQL Web Engine'
        Port = $EnginePort
        Action = if ($Exposure -eq 'Lan') { 'Add' } else { 'Remove' }
    }
    [pscustomobject]@{
        Name = 'AKML SQL Web Site (HTTPS)'
        Port = $HttpsPort
        Action = if ($Exposure -eq 'Lan' -and $WebHost -eq 'IIS') { 'Add' } else { 'Remove' }
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Error 'Firewall configuration requires administrator rights.'
        exit 1
    }
    $logRoot = Join-Path $env:ProgramData 'AKML SQL Web'
    New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
    $logFile = Join-Path $logRoot 'install.log'
    try {
        $plan = if ($Apply) {
            Get-FirewallPlan -WebHost $WebHost -Exposure $Exposure -EnginePort $EnginePort -HttpsPort $HttpsPort
        } elseif ($Action) {
            [pscustomobject]@{ Name = $RuleName; Port = $Port; Action = $Action }
        } else { throw 'Specify -Apply with the hosting settings.' }
        foreach ($rule in $plan) {
            if ($rule.Action -eq 'Add' -and ($rule.Port -lt 1 -or $rule.Port -gt 65535)) { throw 'Port must be between 1 and 65535.' }
            $result = & netsh advfirewall firewall delete rule "name=$($rule.Name)" 2>&1
            if ($rule.Action -eq 'Add') {
                $result = & netsh advfirewall firewall add rule "name=$($rule.Name)" dir=in action=allow protocol=TCP "localport=$($rule.Port)" profile=any "description=Allow inbound connections to $($rule.Name)" 2>&1
                if ($LASTEXITCODE -ne 0) { throw "Could not add $($rule.Name): $result" }
            }
            Add-Content -LiteralPath $logFile -Encoding UTF8 -Value "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] [firewall] $($rule.Action) $($rule.Name): $result"
        }
        exit 0
    }
    catch {
        Add-Content -LiteralPath $logFile -Encoding UTF8 -Value "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] [firewall] ERROR: $_"
        Write-Host "Firewall setup failed: $_"
        exit 1
    }
}
