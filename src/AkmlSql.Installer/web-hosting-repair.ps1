<# Repairs the persisted host. The guard is checked before elevation and again in the elevated process. #>
param()

function Test-UnsafeHostingWriteAce {
    param([string] $Sid, [Security.AccessControl.FileSystemRights] $Rights,
        [Security.AccessControl.AccessControlType] $AccessType = 'Allow',
        [Security.AccessControl.PropagationFlags] $Propagation = 'None')
    if ($AccessType -ne 'Allow' -or ($Propagation -band [Security.AccessControl.PropagationFlags]::InheritOnly)) { return $false }
    if ($Sid -in @('S-1-5-32-544', 'S-1-5-18', 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')) { return $false }
    $writes = [Security.AccessControl.FileSystemRights]'WriteData,AppendData,WriteAttributes,WriteExtendedAttributes,Delete,DeleteSubdirectoriesAndFiles,ChangePermissions,TakeOwnership'
    return ($Rights -band $writes) -ne 0
}

function Set-PrivateHostingState([string] $Folder) {
    New-Item -ItemType Directory -Path $Folder -Force | Out-Null
    $paths = New-Object 'Collections.Generic.List[string]'
    $pending = New-Object 'Collections.Generic.Stack[string]'
    $pending.Push([IO.Path]::GetFullPath($Folder))
    while ($pending.Count) {
        $path = $pending.Pop()
        $item = Get-Item -LiteralPath $path -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'State-folder reparse points are refused.' }
        $paths.Add($path)
        if ($item.PSIsContainer) { Get-ChildItem -LiteralPath $path -Force | ForEach-Object { $pending.Push($_.FullName) } }
    }
    & icacls.exe $Folder /setowner '*S-1-5-32-544' /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not set the state-folder owner.' }
    foreach ($path in $paths) {
        $acl = (Get-Item -LiteralPath $path -Force).GetAccessControl()
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($rule in @($acl.GetAccessRules($true, $false, [Security.Principal.SecurityIdentifier]))) { $acl.RemoveAccessRuleSpecific($rule) }
        $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
        $flags = [Security.AccessControl.InheritanceFlags]::None
        if ((Get-Item -LiteralPath $path -Force).PSIsContainer) { $flags = [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit' }
        foreach ($sid in @('S-1-5-32-544', 'S-1-5-18')) {
            $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($sid)), 'FullControl', $flags, 'None', 'Allow')))
        }
        (Get-Item -LiteralPath $path -Force).SetAccessControl($acl)
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    $pause = $true
    function Read-InstallValue([string] $key, [string] $name, [string] $fallback = '') {
        foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32)) {
            $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
            try { $item = $base.OpenSubKey($key); if ($item) { try { $value = $item.GetValue($name); if ($null -ne $value -and "$value" -ne '') { return "$value" } } finally { $item.Dispose() } } } finally { $base.Dispose() }
        }
        return $fallback
    }
    function Invoke-Support([string] $name, [string[]] $parameters) {
        & (Join-Path $env:windir 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot $name) @parameters
        if ($LASTEXITCODE -ne 0) { throw "$name failed with exit $LASTEXITCODE. See install.log." }
    }
    function Invoke-Sc([string[]] $parameters) {
        & sc.exe @parameters | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "sc.exe $($parameters -join ' ') failed ($LASTEXITCODE)." }
    }
    try {
        $uninstall = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{F7E8A9B0-C1D2-E3F4-A5B6-C7D8E9F0A1B2}_is1'
        $app = Read-InstallValue $uninstall 'InstallLocation'
        if (-not $app) { Write-Host 'Nothing to repair: the web edition is not hosted on this computer.'; return }
        $app = [IO.Path]::GetFullPath($app).TrimEnd('\')
        if ([IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') -ine (Join-Path $app 'Support')) { throw 'Repair must run from the installed Support folder.' }
        foreach ($path in @($app, $PSScriptRoot)) {
            if ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Repair refuses reparse-point installation folders.' }
        }
        foreach ($ace in ([IO.Directory]::GetAccessControl($app)).GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
            if (Test-UnsafeHostingWriteAce $ace.IdentityReference.Value $ace.FileSystemRights $ace.AccessControlType $ace.PropagationFlags) { throw 'Repair refuses an installation folder writable by a non-administrator.' }
        }
        $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'))
            $pause = $false
            return
        }
        $key = 'Software\AKML SQL\Web'
        $hostChoice = Read-InstallValue $key 'Host'
        if ($hostChoice -notin @('Service', 'IIS')) { Write-Host 'Nothing to repair: the web edition is not hosted on this computer.'; return }
        $exposure = Read-InstallValue $key 'Exposure' 'Localhost'
        $enginePort = [int](Read-InstallValue $key 'BridgePort' '47291')
        $iisPort = [int](Read-InstallValue $key 'IisPort' '80')
        $httpsPort = [int](Read-InstallValue $key 'HttpsPort' '443')
        if ($exposure -notin @('Localhost', 'Lan') -or $enginePort -lt 1024 -or $enginePort -gt 65535) { throw 'Invalid persisted hosting settings. Re-run setup.' }
        $state = Join-Path $env:ProgramData 'AKML SQL Web'
        $config = Join-Path $state 'config.json'
        if ($hostChoice -eq 'IIS') {
            if ($exposure -eq 'Lan') { Invoke-Support 'web-tls-setup.ps1' @('-Port', "$enginePort", '-PfxPath', (Join-Path $state 'certs\bridge.pfx')) }
            Invoke-Support 'web-iis-repair.ps1' @('-NoPrompt')
            Invoke-Support 'web-firewall.ps1' @('-Apply', '-WebHost', $hostChoice, '-Exposure', $exposure, '-EnginePort', "$enginePort", '-HttpsPort', "$httpsPort")
            Invoke-Sc @('config', 'AkmlSqlWebEngine', 'start=', 'delayed-auto')
        } else {
            if (-not (Get-Service 'AkmlSqlWebEngine' -ErrorAction SilentlyContinue)) {
                New-Service -Name 'AkmlSqlWebEngine' -BinaryPathName ('"' + (Join-Path $app 'Engine\AkmlSql.Engine.exe') + '" --web --config "' + $config + '"') -DisplayName 'AKML SQL Web Engine' -StartupType Automatic | Out-Null
            }
            Invoke-Sc @('config', 'AkmlSqlWebEngine', 'start=', 'auto')
            Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\AkmlSqlWebEngine' -Name DelayedAutostart -Value 0 -Type DWord
            Set-PrivateHostingState $state
            Invoke-Support 'web-config-bridge.ps1' @('-Port', "$enginePort", '-Mode', $exposure, '-WebRoot', (Join-Path $app 'Web'), '-ConfigPath', $config)
            if ($exposure -eq 'Lan') { Invoke-Support 'web-tls-setup.ps1' @('-Port', "$enginePort", '-PfxPath', (Join-Path $state 'certs\bridge.pfx')) }
            Invoke-Support 'web-firewall.ps1' @('-Apply', '-WebHost', $hostChoice, '-Exposure', $exposure, '-EnginePort', "$enginePort", '-HttpsPort', "$httpsPort")
        }
        Invoke-Sc @('failure', 'AkmlSqlWebEngine', 'reset=', '86400', 'actions=', 'restart/5000/restart/10000/restart/60000')
        Invoke-Sc @('failureflag', 'AkmlSqlWebEngine', '1')
        if ($hostChoice -eq 'Service') { Set-PrivateHostingState $state }
        $service = Get-Service 'AkmlSqlWebEngine'
        if ($service.Status -eq 'Running') { Restart-Service 'AkmlSqlWebEngine' -Force } else { Start-Service 'AkmlSqlWebEngine' }
        (Get-Service 'AkmlSqlWebEngine').WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
        if ($hostChoice -eq 'IIS') { $url = "http://localhost:$iisPort/"; $expectation = '-ExpectBundle' }
        elseif ($exposure -eq 'Lan') { $url = "https://localhost:$enginePort/"; $expectation = '-ExpectEngine' }
        else { $url = "http://127.0.0.1:$enginePort/"; $expectation = '-ExpectEngine' }
        Invoke-Support 'web-hosting-check.ps1' @('-Url', $url, $expectation, '-ReasonPath', (Join-Path $state 'repair-reason.txt'))
        Write-Host "Hosting answers at $url" -ForegroundColor Green
        if ($exposure -eq 'Lan') { Write-Host ('Pairing PIN: ' + (Get-Content -LiteralPath (Join-Path $state 'pairing-pin.txt') -Raw).Trim()) }
    } catch { Write-Host $_.Exception.Message -ForegroundColor Red }
    finally { if ($pause) { Read-Host 'Press Enter to close' | Out-Null } }
}
