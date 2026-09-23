#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Adds or removes the "AKML SQL\Update Check" scheduled task.

.DESCRIPTION
    The task runs "AkmlSql.Updater.exe --scheduled" for whoever is signed in:
      - daily (with a random delay of up to 4 hours, so machines do not all ask at once), and
      - 10 minutes after a user signs in.
    It runs as the signed-in user (the BUILTIN\Users group, least privilege), so it reads that
    user's own settings and update cache and can show them a Windows notification. It never
    installs anything; installing needs the user's click and Windows' admin prompt.

    The updater itself decides whether there is anything to do: it honours each user's
    "Check for updates automatically" setting and skips a check made within the last 12 hours.

.PARAMETER Action
    Add (install) or Remove (uninstall).

.PARAMETER UpdaterPath
    Full path to AkmlSql.Updater.exe. Required for Add.

.PARAMETER TaskPath
    Task Scheduler folder. Default \AKML SQL\ ; tests pass their own.

.PARAMETER TaskName
    Task name. Default "Update Check".

.NOTES
    Idempotent: Add replaces an existing task of the same name. ASCII-only on purpose: Windows
    PowerShell 5.1 reads a no-BOM script as ANSI.
#>
param(
    [Parameter(Mandatory = $true)] [ValidateSet('Add', 'Remove')] [string] $Action,
    [string] $UpdaterPath = '',
    [string] $TaskPath = '\AKML SQL\',
    [string] $TaskName = 'Update Check'
)

$ErrorActionPreference = 'Stop'
$logRoot = Join-Path $env:ProgramData 'AKML SQL'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$logFile = Join-Path $logRoot 'install.log'

function Log([string] $msg) {
    $line = '[{0}] [update-task] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $msg
    Add-Content -Path $logFile -Value $line -Encoding UTF8
}

try {
    $existing = Get-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -ErrorAction SilentlyContinue

    if ($Action -eq 'Remove') {
        if ($existing) {
            Unregister-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -Confirm:$false
            Log "Removed task $TaskPath$TaskName"
        }
        # Drop the folder too when nothing else lives in it.
        try {
            $service = New-Object -ComObject 'Schedule.Service'
            $service.Connect()
            $folder = $service.GetFolder($TaskPath.TrimEnd('\'))
            if ($folder.GetTasks(1).Count -eq 0 -and $folder.GetFolders(0).Count -eq 0) {
                $service.GetFolder('\').DeleteFolder($TaskPath.Trim('\'), 0)
            }
        } catch { }
        exit 0
    }

    if (-not $UpdaterPath -or -not (Test-Path -LiteralPath $UpdaterPath)) {
        throw "Updater not found at '$UpdaterPath'."
    }

    $taskAction = New-ScheduledTaskAction -Execute $UpdaterPath -Argument '--scheduled'

    $daily = New-ScheduledTaskTrigger -Daily -At '10:00'
    $daily.RandomDelay = 'PT4H'
    $atLogon = New-ScheduledTaskTrigger -AtLogOn
    $atLogon.Delay = 'PT10M'

    # BUILTIN\Users by SID (locale-independent): runs in each signed-in user's own session, without
    # elevation, and only while they are signed in.
    $principal = New-ScheduledTaskPrincipal -GroupId 'S-1-5-32-545' -RunLevel Limited

    $settings = New-ScheduledTaskSettingsSet `
        -StartWhenAvailable `
        -RunOnlyIfNetworkAvailable `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit (New-TimeSpan -Minutes 30) `
        -MultipleInstances IgnoreNew `
        -Hidden

    $task = New-ScheduledTask -Action $taskAction -Trigger @($daily, $atLogon) -Principal $principal `
        -Settings $settings -Description 'Checks for AKML SQL updates, downloads and verifies them, and tells you when one is ready to install. It never installs anything by itself.'

    if ($existing) {
        Unregister-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -Confirm:$false
    }
    Register-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -InputObject $task | Out-Null
    Log "Registered task $TaskPath$TaskName -> $UpdaterPath --scheduled"
    exit 0
}
catch {
    Log "ERROR: $_"
    Write-Host "Update task setup failed: $_"
    exit 1
}
