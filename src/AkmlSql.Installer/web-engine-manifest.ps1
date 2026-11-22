<#
.SYNOPSIS
    Publishes the web engine's bridge port next to the web bundle. Dot-source for Get-EngineManifestJson only.
.DESCRIPTION
    Spec 047 (WEB-001, contracts/engine-pairing.md section 1). A page IIS serves is static, so the browser cannot learn which
    port setup gave the engine; the page reads akml-engine.json from its own origin instead. The file never carries a host,
    PIN, token or path: the engine is always on the page's own host. Not written for Service hosting, where the engine
    serves the page itself (and HEAD / names it); -Remove deletes a file a previous host choice left.
.PARAMETER WebRoot
    The public web-edition folder ({app}\Web).
.PARAMETER Port
    The bridge port (1024-65535).
.PARAMETER Scheme
    ws for Localhost exposure, wss for LAN exposure.
.PARAMETER Remove
    Delete akml-engine.json instead of writing it.
#>
param(
    [string] $WebRoot = '',
    [int] $Port,
    [ValidateSet('ws', 'wss')] [string] $Scheme = 'ws',
    [switch] $Remove
)

function Get-EngineManifestJson {
    param([int] $Port, [string] $Scheme)
    # Fixed key order; parsed by PageEngine (manifestVersion 1, bridgePort, scheme).
    '{"manifestVersion":1,"bridgePort":' + $Port + ',"scheme":"' + $Scheme + '"}'
}

if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    $logRoot = Join-Path $env:ProgramData 'AKML SQL Web'
    $logFile = Join-Path $logRoot 'install.log'
    function Write-ManifestLog([string] $Text) {
        try {
            New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
            Add-Content -LiteralPath $logFile -Encoding UTF8 -Value "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] [engine-manifest] $Text"
        } catch { }   # logging is best effort; the exit code is the contract
    }
    try {
        if ([string]::IsNullOrWhiteSpace($WebRoot)) { throw 'WebRoot is required.' }
        $root = [IO.Path]::GetFullPath($WebRoot)
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "The web root does not exist: $root" }
        if (((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "The web root is a reparse point and is refused: $root"
        }
        $target = Join-Path $root 'akml-engine.json'
        if ($Remove) {
            if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
            Write-ManifestLog "Removed $target (if present)."
            exit 0
        }
        if ($Port -lt 1024 -or $Port -gt 65535) { throw "The bridge port must be between 1024 and 65535 (got $Port)." }
        $json = Get-EngineManifestJson -Port $Port -Scheme $Scheme
        $temp = "$target.tmp"
        [IO.File]::WriteAllText($temp, $json, (New-Object Text.UTF8Encoding($false)))
        Move-Item -LiteralPath $temp -Destination $target -Force
        Write-ManifestLog "Wrote $target (port $Port, scheme $Scheme)."
        exit 0
    }
    catch {
        Write-ManifestLog "ERROR: $_"
        [Console]::Error.WriteLine("Engine manifest failed: $_")
        exit 1
    }
}
