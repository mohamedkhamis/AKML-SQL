<#
.SYNOPSIS
    Downloads / refreshes the MaxMind GeoLite2 database the site uses for country and region
    reporting.

.DESCRIPTION
    The site resolves a visitor's country from their IP offline, against a GeoLite2 .mmdb file.
    That file is NOT in source control: MaxMind requires a (free) account and licence key, and
    the data is refreshed weekly, so committing a copy would ship stale data under a licence
    that does not permit redistribution.

    Without the file the site still works — visits are simply recorded with no location, and the
    admin dashboard says so. This script is therefore optional, and safe to re-run: it downloads
    to a temp file and only replaces the live database once the download has been verified.

    To get a licence key (free):
      1. Sign up at https://www.maxmind.com/en/geolite2/signup
      2. Manage License Keys -> Generate new licence key
      3. Pass it as -LicenseKey, or set the MAXMIND_LICENSE_KEY environment variable.

    Schedule it monthly to keep the data current, e.g.:
      schtasks /create /tn "AKML GeoIP refresh" /sc monthly /ru SYSTEM ^
        /tr "powershell -NoProfile -File C:\Repos\AKML\AKML-SQL\scripts\update-geoip.ps1"

.PARAMETER LicenseKey
    MaxMind licence key. Falls back to $env:MAXMIND_LICENSE_KEY.

.PARAMETER AccountId
    MaxMind account id. Falls back to $env:MAXMIND_ACCOUNT_ID. Required by the download endpoint.

.PARAMETER Edition
    GeoLite2-City (country + region + city + timezone) or GeoLite2-Country (country only).
    City is the default: the extra detail is what makes the region and timezone columns useful.

.PARAMETER DestinationPath
    Where to write the .mmdb. Defaults to the location the site reads when
    Analytics:GeoDatabasePath is empty.

.EXAMPLE
    powershell -NoProfile -File scripts\update-geoip.ps1 -AccountId 123456 -LicenseKey abc123
#>
[CmdletBinding()]
param(
    [string] $LicenseKey = $env:MAXMIND_LICENSE_KEY,
    [string] $AccountId = $env:MAXMIND_ACCOUNT_ID,
    [ValidateSet('GeoLite2-City', 'GeoLite2-Country')]
    [string] $Edition = 'GeoLite2-City',
    [string] $DestinationPath = (Join-Path $env:ProgramData 'AKML SQL Site\GeoLite2-City.mmdb')
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($LicenseKey) -or [string]::IsNullOrWhiteSpace($AccountId)) {
    Write-Host 'MaxMind credentials not supplied.' -ForegroundColor Yellow
    Write-Host '  The site runs fine without a geo database -- visits are recorded without a location'
    Write-Host '  and the admin dashboard shows "No geo database installed".'
    Write-Host ''
    Write-Host '  To enable country/region reporting:'
    Write-Host '    1. Free account: https://www.maxmind.com/en/geolite2/signup'
    Write-Host '    2. Manage License Keys -> Generate new licence key'
    Write-Host '    3. Re-run:  update-geoip.ps1 -AccountId <id> -LicenseKey <key>'
    exit 2
}

$tempDir = Join-Path $env:TEMP ("geoip-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

try {
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

    $mmdb = Get-ChildItem $tempDir -Recurse -Filter '*.mmdb' | Select-Object -First 1
    if (-not $mmdb) { throw 'No .mmdb found in the downloaded archive.' }

    $destDir = Split-Path -Parent $DestinationPath
    New-Item -ItemType Directory -Force -Path $destDir | Out-Null

    # Replace in one move so the site never observes a half-written file. The reader holds the
    # old file open until the app pool recycles, which is why the move must not be a copy.
    Move-Item -Path $mmdb.FullName -Destination $DestinationPath -Force
    $size = [math]::Round((Get-Item $DestinationPath).Length / 1MB, 1)
    Write-Host "Wrote $DestinationPath ($size MB)." -ForegroundColor Green
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
