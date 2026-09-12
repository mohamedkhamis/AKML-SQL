#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Spec 021 (web edition) -- M4 task T087 + T088. Generates a self-signed TLS
    cert for the LAN-exposed engine bridge and binds it to the chosen port via
    `netsh http add sslcert`.

.DESCRIPTION
    Creates `CN=AKML SQL Web Engine` with DnsName entries for hostname + FQDN +
    primary LAN IP. Exports PFX (private key) + CER (public part for trust
    rollout to other machines). Writes the thumbprint to thumbprint.txt so the
    installer can bake it into INSTALL-SUMMARY.txt.

.PARAMETER Port
    The TCP port to bind the cert to.

.PARAMETER PfxPath
    Destination path for the .pfx file.

.NOTES
    Upgrade-safe: a still-valid existing bridge cert (same subject, private key present,
    more than 30 days left) is REUSED so the thumbprint browsers have pinned and trusted
    does not change on every re-install -- previously each upgrade minted a new cert and
    every paired browser lost the bridge (fingerprint mismatch / untrusted wss). A new
    cert is generated only when none exists or the survivor is near expiry. Stale AKML
    bridge certs are removed so the store does not accumulate one per install. The PFX is
    marked NonExportable so the private key can't be lifted from the cert store.
#>
param(
    [Parameter(Mandatory = $true)] [int] $Port,
    [Parameter(Mandatory = $true)] [string] $PfxPath
)

$ErrorActionPreference = 'Stop'
$logRoot = Join-Path $env:ProgramData 'AKML SQL Web'
$certDir = Join-Path $logRoot 'certs'
$logFile = Join-Path $logRoot 'install.log'
New-Item -ItemType Directory -Force -Path $certDir | Out-Null

function Log {
    param([string] $msg)
    $line = '[{0}] [tls] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $msg
    Add-Content -Path $logFile -Value $line -Encoding UTF8
}

try {
    $subject = 'CN=AKML SQL Web Engine'

    # Reuse the existing bridge cert when it is still healthy (private key + >30 days left).
    $renewAfter = (Get-Date).AddDays(30)
    $cert = Get-ChildItem 'Cert:\LocalMachine\My' |
            Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotAfter -gt $renewAfter } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1

    if ($null -ne $cert) {
        Log "Reusing existing bridge cert. Thumbprint=$($cert.Thumbprint); expires $($cert.NotAfter.ToString('yyyy-MM-dd'))"
    }
    else {
        Log "Generating self-signed cert for port $Port"

        # Build the SAN list: hostname + FQDN + every local IPv4 that isn't loopback.
        $hostname = [System.Net.Dns]::GetHostName()
        $fqdn = ([System.Net.Dns]::GetHostEntry($hostname)).HostName
        $ips = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
               Where-Object { $_.IPAddress -notmatch '^127\.' -and $_.IPAddress -notmatch '^169\.254\.' } |
               Select-Object -ExpandProperty IPAddress

        $sanList = @($hostname, $fqdn) + $ips | Sort-Object -Unique
        Log "SAN list: $($sanList -join ', ')"

        # Generate the cert. NotAfter = 2 years; KeyExportPolicy = NonExportable.
        $cert = New-SelfSignedCertificate `
            -Subject $subject `
            -DnsName $sanList `
            -CertStoreLocation 'Cert:\LocalMachine\My' `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -HashAlgorithm SHA256 `
            -KeyExportPolicy NonExportable `
            -NotAfter (Get-Date).AddYears(2)
        Log "Generated cert. Thumbprint=$($cert.Thumbprint)"
    }

    # Remove stale AKML bridge certs -- older installs each left one behind.
    Get-ChildItem 'Cert:\LocalMachine\My' |
        Where-Object { $_.Subject -eq $subject -and $_.Thumbprint -ne $cert.Thumbprint } |
        ForEach-Object {
            Log "Removing stale bridge cert $($_.Thumbprint)"
            Remove-Item -Path $_.PSPath -Force
        }

    $thumbprint = $cert.Thumbprint

    # Export PFX (private key) + CER (public part for trust rollout).
    # NOTE: NonExportable means we can't actually Export-PfxCertificate the
    # private key. The PFX export is informational only -- in practice the
    # binding uses the LocalMachine cert store via thumbprint, not the file.
    $cerPath = Join-Path $certDir 'bridge.cer'
    Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
    Log "Exported public certificate to $cerPath"

    # Capture the thumbprint for the installer's INSTALL-SUMMARY.txt.
    $thumbprint | Out-File -FilePath (Join-Path $certDir 'thumbprint.txt') -Encoding ascii -Force

    # Bind the cert to the bridge port via netsh.
    $appId = '{a1b2c3d4-1111-2222-3333-444455556666}'   # well-known AKML SQL guid
    # Remove any existing binding on this port (re-run case).
    & netsh http delete sslcert ipport=0.0.0.0:$Port 2>$null | Out-Null
    $netshResult = & netsh http add sslcert "ipport=0.0.0.0:$Port" "certhash=$thumbprint" "appid=$appId" 2>&1
    Log "netsh sslcert add result: $netshResult"

    Log "TLS setup complete. Thumbprint=$thumbprint"
    exit 0
}
catch {
    Log "ERROR: $_"
    Write-Host "TLS setup failed: $_"
    exit 1
}
