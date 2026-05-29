#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Spec 025 (M3 bridge closure) FR-027 / T008. Writes the WebSocket-bridge
    section into the engine's config.json so EngineHost.RunAsync starts a
    WebSocketTransport alongside the existing NamedPipeTransport.

    Idempotent: re-running overwrites the previous `bridge` section without
    touching any other settings. The merge preserves all other top-level keys
    so existing IDE-plugin engine config stays valid.

.PARAMETER Port
    The TCP port the WebSocket transport binds to. Same value the installer
    passed to web-iis-setup.ps1 / web-tls-setup.ps1 / web-firewall.ps1.

.PARAMETER Mode
    "Localhost" (BindAddress=127.0.0.1, no TLS) or "Lan" (BindAddress=0.0.0.0,
    TLS cert required).

.PARAMETER TlsCertPath
    Absolute path to the PFX. Required for Lan mode (web-tls-setup.ps1 writes
    it to %ProgramData%\AKML SQL Web\certs\bridge.pfx); ignored for Localhost.

.PARAMETER ConfigPath
    Absolute path to the engine config.json the AkmlSqlWebEngine service reads.
    The installer always passes %CommonAppData%\AKML SQL Web\config.json. When
    omitted it defaults to that same web-edition path -- NEVER the per-user
    IDE-plugin config (%AppData%\AKML SQL\config.json) -- so this script can never
    mutate IDE-plugin state (spec 026 M4 closure C3 / FR-006 / SC-007).

.NOTES
    Writes atomically: temp file + rename. Mirrors ConfigManager.Save in
    src/AkmlSql.Core/Config/ConfigManager.cs.
#>
param(
    [Parameter(Mandatory = $true)] [int] $Port,
    [Parameter(Mandatory = $true)] [ValidateSet('Localhost', 'Lan')] [string] $Mode,
    [string] $TlsCertPath = '',
    [string] $ConfigPath = ''
)

$ErrorActionPreference = 'Stop'
$logRoot = Join-Path $env:ProgramData 'AKML SQL Web'
$logFile = Join-Path $logRoot 'install.log'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

function Log {
    param([string] $msg)
    $line = '[{0}] [config-bridge] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $msg
    Add-Content -Path $logFile -Value $line -Encoding UTF8
}

try {
    if ([string]::IsNullOrEmpty($ConfigPath)) {
        # Spec 026 (M4 closure) C3 defense-in-depth: default to the web-edition config, NOT the
        # per-user IDE-plugin config, so a caller that forgets -ConfigPath cannot clobber IDE state.
        $ConfigPath = Join-Path $env:ProgramData 'AKML SQL Web\config.json'
    }
    $configDir = Split-Path -Parent $ConfigPath
    New-Item -ItemType Directory -Force -Path $configDir | Out-Null

    if ($Mode -eq 'Lan' -and [string]::IsNullOrEmpty($TlsCertPath)) {
        # web-tls-setup.ps1 generates `bridge.cer` (the public part); the private
        # key is NonExportable in LocalMachine\My, so no PFX file is written. The
        # engine's ValidateCertBindingOrThrow handles either format (CER or PFX);
        # we point at the CER because that's what the installer actually produces.
        $TlsCertPath = Join-Path $env:ProgramData 'AKML SQL Web\certs\bridge.cer'
    }

    $bridgeSection = [ordered]@{
        enabled            = $true
        bindAddress        = if ($Mode -eq 'Lan') { '0.0.0.0' } else { '127.0.0.1' }
        port               = $Port
        tlsCertPath        = if ($Mode -eq 'Lan') { $TlsCertPath } else { '' }
        tlsCertPasswordRef = $null
        tokenStorePath     = Join-Path $env:ProgramData 'AKML SQL Web\tokens.json'
        tokenTtlDays       = 90
    }

    $config = $null
    if (Test-Path $ConfigPath) {
        $raw = Get-Content -Path $ConfigPath -Raw -Encoding UTF8
        if (-not [string]::IsNullOrWhiteSpace($raw)) {
            $config = $raw | ConvertFrom-Json
        }
    }
    if ($null -eq $config) {
        $config = [pscustomobject]@{ configVersion = 1 }
    }

    if ($config.PSObject.Properties.Name -contains 'bridge') {
        $config.bridge = $bridgeSection
    }
    else {
        Add-Member -InputObject $config -NotePropertyName 'bridge' -NotePropertyValue $bridgeSection
    }

    $json = $config | ConvertTo-Json -Depth 20

    # Atomic write: temp + rename (mirrors ConfigManager.Save).
    $tempPath = "$ConfigPath.tmp"
    Set-Content -Path $tempPath -Value $json -Encoding UTF8 -Force
    if (Test-Path $ConfigPath) {
        Remove-Item -Path $ConfigPath -Force
    }
    Move-Item -Path $tempPath -Destination $ConfigPath -Force

    Log "Wrote bridge section to $ConfigPath (Mode=$Mode, Port=$Port)."
    exit 0
}
catch {
    Log "ERROR: $_"
    Write-Host "Bridge config write failed: $_"
    exit 1
}
