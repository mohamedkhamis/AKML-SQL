<# Removes only AKML's IIS site, unused app pool and recorded HTTPS binding. Dot-source for pure plans. #>
param([string] $AppDir = '')

function Get-IisRemovalPlan {
    param([bool] $WebAdministrationAvailable = $false, [bool] $AppCmdAvailable = $false,
        [string[]] $OtherPoolUsers = @(), [bool] $SiteListedAfter = $true, [bool] $PoolUsageKnown = $true)
    $method = if ($WebAdministrationAvailable) { 'WebAdministration' } elseif ($AppCmdAvailable) { 'AppCmd' } else { 'None' }
    [pscustomobject]@{
        SiteMethod = $method
        PoolMethod = if ($PoolUsageKnown -and $OtherPoolUsers.Count -eq 0) { $method } else { 'None' }
        RemoveOwnedBinding = $true
        DeleteWebConfig = -not $SiteListedAfter
        WriteFailure = $SiteListedAfter
        FailureReason = 'AkmlSqlWeb could not be confirmed removed. Run: %windir%\System32\inetsrv\appcmd.exe delete site "AkmlSqlWeb"'
        Markers = @('iis-site.ok', 'https-site.ok', 'https-site.failed')
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    $state = Join-Path $env:ProgramData 'AKML SQL Web'
    function Write-RemovalLog([string] $text) {
        try { Add-Content -LiteralPath (Join-Path $state 'install.log') -Value "[$(Get-Date -Format s)] [iis-remove] $text" -Encoding UTF8 } catch { }
    }
    function Read-IisState {
        $config = Join-Path $env:windir 'System32\inetsrv\config\applicationHost.config'
        try {
            if (-not (Test-Path -LiteralPath $config)) { return @{ Known = $true; Listed = $false; Users = @(); UsageKnown = $false } }
            [xml] $xml = Get-Content -LiteralPath $config -Raw -ErrorAction Stop
            $sites = @($xml.configuration.'system.applicationHost'.sites.site)
            $listed = @($sites | Where-Object { $_.name -eq 'AkmlSqlWeb' }).Count -gt 0
            $users = @()
            $usageKnown = $true
            foreach ($site in @($sites | Where-Object { $_.name -ne 'AkmlSqlWeb' })) {
                foreach ($app in @($site.application)) {
                    $pool = [string]$app.applicationPool
                    if (-not $pool) { $pool = [string]$site.applicationDefaults.applicationPool }
                    if (-not $pool) { $pool = [string]$xml.configuration.'system.applicationHost'.sites.siteDefaults.applicationDefaults.applicationPool }
                    if (-not $pool) { $usageKnown = $false }
                    if ($pool -eq 'AkmlSqlWeb') { $users += [string]$site.name + [string]$app.path }
                }
            }
            return @{ Known = $true; Listed = $listed; Users = $users; UsageKnown = $usageKnown }
        } catch { return @{ Known = $false; Listed = $true; Users = @(); UsageKnown = $false } }
    }
    try {
        $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'IIS removal requires administrator rights.' }
        New-Item -ItemType Directory -Path $state -Force | Out-Null
        $appcmd = Join-Path $env:windir 'System32\inetsrv\appcmd.exe'
        $module = $false
        try { Import-Module WebAdministration -ErrorAction Stop; $module = $true } catch { }
        $before = Read-IisState
        $plan = Get-IisRemovalPlan -WebAdministrationAvailable $module -AppCmdAvailable (Test-Path -LiteralPath $appcmd)
        try {
            if ($plan.SiteMethod -eq 'WebAdministration') { Remove-WebSite -Name 'AkmlSqlWeb' -ErrorAction SilentlyContinue }
            elseif ($plan.SiteMethod -eq 'AppCmd') { & $appcmd delete site 'AkmlSqlWeb' 2>&1 | Out-Null }
        } catch { Write-RemovalLog $_.Exception.Message }
        $after = Read-IisState
        $plan = Get-IisRemovalPlan -WebAdministrationAvailable $module -AppCmdAvailable (Test-Path -LiteralPath $appcmd) -OtherPoolUsers $after.Users -PoolUsageKnown ($after.Known -and $after.UsageKnown -and -not $after.Listed) -SiteListedAfter $after.Listed
        try {
            if ($plan.PoolMethod -eq 'WebAdministration') { Remove-WebAppPool -Name 'AkmlSqlWeb' -ErrorAction SilentlyContinue }
            elseif ($plan.PoolMethod -eq 'AppCmd') { & $appcmd delete apppool 'AkmlSqlWeb' 2>&1 | Out-Null }
        } catch { Write-RemovalLog $_.Exception.Message }
        try {
            . (Join-Path $PSScriptRoot 'web-https-binding.ps1')
            Write-RemovalLog (Remove-OwnedHttpsBinding)
        } catch { Write-RemovalLog $_.Exception.Message }
        if ($plan.DeleteWebConfig -and $after.Known -and $AppDir) {
            $root = [IO.Path]::GetFullPath($AppDir)
            $web = Join-Path $root 'Web'
            if ((Test-Path -LiteralPath $web) -and -not ((Get-Item -LiteralPath $web -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                Remove-Item -LiteralPath (Join-Path $web 'web.config') -Force -ErrorAction SilentlyContinue
            }
        }
        foreach ($marker in $plan.Markers) { Remove-Item -LiteralPath (Join-Path $state $marker) -Force -ErrorAction SilentlyContinue }
        $failure = Join-Path $state 'iis-remove.failed'
        if ($after.Listed) {
            Set-Content -LiteralPath $failure -Encoding UTF8 -Value $plan.FailureReason
        } else { Remove-Item -LiteralPath $failure -Force -ErrorAction SilentlyContinue }
        Write-RemovalLog "Site still listed: $($after.Listed); configuration readable: $($after.Known)"
    } catch {
        Write-RemovalLog $_.Exception.Message
        try { Set-Content -LiteralPath (Join-Path $state 'iis-remove.failed') -Encoding UTF8 -Value ('AkmlSqlWeb removal was not completed: ' + $_.Exception.Message + '. Run: appcmd.exe delete site "AkmlSqlWeb"') } catch { }
    }
    exit 0
}
