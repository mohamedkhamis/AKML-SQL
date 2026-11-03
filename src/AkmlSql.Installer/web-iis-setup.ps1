#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Spec 021 (web edition) -- M4 task T084 + T085. Provisions the IIS site that
    serves the AKML SQL Web bundle.

.DESCRIPTION
    Creates (or updates) the IIS site `AkmlSqlWeb` bound to localhost on the
    chosen port, sets MIME types for .wasm / .dat / .blat / .br, applies
    Cache-Control: no-cache for the framework files, writes the
    Content-Security-Policy header per contracts/ai-key-wrapping.md "Threat
    model", and serves index.html for unknown paths so deep links load.

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
    [ValidateSet('Localhost', 'Lan')] [string] $Mode = 'Localhost',
    # Review WEB-03: in LAN mode other computers load the app over HTTPS on this port, with the
    # engine's certificate (web-tls-setup.ps1 creates it first). 0 = no HTTPS binding (localhost:
    # http://localhost is already a secure context, so pairing tokens and API keys can be stored).
    [int] $HttpsPort = 0
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
# Re-review REV-03: the same for the site's HTTPS binding (LAN mode): ok, or the reason it was not made.
$httpsOkMarker = Join-Path $logRoot 'https-site.ok'
$httpsFailMarker = Join-Path $logRoot 'https-site.failed'
Remove-Item $httpsOkMarker, $httpsFailMarker -Force -ErrorAction SilentlyContinue
$httpsFailure = $null
. (Join-Path $PSScriptRoot 'web-https-binding.ps1')

function Log {
    param([string] $msg)
    $line = '[{0}] [iis] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $msg
    Add-Content -Path $logFile -Value $line -Encoding UTF8
}

# The IIS config COM classes behind WebAdministration (AppHostAdminManager, CLSID {688EEEE5-...})
# are registered for 64-bit only: in a redirected PowerShell every IIS: drive touch dies with
# 0x80040154 REGDB_E_CLASSNOTREG even when IIS is fully installed. That is exactly what a redirected
# installer gets when it launches a bare 'powershell.exe' (file-system redirection hands it the
# SysWOW64 host), so every install "failed" here while a manually-run 64-bit console succeeded.
# Relaunch ourselves in the native host and mirror its exit code; 'sysnative' is only visible
# from redirected processes, which is precisely when it is needed.
if (-not [Environment]::Is64BitProcess -and [Environment]::Is64BitOperatingSystem) {
    Log 'redirected PowerShell detected -- relaunching in the 64-bit host (IIS config COM classes are 64-bit only).'
    & (Join-Path $env:WINDIR 'sysnative\WindowsPowerShell\v1.0\powershell.exe') `
        -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath `
        -Port $Port -PhysicalPath $PhysicalPath -Mode $Mode -HttpsPort $HttpsPort
    exit $LASTEXITCODE
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

    # Review WEB-03: a LAN browser must get the page itself over TLS. Over plain HTTP anyone on the
    # path can replace the script that handles the pairing token and the AI keys (wss to the bridge
    # does not protect the page), and the browser has no Web Crypto outside a secure context, so it
    # could not store them anyway. The site gets an HTTPS binding with the bridge's certificate,
    # whose names already cover this computer's host names and addresses.
    #
    # Re-review REV-03: the certificate binding belongs to the IP:port in HTTP.sys, not to this site, so
    # it is only created or replaced when the port is free or already AKML's (web-https-binding.ps1);
    # another application's binding is never touched. A change of port, or a reinstall in localhost
    # mode, removes the binding AKML recorded -- if it is still AKML's.
    $record = Read-HttpsRecord
    if ($null -ne $record -and -not ($Mode -eq 'Lan' -and $HttpsPort -gt 0 -and [int]$record.Port -eq $HttpsPort)) {
        Log (Remove-OwnedHttpsBinding)
        $record = $null
    }
    if ($Mode -eq 'Lan' -and $HttpsPort -gt 0) {
        $siteCert = Get-ChildItem 'Cert:\LocalMachine\My' |
                    Where-Object { $_.Subject -eq 'CN=AKML SQL Web Engine' -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
                    Sort-Object NotAfter -Descending | Select-Object -First 1
        $otherSites = @(Get-Website | Where-Object {
                $_.Name -ne 'AkmlSqlWeb' -and @($_.bindings.Collection | Where-Object {
                    $_.protocol -eq 'https' -and $_.bindingInformation -match (':' + $HttpsPort + ':') }).Count -gt 0
            } | ForEach-Object { $_.Name })
        if ($null -eq $siteCert) {
            $httpsFailure = 'No AKML SQL Web Engine certificate was found (web-tls-setup.ps1 did not run or failed).'
        }
        else {
            $decision = Get-HttpsBindingDecision -ExistingHash (Read-BoundHash $HttpsPort) -OurThumbprint $siteCert.Thumbprint `
                -Record $record -OtherSites $otherSites -Port $HttpsPort
            Log "HTTPS port ${HttpsPort}: $($decision.Action) -- $($decision.Reason)"
            if ($decision.Action -eq 'Refuse') {
                $httpsFailure = $decision.Reason
            }
            else {
                New-WebBinding -Name 'AkmlSqlWeb' -Protocol https -Port $HttpsPort -IPAddress '*' | Out-Null
                # Only AKML's own binding is ever replaced here (Rebind); a free port has nothing to delete.
                if ($decision.Action -eq 'Rebind') { & netsh http delete sslcert "ipport=0.0.0.0:$HttpsPort" 2>$null | Out-Null }
                (Get-WebBinding -Name 'AkmlSqlWeb' -Protocol https -Port $HttpsPort).AddSslCertificate($siteCert.Thumbprint, 'My')
                if ((Read-BoundHash $HttpsPort) -ine $siteCert.Thumbprint) {
                    $httpsFailure = "The certificate could not be bound to port $HttpsPort."
                }
                else {
                    Write-HttpsRecord -Port $HttpsPort -Thumbprint $siteCert.Thumbprint
                    Set-Content -Path $httpsOkMarker -Value "https on port $HttpsPort" -Encoding UTF8 -Force
                    Log "Added HTTPS binding on port $HttpsPort with certificate $($siteCert.Thumbprint)"
                }
            }
        }
        if ($httpsFailure) {
            # The installer shows this in INSTALL-SUMMARY.txt instead of an https:// address that does not work.
            Set-Content -Path $httpsFailMarker -Value $httpsFailure -Encoding UTF8 -Force
            Log "WARNING: no HTTPS for other computers -- $httpsFailure"
        }
    }

    # MIME types -- IIS doesn't know about .wasm / .blat / .br by default.
    $mimes = @{
        '.sql'  = 'text/plain'
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
    # bridge connection was blocked and LAN pairing/US2 could never start). The `wss:` scheme-source
    # permits any TLS WebSocket host (a remote bridge still requires the pairing PIN/bearer + a
    # pinned cert, so this is not a meaningful CSP relaxation).
    #
    # It is added in BOTH modes. A localhost install is exactly the one a user pairs FROM when the
    # engine they want is on another machine; without `wss:` the browser blocked that connection
    # before it left the machine and reported only "WebSocket connect failed", which looked like a
    # network or certificate fault on the remote side. Plaintext `ws:` stays loopback-only.
    $bridgeConnectSrc = "ws://127.0.0.1:* wss://127.0.0.1:* ws://localhost:* wss://localhost:* wss: "
    # CodeMirror is vendored locally (wwwroot/lib/codemirror/akml-cm.js), so script-src no longer
    # needs the esm.sh CDN -- everything loads from 'self'. 'wasm-unsafe-eval' stays for the Blazor
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
    # Remove any pre-existing CSP header first so a re-run (installer re-run or the "Repair AKML SQL
    # Web hosting" shortcut) doesn't fail with "Cannot add duplicate collection entry ...
    # 'Content-Security-Policy'": Remove-Website above does NOT clear the site's <location> block in
    # applicationHost.config, so the header survives and Add- would create a duplicate.
    Remove-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST/AkmlSqlWeb" `
        -Filter "system.webServer/httpProtocol/customHeaders" `
        -Name "." -AtElement @{name = 'Content-Security-Policy'} -ErrorAction SilentlyContinue
    Add-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST/AkmlSqlWeb" `
        -Filter "system.webServer/httpProtocol/customHeaders" `
        -Name "." -Value @{name = 'Content-Security-Policy'; value = $csp}
    Log "Applied CSP header"

    # Cache-Control: no-cache for framework files so a re-install picks up the
    # new WASM hash on first refresh.
    Set-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST/AkmlSqlWeb" `
        -Filter "system.webServer/staticContent/clientCache" `
        -Name "cacheControlMode" -Value "DisableCache"

    # Deep links. The app's pages (/editor, /settings/connections, /history ...) are routes inside
    # index.html, not files, so reloading any page but / -- or opening a bookmark or a link from the
    # docs -- got IIS's 404. The usual fix is a URL Rewrite rule, but that module is not part of IIS
    # and is missing on most servers, so use httpErrors instead: an unknown path executes /index.html
    # (the status stays 404, which browsers render and Blazor routes like any other load). errorMode
    # Custom, because the default DetailedLocalOnly would still show the 404 page to a browser on this
    # computer. Remove-then-add for the same re-run reason as the CSP header above.
    Remove-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST/AkmlSqlWeb" `
        -Filter "system.webServer/httpErrors" `
        -Name "." -AtElement @{statusCode = 404} -ErrorAction SilentlyContinue
    Add-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST/AkmlSqlWeb" `
        -Filter "system.webServer/httpErrors" `
        -Name "." -Value @{statusCode = 404; subStatusCode = -1; path = '/index.html'; responseMode = 'ExecuteURL'}
    Set-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST/AkmlSqlWeb" `
        -Filter "system.webServer/httpErrors" `
        -Name "errorMode" -Value "Custom"
    Log "Configured the index.html fallback for deep links"

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
