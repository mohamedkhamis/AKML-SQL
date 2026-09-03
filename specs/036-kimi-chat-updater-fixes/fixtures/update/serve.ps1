#Requires -Version 5.1
<#
.SYNOPSIS
    Serves the spec-036 update fixture directory over plain local HTTP for quickstart
    scenarios 41-51 (no Python on the dev box). The HTTPS-only rule applies to the
    manifest's downloadUrl (the GitHub CDN), not to the local manifest fetch.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File serve.ps1 -Port 8099
#>
param(
    [int] $Port = 8099,
    [string] $Root = $PSScriptRoot
)

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://127.0.0.1:$Port/")
$listener.Start()
Write-Host "Serving $Root at http://127.0.0.1:$Port/ (Ctrl+C to stop)"

try {
    while ($listener.IsListening) {
        $ctx = $listener.GetContext()
        $name = $ctx.Request.Url.AbsolutePath.TrimStart('/')
        if ([string]::IsNullOrEmpty($name)) { $name = 'update-manifest.json' }
        $file = Join-Path $Root $name
        if (Test-Path $file) {
            $bytes = [System.IO.File]::ReadAllBytes($file)
            $ctx.Response.ContentType = 'application/json'
            $ctx.Response.ContentLength64 = $bytes.Length
            $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        }
        else {
            $ctx.Response.StatusCode = 404
        }
        $ctx.Response.Close()
    }
}
finally {
    $listener.Stop()
    $listener.Close()
}
