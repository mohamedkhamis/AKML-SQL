<#
.SYNOPSIS
    Downloads / refreshes the offline country database the site uses for country reporting.

.DESCRIPTION
    The site resolves a visitor's country from their IP offline, against a GeoLite2 .mmdb file.
    That file is NOT in source control: MaxMind requires a (free) account and licence key, and
    the data is refreshed weekly, so committing a copy would ship stale data under a licence
    that does not permit redistribution.

    Without the file the site still works — visits are simply recorded with no location, and the
    admin dashboard says so. This script is therefore optional, and safe to re-run: it downloads
    to a temp file and only replaces the live database once the download has been verified.

    TWO SOURCES are supported:

      db-ip (DEFAULT, no account needed)
        DB-IP publish "IP to Country Lite" monthly in the same MaxMind-DB (.mmdb) format, free and
        without registration, under CC BY 4.0. Country-level accuracy is good and it needs no
        credentials, so it is the default -- the site ran for MONTHS with no database at all
        because obtaining a key was a manual step nobody had done, and every country read
        "Unknown" the whole time.
        Attribution is required by the licence; the site carries it on /privacy.

      maxmind (needs a free account)
        GeoLite2-Country. Refreshed weekly.
          1. Sign up at https://www.maxmind.com/en/geolite2/signup
          2. Manage License Keys -> Generate new licence key
          3. Re-run with -Source maxmind -AccountId <id> -LicenseKey <key>
             (or set MAXMIND_ACCOUNT_ID / MAXMIND_LICENSE_KEY)

    Schedule it monthly to keep the data current, e.g.:
      schtasks /create /tn "AKML GeoIP refresh" /sc monthly /ru SYSTEM ^
        /tr "powershell -NoProfile -File C:\Repos\AKML\AKML-SQL\scripts\update-geoip.ps1"

.PARAMETER LicenseKey
    MaxMind licence key. Falls back to $env:MAXMIND_LICENSE_KEY.

.PARAMETER AccountId
    MaxMind account id. Falls back to $env:MAXMIND_ACCOUNT_ID. Required by the download endpoint.

.PARAMETER Edition
    GeoLite2-Country (country only) or GeoLite2-City. Country is the default and is what the
    site uses: it is about a tenth the size, and it cannot yield city-level data, so the
    "country only" decision is enforced by the file rather than by remembering not to read it.

.PARAMETER DestinationPath
    Where to write the .mmdb. Defaults to the location the site reads when
    Analytics:GeoDatabasePath is empty.

.PARAMETER Source
    db-ip (default, no credentials) or maxmind (requires AccountId + LicenseKey).

.EXAMPLE
    powershell -NoProfile -File scripts\update-geoip.ps1
    # DB-IP country database, no account required

.EXAMPLE
    powershell -NoProfile -File scripts\update-geoip.ps1 -Source maxmind -AccountId 123456 -LicenseKey abc123
#>
[CmdletBinding()]
param(
    [string] $LicenseKey = $env:MAXMIND_LICENSE_KEY,
    [string] $AccountId = $env:MAXMIND_ACCOUNT_ID,
    [ValidateSet('GeoLite2-Country', 'GeoLite2-City')]
    [string] $Edition = 'GeoLite2-Country',
    [ValidateSet('db-ip', 'maxmind')]
    [string] $Source = 'db-ip',
    [string] $DestinationPath = (Join-Path $env:ProgramData 'AKML SQL Site\GeoLite2-Country.mmdb')
)

$ErrorActionPreference = 'Stop'

if ($Source -eq 'maxmind' -and ([string]::IsNullOrWhiteSpace($LicenseKey) -or [string]::IsNullOrWhiteSpace($AccountId))) {
    Write-Host 'MaxMind credentials not supplied.' -ForegroundColor Yellow
    Write-Host '  Either supply them:'
    Write-Host '    1. Free account: https://www.maxmind.com/en/geolite2/signup'
    Write-Host '    2. Manage License Keys -> Generate new licence key'
    Write-Host '    3. Re-run:  update-geoip.ps1 -Source maxmind -AccountId <id> -LicenseKey <key>'
    Write-Host ''
    Write-Host '  ...or use the default source, which needs no account at all:'
    Write-Host '    update-geoip.ps1'
    exit 2
}

$tempDir = Join-Path $env:TEMP ("geoip-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

try {
    if ($Source -eq 'db-ip') {
        # DB-IP publish a fresh file at the start of each month. Walk back a couple of months so a
        # run on the 1st (before that month's file is posted) still succeeds rather than failing
        # for a reason nobody would guess.
        $archive = Join-Path $tempDir 'dbip-country.mmdb.gz'
        $downloaded = $false
        foreach ($monthsBack in 0..2) {
            $stamp = (Get-Date).AddMonths(-$monthsBack).ToString('yyyy-MM')
            $uri = "https://download.db-ip.com/free/dbip-country-lite-$stamp.mmdb.gz"
            Write-Host "Trying DB-IP IP-to-Country Lite $stamp ..."
            try {
                Invoke-WebRequest -Uri $uri -OutFile $archive -UseBasicParsing -TimeoutSec 180
                $downloaded = $true
                break
            }
            catch {
                Write-Host "  not published yet ($stamp)"
            }
        }
        if (-not $downloaded) { throw 'No DB-IP monthly release could be downloaded.' }

        if ((Get-Item $archive).Length -lt 100KB) { throw 'Download looks truncated.' }

        Write-Host 'Extracting ...'
        # A bare .gz, not a tar -- Windows' tar will not take it, so decompress via .NET.
        $in = [System.IO.File]::OpenRead($archive)
        $outFile = Join-Path $tempDir 'dbip-country.mmdb'
        $out = [System.IO.File]::Create($outFile)
        $gz = New-Object System.IO.Compression.GZipStream($in, [System.IO.Compression.CompressionMode]::Decompress)
        try { $gz.CopyTo($out) } finally { $gz.Dispose(); $out.Dispose(); $in.Dispose() }
    }
    else {
        $archive = Join-Path $tempDir 'geolite2.tar.gz'
        $uri = "https://download.maxmind.com/geoip/databases/$Edition/download?suffix=tar.gz"

        Write-Host "Downloading $Edition ..."
        # Basic auth with account id + licence key is MaxMind's documented scheme for this endpoint.
        $pair = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${AccountId}:${LicenseKey}"))
        Invoke-WebRequest -Uri $uri -Headers @{ Authorization = "Basic $pair" } -OutFile $archive -UseBasicParsing

        if (-not (Test-Path $archive) -or (Get-Item $archive).Length -lt 100KB) {
            throw "Download looks truncated -- check the account id and licence key."
        }

        Write-Host 'Extracting ...'
        # tar ships with Windows 10+/Server 2019+; the archive nests the .mmdb under a dated folder.
        & tar -xzf $archive -C $tempDir
        if ($LASTEXITCODE -ne 0) { throw "tar failed ($LASTEXITCODE)" }
    }

    $mmdb = Get-ChildItem $tempDir -Recurse -Filter '*.mmdb' | Select-Object -First 1
    if (-not $mmdb) { throw 'No .mmdb found in the download.' }

    $destDir = Split-Path -Parent $DestinationPath
    New-Item -ItemType Directory -Force -Path $destDir | Out-Null

    # Replace in one move so the site never observes a half-written file. The reader holds the
    # old file open until the app pool recycles, which is why the move must not be a copy.
    Move-Item -Path $mmdb.FullName -Destination $DestinationPath -Force

    # The IIS app pool reads this file. The deploy grants (M) on the folder, but a file moved in
    # afterwards is worth ACLing explicitly -- a database the site cannot read is indistinguishable
    # from no database at all, and fails just as silently.
    $poolIdentity = 'IIS AppPool\AkmlSqlSite'
    & icacls $DestinationPath /grant "${poolIdentity}:(R)" /Q 2>&1 | Out-Null

    $size = [math]::Round((Get-Item $DestinationPath).Length / 1MB, 1)
    Write-Host "Wrote $DestinationPath ($size MB) from $Source." -ForegroundColor Green

    if ($Source -eq 'db-ip') {
        Write-Host 'DB-IP IP-to-Country Lite is CC BY 4.0 -- attribution is required and the site'
        Write-Host 'carries it on /privacy. https://db-ip.com'
    }

    Write-Host ''
    Write-Host 'Country is resolved WHEN A VISIT IS RECORDED, so this does not backfill history:'
    Write-Host 'only traffic from now on will carry a country.' -ForegroundColor Yellow
    Write-Host 'Recycle the app pool for the site to pick it up:  Restart-WebAppPool AkmlSqlSite'
    exit 0
}
catch {
    Write-Host "GeoIP update failed: $_" -ForegroundColor Red
    Write-Host 'The site continues to work without location data.'
    exit 1
}
finally {
    Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}
