#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Spec 021 (web edition) -- M4 task T084 + T085. Provisions the IIS site that
    serves the AKML SQL Web bundle.

.DESCRIPTION
    Creates (or updates) the IIS site `AkmlSqlWeb` bound to localhost on the
    chosen port, sets MIME types for .wasm / .dat / .blat / .br, applies
    Cache-Control: no-cache for the framework files, and writes the
    Content-Security-Policy header per contracts/ai-key-wrapping.md "Threat
    model".

.PARAMETER Port
    The TCP port to bind. The installer page enforces 1024..65535.

.PARAMETER PhysicalPath
    The physical path of the site root (`%ProgramFiles%\AKML SQL\Web`).

.NOTES
    Invoked by web-installer.iss with elevated privileges. Writes diagnostics
    to %CommonAppData%\AKML SQL Web\install.log.
#>
param(
    [Parameter(Mandatory = $true)] [int] $Port,
    [Parameter(Mandatory = $true)] [string] $PhysicalPath,
    # Spec 026 (M4 closure) M6: 'Lan' widens the CSP connect-src so the browser may open the
    # cross-host bridge socket (wss://<machineA>:<bridgePort>); 'Localhost' keeps the tight list.
    [ValidateSet('Localhost', 'Lan')] [string] $Mode = 'Localhost'
)

$ErrorActionPreference = 'Stop'
$logRoot = Join-Path $env:ProgramData 'AKML SQL Web'
$logFile = Join-Path $logRoot 'install.log'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

# Spec 026 (M4 closure) follow-up: this script exits 0 even on failure (IIS problems must never
# abort the install), so the installer can't tell from the exit code whether the site was created.
# We write a success marker only on the success path below; the installer's Web_PostInstall warns in
# INSTALL-SUMMARY.txt when IIS hosting was requested but this marker is absent. Cleared up front so a
# failed re-run can't leave a stale "ok".
$okMarker = Join-Path $logRoot 'iis-site.ok'
Remove-Item $okMarker -Force -ErrorAction SilentlyContinue

function Log {
    param([string] $msg)
    $line = '[{0}] [iis] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $msg
    Add-Content -Path $logFile -Value $line -Encoding UTF8
}

try {
    Log "Starting IIS setup. Port=$Port PhysicalPath=$PhysicalPath"

    # Check IIS is installed.
    if (-not (Get-Module -ListAvailable -Name WebAdministration)) {
        Log 'WebAdministration module not found -- IIS may not be installed. Skipping IIS setup.'
        Write-Host 'IIS does not appear to be installed. The web bundle is in place; the user can serve it however they like.'
        exit 0
    }

    Import-Module WebAdministration -ErrorAction Stop

    # Ensure the application pool exists with No Managed Code (the bundle is
    # static -- no .NET runtime needed in IIS itself).
    $appPoolName = 'AkmlSqlWeb'
    if (-not (Test-Path "IIS:\AppPools\$appPoolName")) {
        New-WebAppPool -Name $appPoolName -Force | Out-Null
        Log "Created application pool $appPoolName"
    }
    Set-ItemProperty "IIS:\AppPools\$appPoolName" -Name managedRuntimeVersion -Value ''
    Set-ItemProperty "IIS:\AppPools\$appPoolName" -Name enable32BitAppOnWin64 -Value $false

    # Remove any existing site bound to the same port (re-run case).
    $existing = Get-Website | Where-Object { $_.Name -eq 'AkmlSqlWeb' }
    if ($null -ne $existing) {
        Remove-Website -Name 'AkmlSqlWeb' -ErrorAction SilentlyContinue
        Log 'Removed previous AkmlSqlWeb site (re-run install).'
    }

    New-Website `
        -Name 'AkmlSqlWeb' `
        -PhysicalPath $PhysicalPath `
        -Port $Port `
        -HostHeader '' `
        -ApplicationPool $appPoolName `
        -Force | Out-Null
    Log "Created IIS site AkmlSqlWeb on port $Port"

    # MIME types -- IIS doesn't know about .wasm / .blat / .br by default.
    $mimes = @{
        '.wasm' = 'application/wasm'
        '.dat'  = 'application/octet-stream'
        '.blat' = 'application/octet-stream'
        '.br'   = 'application/octet-stream'
        '.dll'  = 'application/octet-stream'
    }
    foreach ($ext in $mimes.Keys) {
        $mime = $mimes[$ext]
        # Remove any pre-existing handler for this extension, then add ours.
        Remove-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST/AkmlSqlWeb" `
            -Filter "system.webServer/staticContent" `
            -Name "." -AtElement @{fileExtension = $ext} -ErrorAction SilentlyContinue
        Add-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST/AkmlSqlWeb" `
            -Filter "system.webServer/staticContent" `
            -Name "." -Value @{fileExtension = $ext; mimeType = $mime}
    }
    Log "Configured $($mimes.Count) MIME types"

    # CSP header per contracts/ai-key-wrapping.md "Threat model". connect-src lists the AI provider
    # allow-list + the bridge sockets. Spec 026 (M4 closure) M6: in LAN mode the browser on a SECOND
    # machine loads the bundle from http://<machineA>/ and must open wss://<machineA>:<bridgePort> --
    # a host that is neither localhost nor 127.0.0.1, which the localhost-only list forbids (so the
    # bridge connection was blocked and LAN pairing/US2 could never start). For LAN installs we add
    # the `wss:` scheme-source so any TLS WebSocket host is permitted (the bridge still requires the
    # pairing PIN/bearer + a pinned cert, so this is not a meaningful CSP relaxation). Localhost
    # installs keep the tight host-locked list.
    $bridgeConnectSrc = "ws://127.0.0.1:* wss://127.0.0.1:* ws://localhost:* wss://localhost:* "
    if ($Mode -eq 'Lan') {
        $bridgeConnectSrc += "wss: "
    }
    # CodeMirror is vendored locally (wwwroot/lib/codemirror/akml-cm.js), so script-src no longer
    # needs the esm.sh CDN — everything loads from 'self'. 'wasm-unsafe-eval' stays for the Blazor
    # WASM runtime; style-src keeps 'unsafe-inline' for CodeMirror's injected editor styles.
    $csp = "default-src 'self'; " +
           "script-src 'self' 'wasm-unsafe-eval'; " +
           "style-src 'self' 'unsafe-inline'; " +
           "img-src 'self' data:; " +
           "connect-src 'self' " +
               $bridgeConnectSrc +
               "https://api.openai.com https://api.anthropic.com " +
               "https://generativelanguage.googleapis.com https://*.openai.azure.com " +
               "http://localhost:11434 http://127.0.0.1:11434 " +
               "http://localhost:1234 http://127.0.0.1:1234;"
    Add-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST/AkmlSqlWeb" `
        -Filter "system.webServer/httpProtocol/customHeaders" `
        -Name "." -Value @{name = 'Content-Security-Policy'; value = $csp}
    Log "Applied CSP header"

    # Cache-Control: no-cache for framework files so a re-install picks up the
    # new WASM hash on first refresh.
    Set-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST/AkmlSqlWeb" `
        -Filter "system.webServer/staticContent/clientCache" `
        -Name "cacheControlMode" -Value "DisableCache"

    Log "IIS setup complete."
    Set-Content -Path $okMarker -Value "AkmlSqlWeb on port $Port" -Encoding UTF8 -Force
    exit 0
}
catch {
    Log "ERROR: $_"
    Write-Host "IIS setup failed: $_"
    # Don't fail the installer on IIS issues -- the user can install IIS later
    # and re-run.
    exit 0
}
