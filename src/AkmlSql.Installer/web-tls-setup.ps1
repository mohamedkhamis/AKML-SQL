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

.PARAMETER ExtraNames
    Additional names browsers will use to reach this engine, each an IP address or a DNS name.
    Needed when the machine is reached by an address it does not own itself -- a cloud VM or a
    server behind NAT whose public IP is not on any local interface, or a public DNS name. For
    example: -ExtraNames 203.0.113.10, sql.example.com

.PARAMETER RestartEngine
    Restart the AkmlSqlWebEngine service afterwards, so a manual re-run takes effect at once.
    The installer does not pass it (it starts the service itself).

.EXAMPLE
    # Add the server's public IP to an existing install, then restart the engine:
    & "C:\Program Files\AKML SQL\Support\web-tls-setup.ps1" -Port 59417 `
        -PfxPath "C:\ProgramData\AKML SQL Web\certs\bridge.pfx" -ExtraNames 203.0.113.10 -RestartEngine

.NOTES
    IP addresses are written as IP-address SAN entries. They used to go in as DNS names
    (New-SelfSignedCertificate -DnsName), which Chrome and Edge never match against an IP in the
    URL -- so even a certificate installed as trusted failed for wss://<ip>:<port>. A cert that is
    missing any required name, or carries an IP only as a DNS entry, is replaced.

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
    [Parameter(Mandatory = $true)] [string] $PfxPath,
    [string[]] $ExtraNames = @(),
    [switch] $RestartEngine
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

    # The names browsers may use: hostname + FQDN (DNS), every local IPv4 that isn't loopback or
    # link-local (IP), and any -ExtraNames (typed by what they parse as).
    $hostname = [System.Net.Dns]::GetHostName()
    $fqdn = ([System.Net.Dns]::GetHostEntry($hostname)).HostName
    $localIps = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                Where-Object { $_.IPAddress -notmatch '^127\.' -and $_.IPAddress -notmatch '^169\.254\.' } |
                Select-Object -ExpandProperty IPAddress

    $wanted = New-Object System.Collections.Generic.List[string]
    foreach ($name in @($hostname, $fqdn) + @($localIps) + @($ExtraNames)) {
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        $n = $name.Trim()
        $ip = $null
        if ([System.Net.IPAddress]::TryParse($n, [ref] $ip)) { $entry = "IPAddress=$($ip.ToString())" }
        else { $entry = "DNS=$($n.ToLowerInvariant())" }
        if (-not $wanted.Contains($entry)) { $wanted.Add($entry) }
    }
    Log "Required SAN entries: $($wanted -join ', ')"

    # DER length at $pos (short or long form); advances $pos past it.
    function Read-DerLength([byte[]] $bytes, [ref] $pos) {
        $first = [int] $bytes[$pos.Value]; $pos.Value++
        if ($first -lt 0x80) { return $first }
        $len = 0
        for ($k = 0; $k -lt ($first -band 0x7F); $k++) { $len = ($len * 256) + $bytes[$pos.Value]; $pos.Value++ }
        return $len
    }

    # The SAN entries a cert actually carries, in the same "DNS=x" / "IPAddress=y" form. Read from
    # the extension's DER bytes: X509Extension.Format() is localized ("DNS Name=" only on English
    # Windows), and misreading it would replace the cert -- and break every pinned browser -- on
    # every re-install.
    function Get-SanEntries($c) {
        $ext = $c.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.17' } | Select-Object -First 1
        if ($null -eq $ext) { return @() }
        [byte[]] $b = $ext.RawData
        if ($b.Length -lt 2 -or $b[0] -ne 0x30) { return @() }
        $i = 1
        $null = Read-DerLength $b ([ref] $i)
        $entries = @()
        while ($i -lt $b.Length) {
            $tag = $b[$i]; $i++
            $len = Read-DerLength $b ([ref] $i)
            if ($len -gt 0 -and ($i + $len) -le $b.Length) {
                [byte[]] $value = $b[$i..($i + $len - 1)]
                if ($tag -eq 0x82) {            # [2] dNSName
                    $entries += 'DNS=' + [System.Text.Encoding]::ASCII.GetString($value).ToLowerInvariant()
                }
                elseif ($tag -eq 0x87 -and ($len -eq 4 -or $len -eq 16)) {   # [7] iPAddress
                    $entries += 'IPAddress=' + ([System.Net.IPAddress]::new($value)).ToString()
                }
            }
            $i += $len
        }
        return $entries
    }

    # Reuse the existing bridge cert when it is still healthy (private key + >30 days left) AND
    # covers every required name with the right type. Reuse keeps the thumbprint paired browsers
    # pinned; a cert that cannot validate for one of the addresses is not worth keeping.
    $renewAfter = (Get-Date).AddDays(30)
    $cert = Get-ChildItem 'Cert:\LocalMachine\My' |
            Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotAfter -gt $renewAfter } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1

    if ($null -ne $cert) {
        $have = @(Get-SanEntries $cert)
        $missing = @($wanted | Where-Object { $have -notcontains $_ })
        if ($missing.Count -gt 0) {
            Log "Existing bridge cert $($cert.Thumbprint) lacks SAN entries: $($missing -join ', ') -- replacing it"
            # Keep what it already had (e.g. an -ExtraNames from an earlier run) in the new cert.
            foreach ($entry in $have) { if (-not $wanted.Contains($entry)) { $wanted.Add($entry) } }
            $cert = $null
        }
        else {
            Log "Reusing existing bridge cert. Thumbprint=$($cert.Thumbprint); expires $($cert.NotAfter.ToString('yyyy-MM-dd'))"
        }
    }

    if ($null -eq $cert) {
        Log "Generating self-signed cert for port $Port"

        # SAN through -TextExtension, not -DnsName: -DnsName writes every value as a DNS name,
        # including IP addresses, and browsers only match an IP in the URL against an IP entry.
        $san = '2.5.29.17={text}' + ($wanted -join '&')
        Log "SAN extension: $san"

        # Generate the cert. NotAfter = 2 years; KeyExportPolicy = NonExportable.
        $cert = New-SelfSignedCertificate `
            -Subject $subject `
            -TextExtension @($san) `
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

    if ($RestartEngine) {
        # The engine reads the certificate at start-up; restart it so the new one is served.
        $svc = Get-Service -Name 'AkmlSqlWebEngine' -ErrorAction SilentlyContinue
        if ($null -ne $svc) {
            Restart-Service -Name 'AkmlSqlWebEngine' -Force
            Log 'Restarted AkmlSqlWebEngine'
        }
    }

    Write-Host "Bridge certificate $thumbprint bound to port $Port for: $($wanted -join ', ')"
    exit 0
}
catch {
    Log "ERROR: $_"
    Write-Host "TLS setup failed: $_"
    exit 1
}
