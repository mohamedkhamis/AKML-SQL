#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Deploys the AKML SQL product site (src/AkmlSql.Site, ASP.NET Core static SSR)
    to local IIS and secures it with a TLS certificate.

.DESCRIPTION
    Idempotent deploy pipeline:
      1. dotnet publish (framework-dependent) + mirror into the deploy path
      2. App pool (No Managed Code, AlwaysRunning) + read ACL for the pool identity
      3. IIS site with http:80 host-header binding
      4. TLS cert, first match wins:
         a. existing cert in LocalMachine\My whose subject/SAN covers -HostName
         b. win-acme (Let's Encrypt) via -WacsPath (default C:\win-acme\wacs.exe),
            unattended IIS target + IIS validation + IIS installation (auto-renewal
            scheduled task is created by win-acme)
         c. -SelfSigned: New-SelfSignedCertificate (testing only, browser warning)
      5. https:443 SNI binding
      6. Smoke test against localhost with the host header

.PARAMETER HostName
    Public host name. Default akml.khamis.work.

.PARAMETER SelfSigned
    Force a self-signed cert even if win-acme is available (local testing).

.PARAMETER SkipCert
    Deploy HTTP only; do not touch certificates or the https binding.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\deploy-site-iis.ps1 -RepoRoot C:\Repos\AKML\AKML-SQL
#>
param(
    [string] $HostName = 'akml.khamis.work',
    [string] $SiteName = 'AkmlSqlSite',
    [string] $AppPoolName = 'AkmlSqlSite',
    [string] $DeployPath = 'C:\inetpub\akml.khamis.work',
    [string] $Configuration = 'Release',
    [string] $WacsPath = 'C:\win-acme\wacs.exe',
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch] $SelfSigned,
    [switch] $SkipCert
)

$ErrorActionPreference = 'Stop'
$logDir = Join-Path $env:ProgramData 'AKML SQL Site'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logFile = Join-Path $logDir 'deploy.log'

function Log {
    param([string] $msg)
    $line = '[{0}] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $msg
    Add-Content -Path $logFile -Value $line -Encoding UTF8
    Write-Host $line
}

try {
    Log "=== Deploy start: $HostName -> $DeployPath (site $SiteName) ==="

    # Docs freshness metadata (New/Updated badges): regenerate from git history so the
    # publish below carries current dates. Non-fatal: no git -> committed copy is used.
    try {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass `
            -File (Join-Path $RepoRoot 'scripts\generate-docs-metadata.ps1') -RepoRoot $RepoRoot
        if ($LASTEXITCODE -ne 0) { throw "generator exit code $LASTEXITCODE" }
        Log 'docs-metadata.json regenerated'
    }
    catch {
        Log "WARNING: docs metadata not regenerated ($_) -- using committed copy"
    }

    # --- 1. Publish ---------------------------------------------------------
    $staging = Join-Path $env:TEMP 'akmlsite-publish'
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    Log "dotnet publish -> $staging"
    & dotnet publish (Join-Path $RepoRoot 'src\AkmlSql.Site\AkmlSql.Site.csproj') -c $Configuration -o $staging | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

    New-Item -ItemType Directory -Force -Path $DeployPath | Out-Null
    # Take the app offline first: ANCM unloads the worker and releases DLL locks,
    # otherwise robocopy retries locked files (near-)forever on a live site.
    $offline = Join-Path $DeployPath 'app_offline.htm'
    Set-Content -Path $offline -Value '<!doctype html><title>Deploying</title><p>Deploying -- back in a few seconds.</p>' -Encoding UTF8
    Start-Sleep -Seconds 3
    Log "robocopy mirror -> $DeployPath"
    # /MIR deletes destination files absent from the source, so anything placed on the server by
    # hand is erased on every deploy (OPS-002 -- this is what kept wiping the admin configuration).
    # Secrets now live in the app pool's environment, but keep appsettings.Production.json excluded
    # so a hand-written override survives, and app_offline.htm excluded so the mirror does not
    # delete the marker written just above (which would bring the app back up mid-copy).
    #
    # appsettings.Development.json is deliberately NOT in this list. /XF protects a file from
    # deletion as well as from copying, so excluding it would preserve a stale copy from an older
    # deploy forever -- the opposite of OPS-003. It is no longer published, so /MIR removes it.
    $mirrorExclusions = @('appsettings.Production.json', 'app_offline.htm')
    & robocopy $staging $DeployPath /MIR /XF $mirrorExclusions /R:2 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { throw "robocopy failed ($LASTEXITCODE)" }
    Remove-Item $offline -Force -ErrorAction SilentlyContinue
    Remove-Item $staging -Recurse -Force

    # --- 2. App pool + ACL --------------------------------------------------
    Import-Module WebAdministration
    if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
        Log "Creating app pool $AppPoolName"
        New-WebAppPool -Name $AppPoolName | Out-Null
    }
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ''
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name startMode -Value 'AlwaysRunning'

    # App pool environment (OPS-001): the admin password hash lives here, not in a file under the
    # deploy path, so the robocopy mirror above can never erase it. This deploy never sets the
    # hash -- it only pins the environment name and reports whether the hash is present.
    $envFilter = "system.applicationHost/applicationPools/add[@name='$AppPoolName']/environmentVariables"
    function Set-PoolEnv {
        param([string] $Name, [string] $Value)
        $existing = Get-WebConfiguration -pspath 'MACHINE/WEBROOT/APPHOST' -filter "$envFilter/add[@name='$Name']"
        if ($existing) {
            Set-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' `
                -filter "$envFilter/add[@name='$Name']" -name 'value' -value $Value
        }
        else {
            Add-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -filter $envFilter `
                -name '.' -value @{ name = $Name; value = $Value }
        }
    }
    Set-PoolEnv -Name 'ASPNETCORE_ENVIRONMENT' -Value 'Production'

    $hashVar = Get-WebConfiguration -pspath 'MACHINE/WEBROOT/APPHOST' -filter "$envFilter/add[@name='Admin__PasswordHash']"
    if ($hashVar -and $hashVar.value) {
        Log 'Admin__PasswordHash present on the app pool -- admin portal configured'
    }
    else {
        Log 'WARNING: Admin__PasswordHash is NOT set -- the admin portal will show "not configured".'
        Log '         Generate one:  .\AkmlSql.Site.exe --hash-password'
        Log "         Then set it:   Add-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -filter `"$envFilter`" -name '.' -value @{name='Admin__PasswordHash';value='<hash>'}"
    }

    $poolIdentity = "IIS AppPool\$AppPoolName"
    Log "Granting $poolIdentity read access to $DeployPath"
    & icacls $DeployPath /grant "${poolIdentity}:(OI)(CI)RX" /T /Q | Out-Null
    # Data folders outside the app root: tracked-download files + analytics SQLite db.
    $downloadsDir = 'C:\inetpub\akml.khamis.work-downloads'
    $analyticsDir = Join-Path $env:ProgramData 'AKML SQL Site'
    foreach ($dir in @($downloadsDir, $analyticsDir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        & icacls $dir /grant "${poolIdentity}:(OI)(CI)(M)" /Q | Out-Null
    }
    Log "Data folders ready: $downloadsDir, $analyticsDir"

    # --- 3. Site + http binding ----------------------------------------------
    if (Test-Path "IIS:\Sites\$SiteName") {
        Log "Updating existing site $SiteName"
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $DeployPath
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
    }
    else {
        Log "Creating site $SiteName"
        New-Website -Name $SiteName -ApplicationPool $AppPoolName -PhysicalPath $DeployPath -Port 80 -HostHeader $HostName -Force | Out-Null
    }
    $hasHttp = Get-WebBinding -Name $SiteName -Protocol http -HostHeader $HostName -Port 80 -ErrorAction SilentlyContinue
    if (-not $hasHttp) {
        Log "Adding http:80:$HostName binding"
        New-WebBinding -Name $SiteName -Protocol http -IPAddress '*' -Port 80 -HostHeader $HostName
    }
    Start-Website -Name $SiteName
    $siteId = (Get-Website -Name $SiteName).Id
    Log "Site id = $siteId"

    # --- 4. Certificate -------------------------------------------------------
    $thumbprint = $null
    if ($SkipCert) {
        Log 'SkipCert set -- leaving https binding untouched'
    }
    elseif (Get-WebBinding -Name $SiteName -Protocol https -HostHeader $HostName -Port 443 -ErrorAction SilentlyContinue) {
        Log 'https binding already present -- cert lifecycle owned by win-acme renewal task, skipping'
    }
    else {
        $existing = Get-ChildItem Cert:\LocalMachine\My, Cert:\LocalMachine\WebHosting |
            Where-Object { $_.NotAfter -gt (Get-Date) -and ($_.Subject -like "*$HostName*" -or ($_.DnsNameList | ForEach-Object { $_.Unicode }) -contains $HostName) } |
            Sort-Object NotAfter -Descending | Select-Object -First 1
        if ($existing) {
            $thumbprint = $existing.Thumbprint
            Log "Reusing existing cert $thumbprint ($($existing.Subject))"
        }
        elseif (-not $SelfSigned -and (Test-Path $WacsPath)) {
            Log "Requesting Let's Encrypt cert via win-acme (unattended IIS/HTTP-01)"
            & $WacsPath --target iis --siteid $siteId --host $HostName `
                --validation selfhosting `
                --installation iis --installationsiteid $siteId --sslport 443 `
                --accepttos 2>&1 | ForEach-Object { Log "wacs: $_" }
            if ($LASTEXITCODE -ne 0) { throw "win-acme failed ($LASTEXITCODE) -- check port-80 reachability; re-run with -SelfSigned for local testing" }
            $issued = Get-ChildItem Cert:\LocalMachine\My |
                Where-Object { $_.Subject -like "*$HostName*" } | Sort-Object NotAfter -Descending | Select-Object -First 1
            if ($issued) { $thumbprint = $issued.Thumbprint }
            Log "win-acme done (thumbprint=$thumbprint); it also manages the https binding + renewals"
        }
        else {
            Log 'Creating SELF-SIGNED cert (testing only -- browsers will warn)'
            $cert = New-SelfSignedCertificate -DnsName $HostName -CertStoreLocation 'Cert:\LocalMachine\My' `
                -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(2)
            $thumbprint = $cert.Thumbprint
        }

        # win-acme adds the https binding itself; for existing/self-signed certs we add it.
        if ($thumbprint -and -not (Get-WebBinding -Name $SiteName -Protocol https -HostHeader $HostName -Port 443 -ErrorAction SilentlyContinue)) {
            Log "Adding https:443:$HostName binding (SNI) with $thumbprint"
            $b = New-WebBinding -Name $SiteName -Protocol https -IPAddress '*' -Port 443 -HostHeader $HostName -SslFlags 1
            $b.AddSslCertificate($thumbprint, 'My')
        }
    }

    # --- 5. Smoke test --------------------------------------------------------
    # /health reports which startup singleton is unhealthy, so a failed deploy says why (OPS-004).
    Start-Sleep -Seconds 3
    $health = & curl.exe -sL -w '\n%{http_code}' -H "Host: $HostName" 'http://localhost/health'
    $healthLines = $health -split "`n"
    $code = $healthLines[-1].Trim()
    $healthBody = ($healthLines[0..($healthLines.Count - 2)] -join '').Trim()
    Log "smoke http://localhost/health (Host: $HostName) -> $code $healthBody"
    if ($code -ne '200') { throw "smoke test failed: /health returned HTTP $code" }
    if ($healthBody -match '"status"\s*:\s*"degraded"') {
        Log 'WARNING: /health reports degraded -- check the docs corpus and releases.json'
    }

    $homeCode = & curl.exe -sL -o NUL -w '%{http_code}' -H "Host: $HostName" 'http://localhost/'
    Log "smoke http://localhost (Host: $HostName, redirects followed) -> $homeCode"
    if ($homeCode -ne '200') { throw "smoke test failed: home page returned HTTP $homeCode" }
    $httpsBound = [bool] (Get-WebBinding -Name $SiteName -Protocol https -HostHeader $HostName -Port 443 -ErrorAction SilentlyContinue)
    if ($httpsBound) {
        $scode = & curl.exe -sk -o NUL -w '%{http_code}' --resolve "${HostName}:443:127.0.0.1" "https://$HostName/"
        Log "smoke https://$HostName (local resolve) -> $scode"
    }

    Log "=== Deploy complete: http://$HostName $(if ($httpsBound) { "+ https://$HostName" }) ==="
    exit 0
}
catch {
    Log "ERROR: $_"
    exit 1
}
