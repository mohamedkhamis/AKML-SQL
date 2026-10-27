<#
.SYNOPSIS
    Checks that the selected address serves the web edition, without changing hosting settings.
#>
param(
    [string] $Url,
    [switch] $ExpectEngine,
    [switch] $ExpectBundle,
    [ValidateRange(1, 300)] [int] $TimeoutSeconds = 30,
    [string] $ReasonPath = ''
)

function Get-RegisteredPortHolder {
    param([string] $Text, [int] $Port)
    $images = foreach ($block in [regex]::Split($Text, '(?m)^(?=\S)')) {
        $matchesPort = $false
        foreach ($match in [regex]::Matches($block, 'https?://[^\s]+', 'IgnoreCase')) {
            $portMatch = [regex]::Match($match.Value, '^https?://(?:\[[^\]]+\]|[^:/]+):([0-9]+)(?:/|$)', 'IgnoreCase')
            if ($portMatch.Success -and [int] $portMatch.Groups[1].Value -eq $Port) {
                $matchesPort = $true
            }
        }
        if ($matchesPort) {
            foreach ($match in [regex]::Matches($block, '(?:[A-Za-z]:\\|\\\\)[^\r\n]*?\.exe\b', 'IgnoreCase')) {
                $match.Value
            }
        }
    }
    (@($images | Select-Object -Unique) -join ', ')
}

if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    if ($ExpectEngine -and $ExpectBundle) { throw 'Choose -ExpectEngine or -ExpectBundle.' }
    $target = [uri] $Url
    if (-not $target.IsAbsoluteUri -or $target.Scheme -notin @('http', 'https')) { throw 'Url must be an HTTP or HTTPS address.' }
    # A per-request callback avoids changing certificate validation for other requests.
    if (-not ('AkmlHostingCheckTls' -as [type])) {
        Add-Type -TypeDefinition @'
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
public static class AkmlHostingCheckTls {
    public static readonly RemoteCertificateValidationCallback Accept =
        delegate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors errors) { return true; };
}
'@
    }
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $answered = $false
    $reason = ''
    do {
        $request = [Net.HttpWebRequest]::Create($target)
        $remaining = [Math]::Max(1, [int] ($deadline - [DateTime]::UtcNow).TotalMilliseconds)
        $request.Method = 'GET'
        $request.Proxy = $null
        $request.AllowAutoRedirect = $false
        $request.Timeout = $remaining
        $request.ReadWriteTimeout = $remaining
        $request.ServerCertificateValidationCallback = [AkmlHostingCheckTls]::Accept
        $response = $null
        $reader = $null
        try {
            $response = $request.GetResponse()
            $answered = $true
            $reader = New-Object IO.StreamReader($response.GetResponseStream())
            $body = New-Object Text.StringBuilder
            $buffer = New-Object char[] 4096
            while ($body.Length -lt 1048576 -and [DateTime]::UtcNow -lt $deadline) {
                $reader.BaseStream.ReadTimeout = [Math]::Max(1, [int] ($deadline - [DateTime]::UtcNow).TotalMilliseconds)
                $count = $reader.Read($buffer, 0, $buffer.Length)
                if ($count -eq 0) { break }
                [void] $body.Append($buffer, 0, $count)
            }
            $valid = [int] $response.StatusCode -eq 200 -and $response.ContentType -match '^text/html(?:;|$)'
            if ($ExpectEngine) { $valid = $valid -and $response.Headers['X-AKML-Host'] -eq 'engine' }
            if ($ExpectBundle) { $valid = $valid -and $body.ToString().Contains('_framework/blazor.webassembly.js') }
            if ($valid) { $reason = ''; break }
            $reason = 'The address answered but is not serving the web edition.'
        }
        catch [Net.WebException] {
            if ($null -ne $_.Exception.Response) {
                $answered = $true
                $_.Exception.Response.Close()
            }
            $reason = if ($answered) { 'The address answered but is not serving the web edition.' } else { 'The address did not answer.' }
        }
        catch { $reason = 'The address did not return the web edition before the deadline.' }
        finally {
            if ($reader) { $reader.Dispose() }
            if ($response) { $response.Close() }
            $request.ServerCertificateValidationCallback = $null
            $request.Abort()
        }
        $remaining = [int] ($deadline - [DateTime]::UtcNow).TotalMilliseconds
        if ($remaining -gt 0) { Start-Sleep -Milliseconds ([Math]::Min(1000, $remaining)) }
    } while ([DateTime]::UtcNow -lt $deadline)

    $exitCode = if ([string]::IsNullOrEmpty($reason)) { 0 } elseif ($answered) { 2 } else { 1 }
    if ($exitCode -ne 0) {
        try {
            $holder = Get-RegisteredPortHolder -Text ((& netsh http show servicestate view=requestq 2>$null) -join "`n") -Port $target.Port
            if ($holder) { $reason += " Port $($target.Port) is registered by $holder." }
        } catch { }
        if ($ReasonPath) { Set-Content -LiteralPath $ReasonPath -Encoding UTF8 -Value $reason }
        Write-Host $reason
    }
    try {
        $logRoot = Join-Path $env:ProgramData 'AKML SQL Web'
        New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
        Add-Content -LiteralPath (Join-Path $logRoot 'install.log') -Encoding UTF8 -Value "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] [hosting-check] Exit $exitCode. $reason"
    } catch { }
    exit $exitCode
}
