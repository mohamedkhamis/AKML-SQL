#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Deploys the AKML SQL product site (src/AkmlSql.Site, ASP.NET Core static SSR)
    to local IIS and secures it with a TLS certificate.

.DESCRIPTION
    Idempotent deploy pipeline:
      0. Release staging: if the installer build at -ReleaseExe (default
         src\AkmlSql.Installer\Output\AKMLSQLSetup.exe) has a version that is not
         yet in wwwroot\releases.json, copy it to the downloads folder as a
         versioned file and prepend a release entry (real SHA-256, size, date).
         Re-runs are no-ops. -SkipRelease disables this.
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

.PARAMETER ReleaseExe
    Path to the installer exe to stage as a release. Default: the build output
    src\AkmlSql.Installer\Output\AKMLSQLSetup.exe. Older versioned files are kept
    in the downloads folder so previous releases stay downloadable.

.PARAMETER NotesSummary
    Release-notes summary for the new entry. Default: generic line with version + size.

.PARAMETER SkipRelease
    Deploy the site without staging a new release.

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
    [string] $DownloadsPath = 'C:\inetpub\akml.khamis.work-downloads',
    [string] $Configuration = 'Release',
    [string] $WacsPath = 'C:\win-acme\wacs.exe',
    [string] $ReleaseExe = '',
    [string] $NotesSummary = '',
    [string] $GitHubRepo = 'mohamedkhamis/AKML-SQL',
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch] $SkipRelease,
    [switch] $SkipCdn,
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

    # --- 0. Release staging -------------------------------------------------
    # A new installer build becomes a site release automatically: versioned copy
    # into the downloads folder + prepended entry in wwwroot\releases.json (the
    # publish below then carries the updated manifest). Idempotent per version.
    if ($SkipRelease) {
        Log 'SkipRelease set -- releases.json untouched'
    }
    else {
        if ([string]::IsNullOrWhiteSpace($ReleaseExe)) {
            $ReleaseExe = Join-Path $RepoRoot 'src\AkmlSql.Installer\Output\AKMLSQLSetup.exe'
        }
        if (-not (Test-Path $ReleaseExe)) {
            Log "No installer at $ReleaseExe -- skipping release staging"
        }
        else {
            $version = (Get-Item $ReleaseExe).VersionInfo.FileVersion.Trim()
            $manifestPath = Join-Path $RepoRoot 'src\AkmlSql.Site\wwwroot\releases.json'
            $manifest = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $already = @($manifest.releases | Where-Object { $_.version -eq $version })
            if ($already.Count -gt 0) {
                Log "Release $version already staged -- skipping"
            }
            else {
                New-Item -ItemType Directory -Force -Path $DownloadsPath | Out-Null
                $fileName = "AKMLSQLSetup-$version.exe"
                Copy-Item $ReleaseExe (Join-Path $DownloadsPath $fileName) -Force
                $sha = (Get-FileHash $ReleaseExe -Algorithm SHA256).Hash.ToLower()
                $sizeMb = [math]::Round((Get-Item $ReleaseExe).Length / 1MB, 2)
                $notes = if (-not [string]::IsNullOrWhiteSpace($NotesSummary)) { $NotesSummary } else { "AKML SQL $version installer for SSMS 22 and Visual Studio 2026 ($sizeMb MB)." }

                # CDN upload (GitHub Releases = free binary CDN). Non-fatal: on any failure the
                # site keeps serving the file from /dl locally. -SkipCdn disables.
                $cdnUrl = $null
                if ($SkipCdn) {
                    Log 'SkipCdn set -- no GitHub upload'
                }
                else {
                    $gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
                    if (-not $gh -and (Test-Path 'C:\Program Files\GitHub CLI\gh.exe')) { $gh = 'C:\Program Files\GitHub CLI\gh.exe' }
                    if (-not $gh) {
                        Log 'WARNING: gh CLI not found -- skipping CDN upload (local /dl still serves the file)'
                    }
                    else {
                        # gh writes "release not found" to stderr for a missing tag; with
                        # EAP=Stop the 2>&1 wrapper turns that into a TERMINATING error before
                        # the LASTEXITCODE check can branch to create. Relax the preference for
                        # these native calls — $LASTEXITCODE decides the outcome (same pattern
                        # as ISCC in Deploy-Build-Release.ps1).
                        $prevEap = $ErrorActionPreference
                        $ErrorActionPreference = 'Continue'
                        $tag = "v$version"
                        $stagedFile = Join-Path $DownloadsPath $fileName
                        & $gh release view $tag --repo $GitHubRepo 2>&1 | Out-Null
                        if ($LASTEXITCODE -eq 0) {
                            Log "GitHub release $tag exists -- uploading asset (clobber)"
                            & $gh release upload $tag $stagedFile --repo $GitHubRepo --clobber 2>&1 | ForEach-Object { Log "gh: $_" }
                        }
                        else {
                            Log "Creating GitHub release $tag + uploading asset"
                            & $gh release create $tag $stagedFile --repo $GitHubRepo --title "AKML SQL $version" --notes $notes 2>&1 | ForEach-Object { Log "gh: $_" }
                        }
                        $ghExit = $LASTEXITCODE
                        $ErrorActionPreference = $prevEap
                        if ($ghExit -eq 0) {
                            $cdnUrl = "https://github.com/$GitHubRepo/releases/download/$tag/$fileName"
                            Log "CDN asset live: $cdnUrl"
                        }
                        else {
                            Log "WARNING: GitHub upload failed (auth? run 'gh auth login' once) -- local /dl still serves the file"
                        }
                    }
                }

                $entry = [PSCustomObject]@{
                    version          = $version
                    releasedAt       = (Get-Date).ToString('yyyy-MM-dd')
                    supportedHosts   = @('SSMS 22', 'VS 2026')
                    downloadUrl      = "downloads/$fileName"
                    sha256Hash       = $sha
                    releaseNotesUrl  = 'https://github.com/mohamedkhamis/AKML-SQL/releases'
                    notesSummary     = $notes
                    minimumOsVersion = '10.0'
                    cdnUrl           = $cdnUrl
                }
                $manifest.generatedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
                $manifest.releases = @($entry) + @($manifest.releases)
                ($manifest | ConvertTo-Json -Depth 6) | Set-Content $manifestPath -Encoding UTF8

                # Spec 036 US5 / FR-036: the updater manifest is emitted from the SAME $entry
                # object — one write, two files, no second computation of version or hash, so the
                # download page and the update channel cannot drift. It MUST stay in this staging
                # block: MapStaticAssets resolves its asset list at build time, so a manifest
                # dropped into wwwroot after the publish below would 404 silently.
                # downloadUrl is always absolute (the updater cannot resolve the site-relative
                # 'downloads/...' path): prefer the CDN asset, fall back to the site's /dl route.
                $updateManifest = [PSCustomObject]@{
                    version          = $entry.version
                    downloadUrl      = $(if ($null -ne $entry.cdnUrl) { $entry.cdnUrl } else { "https://$HostName/dl/$fileName" })
                    releaseNotesUrl  = $entry.releaseNotesUrl
                    minimumOsVersion = $entry.minimumOsVersion
                    sha256Hash       = $entry.sha256Hash
                }
                ($updateManifest | ConvertTo-Json -Depth 4) | Set-Content (Join-Path $RepoRoot 'src\AkmlSql.Site\wwwroot\update-manifest.json') -Encoding UTF8
                Log "Update manifest written for $version (downloadUrl $($updateManifest.downloadUrl))"

                Log "Staged release $version -> $fileName (sha256 $($sha.Substring(0,12))..., $sizeMb MB)"
            }
        }
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
    $downloadsDir = $DownloadsPath
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
