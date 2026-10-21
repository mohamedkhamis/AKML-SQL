<#
.SYNOPSIS
    Re-review REV-03. Decides, and records, the site's HTTPS certificate binding so AKML SQL Web
    never takes over or removes a binding that belongs to another application.

.DESCRIPTION
    A certificate is bound in HTTP.sys per IP:port (netsh http ... sslcert ipport=0.0.0.0:443), not per
    IIS site: replacing or deleting it affects every site and program on that port. So:

      * AKML binds a port only when nothing is bound there, or when what is bound there is AKML's own
        (its current certificate, or the one it recorded binding earlier). Another IIS site with an
        https binding on the port, or another certificate on it, makes the setup refuse the port and
        say which other one to choose.
      * What AKML bound is recorded in %ProgramData%\AKML SQL Web\https-binding.json (port +
        thumbprint). Removal (uninstall, a change of port, a LAN-to-localhost reinstall) deletes a
        binding only while it still is that one.

    Dot-sourced by web-iis-setup.ps1 for its functions. Run directly with -RemoveOwned (the uninstaller
    does) it removes AKML's recorded binding if it is still AKML's, then exits.

.NOTES
    The decision functions are pure (no system access) so the installer tests can run them without
    administrator rights. ASCII-only: Windows PowerShell 5.1 reads a no-BOM script as ANSI.
#>
param(
    [switch] $RemoveOwned
)

$script:HttpsRecordPath = Join-Path (Join-Path $env:ProgramData 'AKML SQL Web') 'https-binding.json'

# The 40-hex certificate hash in `netsh http show sslcert` output. The labels are localized, the hash
# is not; the application id is a GUID with hyphens, so it never matches. Empty when nothing is bound.
function ConvertFrom-NetshSslCert([string] $Text) {
    if ([string]::IsNullOrEmpty($Text)) { return '' }
    $m = [regex]::Match($Text, '(?<![0-9A-Fa-f])[0-9A-Fa-f]{40}(?![0-9A-Fa-f])')
    if ($m.Success) { return $m.Value.ToUpperInvariant() }
    return ''
}

# What to do with the port. Returns @{ Action = 'Bind' | 'Rebind' | 'Refuse'; Reason = '...' }.
#   ExistingHash   the certificate hash bound at 0.0.0.0:<Port> now, '' when none
#   OurThumbprint  the AKML SQL Web Engine certificate's thumbprint
#   Record         AKML's recorded binding (@{ Port; Thumbprint }) or $null
#   OtherSites     names of other IIS sites with an https binding on the port
function Get-HttpsBindingDecision {
    param([string] $ExistingHash, [string] $OurThumbprint, $Record, [string[]] $OtherSites, [int] $Port)
    if ($OtherSites -and $OtherSites.Count -gt 0) {
        return @{ Action = 'Refuse'; Reason = "Port $Port is used by the IIS site '$($OtherSites -join "', '")'. Choose another secure site port." }
    }
    if ([string]::IsNullOrEmpty($ExistingHash)) {
        return @{ Action = 'Bind'; Reason = "Nothing is bound to port $Port." }
    }
    if ($ExistingHash -ieq $OurThumbprint) {
        return @{ Action = 'Rebind'; Reason = "Port $Port already carries the AKML SQL Web certificate." }
    }
    if ($null -ne $Record -and [int]$Record.Port -eq $Port -and ([string]$Record.Thumbprint) -ieq $ExistingHash) {
        return @{ Action = 'Rebind'; Reason = "Port $Port carries the certificate AKML SQL Web bound earlier." }
    }
    return @{ Action = 'Refuse'; Reason = "Another application's certificate ($ExistingHash) is bound to port $Port. It was left alone; choose another secure site port." }
}

# Whether AKML's recorded binding may be removed: only while the port still carries that certificate.
function Test-OwnedBindingRemovable {
    param($Record, [string] $CurrentHash)
    return ($null -ne $Record) -and (-not [string]::IsNullOrEmpty($CurrentHash)) -and (([string]$Record.Thumbprint) -ieq $CurrentHash)
}

function Read-HttpsRecord {
    if (-not (Test-Path $script:HttpsRecordPath)) { return $null }
    try { return Get-Content -Path $script:HttpsRecordPath -Raw | ConvertFrom-Json } catch { return $null }
}

function Write-HttpsRecord([int] $Port, [string] $Thumbprint) {
    @{ Port = $Port; Thumbprint = $Thumbprint } | ConvertTo-Json | Set-Content -Path $script:HttpsRecordPath -Encoding ASCII -Force
}

function Read-BoundHash([int] $Port) {
    $out = & netsh http show sslcert "ipport=0.0.0.0:$Port" 2>$null | Out-String
    return ConvertFrom-NetshSslCert $out
}

# Removes AKML's recorded binding if it is still AKML's, and forgets the record. Returns a log line.
function Remove-OwnedHttpsBinding {
    $record = Read-HttpsRecord
    if ($null -eq $record) { return 'No AKML HTTPS binding is recorded.' }
    $port = [int]$record.Port
    $current = Read-BoundHash $port
    $line = if (Test-OwnedBindingRemovable -Record $record -CurrentHash $current) {
        & netsh http delete sslcert "ipport=0.0.0.0:$port" 2>$null | Out-Null
        "Removed the AKML HTTPS binding on port $port."
    }
    elseif ([string]::IsNullOrEmpty($current)) { "Nothing is bound to port $port any more." }
    else { "Port $port now carries another certificate ($current); it was left alone." }
    Remove-Item -Path $script:HttpsRecordPath -Force -ErrorAction SilentlyContinue
    return $line
}

if ($RemoveOwned) {
    try {
        $line = Remove-OwnedHttpsBinding
        $log = Join-Path (Join-Path $env:ProgramData 'AKML SQL Web') 'install.log'
        Add-Content -Path $log -Value ('[{0}] [https] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $line) -Encoding UTF8 -ErrorAction SilentlyContinue
    } catch { }
    exit 0
}
