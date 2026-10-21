#Requires -Version 5.1
<#
.SYNOPSIS
Start, stop or reset a throwaway local PostgreSQL cluster for the spec 042 live tests.
.DESCRIPTION
Creates a separate cluster in %LOCALAPPDATA%\AKML SQL\pg-test (never touches an installed PostgreSQL
service or its data), using the binaries of an installed PostgreSQL (the newest under
C:\Program Files\PostgreSQL, or the folder in $env:PG_BIN). It listens on localhost only, on port
55432 by default, with the throwaway password "akml" and TLS enabled with a self-signed certificate
when openssl is available (needed by the verify-ca / verify-full checks, FR-101).

Start prints the connection string. Set it for the live tests:

    $env:AKML_PG_TEST_CONNECTION = 'Host=localhost;Port=55432;Database=postgres;Username=postgres;Password=akml'

Tests that need a server call WithPgOrSkipAsync and skip when the variable is unset or the server is
unreachable. Reset drops the cluster and creates a fresh one. The cluster is a development aid only:
the password is public in this script, so it must never be exposed beyond localhost.
.EXAMPLE
.\scripts\pg-test-server.ps1 -Action Start
.EXAMPLE
.\scripts\pg-test-server.ps1 -Action Stop
.EXAMPLE
.\scripts\pg-test-server.ps1 -Action Reset -Port 55433
#>
[CmdletBinding()]
param(
    [ValidateSet('Start', 'Stop', 'Status', 'Reset')][string]$Action = 'Start',
    [int]$Port = 55432,
    [string]$Root = (Join-Path $env:LOCALAPPDATA 'AKML SQL\pg-test')
)

$ErrorActionPreference = 'Stop'
$dataDir = Join-Path $Root 'data'
$logFile = Join-Path $Root 'server.log'
$password = 'akml'
$connection = "Host=localhost;Port=$Port;Database=postgres;Username=postgres;Password=$password"

function Find-PgBin {
    if ($env:PG_BIN -and (Test-Path (Join-Path $env:PG_BIN 'pg_ctl.exe'))) { return $env:PG_BIN }
    $root = 'C:\Program Files\PostgreSQL'
    if (Test-Path $root) {
        $hit = Get-ChildItem $root -Directory | Sort-Object { [int]($_.Name -replace '\D', '0') } -Descending |
            ForEach-Object { Join-Path $_.FullName 'bin' } | Where-Object { Test-Path (Join-Path $_ 'pg_ctl.exe') } | Select-Object -First 1
        if ($hit) { return $hit }
    }
    throw 'No PostgreSQL binaries found. Install PostgreSQL or set $env:PG_BIN to its bin folder.'
}

function Test-Running([string]$bin) {
    if (-not (Test-Path (Join-Path $dataDir 'postmaster.pid'))) { return $false }
    & (Join-Path $bin 'pg_ctl.exe') -D $dataDir status *> $null
    return ($LASTEXITCODE -eq 0)
}

$bin = Find-PgBin

if ($Action -eq 'Stop' -or $Action -eq 'Reset') {
    if (Test-Running $bin) { & (Join-Path $bin 'pg_ctl.exe') -D $dataDir -m fast stop | Out-Host }
    if ($Action -eq 'Stop') { return }
    if (Test-Path $dataDir) { Remove-Item $dataDir -Recurse -Force }
}

if ($Action -eq 'Status') {
    if (Test-Running $bin) { "running on port $Port`n$connection" } else { 'stopped' }
    return
}

if (-not (Test-Path (Join-Path $dataDir 'PG_VERSION'))) {
    New-Item -ItemType Directory -Force $Root | Out-Null
    $pwFile = Join-Path $Root 'pw.tmp'
    Set-Content -Path $pwFile -Value $password -Encoding ASCII
    try {
        & (Join-Path $bin 'initdb.exe') -D $dataDir -U postgres -E UTF8 --locale=C --auth=scram-sha-256 --pwfile=$pwFile | Out-Host
    } finally {
        Remove-Item $pwFile -Force -ErrorAction SilentlyContinue
    }

    $openssl = (Get-Command openssl -ErrorAction SilentlyContinue).Source
    if (-not $openssl -and (Test-Path 'C:\Program Files\Git\usr\bin\openssl.exe')) { $openssl = 'C:\Program Files\Git\usr\bin\openssl.exe' }
    if ($openssl) {
        # openssl prints its progress to stderr; Windows PowerShell 5.1 turns that into a terminating
        # error under 'Stop', so run it under 'Continue' and check for the files instead of the exit text.
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            & $openssl req -new -x509 -days 365 -nodes -text -subj '/CN=localhost' `
                -addext 'subjectAltName=DNS:localhost,IP:127.0.0.1' `
                -out (Join-Path $dataDir 'server.crt') -keyout (Join-Path $dataDir 'server.key') 2>$null | Out-Null
        } finally {
            $ErrorActionPreference = $previous
        }
        if (-not ((Test-Path (Join-Path $dataDir 'server.crt')) -and (Test-Path (Join-Path $dataDir 'server.key')))) {
            Write-Warning 'openssl did not produce a certificate; the cluster will run without TLS.'
        }
    }
}

if (-not (Test-Running $bin)) {
    $options = "-p $Port -c listen_addresses=localhost"
    if (Test-Path (Join-Path $dataDir 'server.crt')) { $options += ' -c ssl=on' }

    # pg_ctl leaves the server running as a daemon. Started with the caller's pipes it would keep them
    # open and a calling tool would wait for it forever, so give it its own redirected output and poll
    # for readiness instead of waiting on the process.
    $startOut = Join-Path $Root 'pg_ctl.out.txt'
    $startErr = Join-Path $Root 'pg_ctl.err.txt'
    $arguments = "-D `"$dataDir`" -l `"$logFile`" -o `"$options`" start"
    Start-Process -FilePath (Join-Path $bin 'pg_ctl.exe') -ArgumentList $arguments -WindowStyle Hidden `
        -RedirectStandardOutput $startOut -RedirectStandardError $startErr | Out-Null

    $ready = $false
    $isReady = Join-Path $bin 'pg_isready.exe'
    for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
        Start-Sleep -Milliseconds 500
        & $isReady -h localhost -p $Port -q
        $ready = ($LASTEXITCODE -eq 0)
    }
    if (-not $ready) { throw "PostgreSQL did not become ready on port $Port. See $logFile" }
}

"PostgreSQL test cluster is up on localhost:$Port"
"`$env:AKML_PG_TEST_CONNECTION = '$connection'"
