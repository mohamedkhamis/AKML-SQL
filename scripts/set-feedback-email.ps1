#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Turns feedback email notifications on (or off) for the IIS-hosted AKML SQL site.

.DESCRIPTION
    Each message sent through the site's /feedback form is always stored in the admin inbox
    (/admin/feedback). This script additionally has the site email you a copy of each one, by
    setting the Feedback__* environment variables on the site's app pool -- the same place
    deploy-site-iis.ps1 keeps Admin__PasswordHash, so a redeploy's file mirror never erases them.

    The app pool is recycled at the end so the site picks the settings up. Afterwards, open
    /admin/feedback and press "Send a test email" to confirm the settings actually work.

    The SMTP password is asked for without echo when -Password is not given.

    NOTE: IIS stores app-pool environment variables in plain text in
    %windir%\System32\inetsrv\config\applicationHost.config (readable by administrators only).
    Use a password that is ONLY good for sending mail -- for Gmail an app password
    (https://myaccount.google.com/apppasswords, requires 2-Step Verification), never the account
    password -- so that file never holds a credential that opens your mailbox.

.PARAMETER NotifyEmail
    Where to send notifications.

.PARAMETER SmtpHost
    SMTP server. Default smtp.gmail.com.

.PARAMETER SmtpPort
    SMTP port. Default 587 (STARTTLS).

.PARAMETER UserName
    SMTP account. Default: the -NotifyEmail address.

.PARAMETER Password
    SMTP password as a SecureString. Prompted for when omitted.

.PARAMETER From
    Sender address. Default: the -UserName.

.PARAMETER Disable
    Remove all Feedback__* variables. Messages keep arriving in the inbox; only the email stops.

.EXAMPLE
    .\scripts\set-feedback-email.ps1 -NotifyEmail you@gmail.com

.EXAMPLE
    .\scripts\set-feedback-email.ps1 -NotifyEmail support@example.com -SmtpHost smtp.office365.com -UserName noreply@example.com

.EXAMPLE
    .\scripts\set-feedback-email.ps1 -Disable
#>
[CmdletBinding(DefaultParameterSetName = 'Enable')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Enable')]
    [string] $NotifyEmail,

    [Parameter(ParameterSetName = 'Enable')]
    [string] $SmtpHost = 'smtp.gmail.com',

    [Parameter(ParameterSetName = 'Enable')]
    [ValidateRange(1, 65535)]
    [int] $SmtpPort = 587,

    [Parameter(ParameterSetName = 'Enable')]
    [string] $UserName = '',

    [Parameter(ParameterSetName = 'Enable')]
    [securestring] $Password,

    [Parameter(ParameterSetName = 'Enable')]
    [string] $From = '',

    [Parameter(Mandatory, ParameterSetName = 'Disable')]
    [switch] $Disable,

    [string] $AppPoolName = 'AkmlSqlSite'
)

$ErrorActionPreference = 'Stop'
Import-Module WebAdministration

if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
    throw "App pool '$AppPoolName' does not exist. Deploy the site first (scripts\deploy-site-iis.ps1)."
}

$apphost = 'MACHINE/WEBROOT/APPHOST'
$envFilter = "system.applicationHost/applicationPools/add[@name='$AppPoolName']/environmentVariables"
$names = @(
    'Feedback__NotifyEmail',
    'Feedback__Smtp__Host',
    'Feedback__Smtp__Port',
    'Feedback__Smtp__EnableSsl',
    'Feedback__Smtp__UserName',
    'Feedback__Smtp__Password',
    'Feedback__Smtp__From'
)

function Remove-PoolEnv([string] $Name) {
    if (Get-WebConfiguration -pspath $apphost -filter "$envFilter/add[@name='$Name']") {
        Remove-WebConfigurationProperty -pspath $apphost -filter $envFilter -name '.' -AtElement @{ name = $Name }
    }
}

function Set-PoolEnv([string] $Name, [string] $Value) {
    # Remove + add rather than update in place: one code path, and no stale attribute survives.
    Remove-PoolEnv $Name
    Add-WebConfigurationProperty -pspath $apphost -filter $envFilter -name '.' -value @{ name = $Name; value = $Value }
}

if ($Disable) {
    foreach ($name in $names) { Remove-PoolEnv $name }
    Write-Host 'Feedback email notifications removed. Messages still arrive in /admin/feedback.'
}
else {
    # The same checks the site applies, so a typo fails here rather than as a silent SMTP error later.
    function Assert-Address([string] $Value, [string] $What) {
        try { $null = [System.Net.Mail.MailAddress]::new($Value) }
        catch { throw "$What '$Value' is not a valid email address." }
    }
    Assert-Address $NotifyEmail 'NotifyEmail'
    if (-not $UserName) { $UserName = $NotifyEmail }
    if ($From) { Assert-Address $From 'From' }

    if (-not $Password) {
        $Password = Read-Host "SMTP password for $UserName (for Gmail: an app password)" -AsSecureString
    }
    $plain = [System.Net.NetworkCredential]::new('', $Password).Password
    if (-not $plain) { throw 'An SMTP password is required.' }
    # Gmail shows app passwords in groups of four ("abcd efgh ijkl mnop"); the spaces are not part of it.
    if ($SmtpHost -eq 'smtp.gmail.com') { $plain = $plain -replace '\s', '' }

    Set-PoolEnv 'Feedback__NotifyEmail' $NotifyEmail
    Set-PoolEnv 'Feedback__Smtp__Host' $SmtpHost
    Set-PoolEnv 'Feedback__Smtp__Port' ([string] $SmtpPort)
    Set-PoolEnv 'Feedback__Smtp__EnableSsl' 'true'
    Set-PoolEnv 'Feedback__Smtp__UserName' $UserName
    Set-PoolEnv 'Feedback__Smtp__Password' $plain
    if ($From) { Set-PoolEnv 'Feedback__Smtp__From' $From } else { Remove-PoolEnv 'Feedback__Smtp__From' }
    $plain = $null

    Write-Host "Feedback notifications will go to $NotifyEmail via ${SmtpHost}:$SmtpPort as $UserName."
}

Restart-WebAppPool -Name $AppPoolName
Write-Host "App pool '$AppPoolName' recycled."
if (-not $Disable) {
    Write-Host 'Now open /admin/feedback and press "Send a test email" to confirm it works.'
}
