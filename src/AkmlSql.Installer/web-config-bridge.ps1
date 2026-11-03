<#
.SYNOPSIS
    Writes the web engine's bridge configuration. Dot-source for Get-BridgeConfigJson only.
.PARAMETER WebRoot
    Public web-edition folder for standalone hosting; empty disables page serving.
.PARAMETER ConfigPath
    Defaults to the web service's ProgramData config, never the desktop user's config.
#>
param(
    [int] $Port,
    [ValidateSet('Localhost', 'Lan')] [string] $Mode = 'Localhost',
    [string] $TlsCertPath = '',
    [string] $ConfigPath = '',
    [string] $WebRoot = ''
)

function Get-BridgeConfigJson {
    param([string] $ExistingJson, [int] $Port, [string] $Mode, [string] $WebRoot = '', [string] $TlsCertPath = '')
    $config = if ([string]::IsNullOrWhiteSpace($ExistingJson)) {
        [pscustomobject]@{ configVersion = 1 }
    } else { $ExistingJson | ConvertFrom-Json -ErrorAction Stop }
    if ($null -eq $config -or $config -isnot [pscustomobject]) { throw 'The engine configuration must be a JSON object.' }
    if ($Mode -eq 'Lan' -and [string]::IsNullOrEmpty($TlsCertPath)) {
        $TlsCertPath = Join-Path $env:ProgramData 'AKML SQL Web\certs\bridge.cer'
    }
    $bridge = [ordered]@{
        enabled = $true
        bindAddress = if ($Mode -eq 'Lan') { '0.0.0.0' } else { '127.0.0.1' }
        port = $Port
        tlsCertPath = if ($Mode -eq 'Lan') { $TlsCertPath } else { '' }
        tlsCertPasswordRef = $null
        tokenStorePath = Join-Path $env:ProgramData 'AKML SQL Web\tokens.json'
        tokenTtlDays = 90
        webRoot = $WebRoot
    }
    $config | Add-Member -NotePropertyName bridge -NotePropertyValue $bridge -Force
    $config | ConvertTo-Json -Depth 20
}

if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Error 'Bridge configuration requires administrator rights.'
        exit 1
    }
    $logRoot = Join-Path $env:ProgramData 'AKML SQL Web'
    New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
    $logFile = Join-Path $logRoot 'install.log'
    try {
        if ($Port -lt 1 -or $Port -gt 65535) { throw 'Port must be between 1 and 65535.' }
        if ([string]::IsNullOrEmpty($ConfigPath)) { $ConfigPath = Join-Path $logRoot 'config.json' }
        $ConfigPath = [IO.Path]::GetFullPath($ConfigPath)
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ConfigPath) | Out-Null
        $raw = if (Test-Path -LiteralPath $ConfigPath) { Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 } else { '' }
        $json = Get-BridgeConfigJson -ExistingJson $raw -Port $Port -Mode $Mode -WebRoot $WebRoot -TlsCertPath $TlsCertPath
        $tempPath = "$ConfigPath.tmp"
        Set-Content -LiteralPath $tempPath -Value $json -Encoding UTF8 -Force
        # Windows PowerShell converts $null to an empty string for this .NET string argument.
        # NullString passes an actual null backup path and keeps replacement atomic on upgrades.
        if (Test-Path -LiteralPath $ConfigPath) { [IO.File]::Replace($tempPath, $ConfigPath, [System.Management.Automation.Language.NullString]::Value) }
        else { [IO.File]::Move($tempPath, $ConfigPath) }
        Add-Content -LiteralPath $logFile -Encoding UTF8 -Value "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] [config-bridge] Wrote bridge section (Mode=$Mode, Port=$Port)."
        exit 0
    }
    catch {
        Add-Content -LiteralPath $logFile -Encoding UTF8 -Value "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] [config-bridge] ERROR: $_"
        Write-Host "Bridge config write failed: $_"
        exit 1
    }
}
