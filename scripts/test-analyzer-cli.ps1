<#
.SYNOPSIS
    Release gate for the analyzer CLI (spec 047, CLI-001).

.DESCRIPTION
    The shipped AkmlSql.Analyzer.exe is a trimmed single-file publish. Trimming once removed every analysis rule
    (they are found by reflection) and the exe answered "No issues found" for any SQL. This script runs a published
    exe on the committed fixtures and fails unless it reports what it must. build.ps1 and Deploy-Build-Release.ps1 call
    it right after publishing the CLI; nothing ships from a build that fails it.

.PARAMETER Exe
    The analyzer to check (normally the publish output).

.PARAMETER Fixtures
    The fixture folder. Defaults to tests\fixtures\cli under the repository root.

.EXAMPLE
    powershell -NoProfile -File scripts/test-analyzer-cli.ps1 -Exe src\AkmlSql.Analyzer\bin\Release\net10.0\win-x64\publish\AkmlSql.Analyzer.exe

.OUTPUTS
    Exit code 0 when every check passes, 1 otherwise (each failed check is printed with the command and its output).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Exe,
    [string] $Fixtures = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Fixtures)) {
    # Windows PowerShell 5.1 can leave $PSScriptRoot empty under -File; fall back to the invocation path.
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $Fixtures = Join-Path (Split-Path -Parent $scriptDir) 'tests\fixtures\cli'
}
if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) { Write-Host "Analyzer CLI smoke: no exe at $Exe" -ForegroundColor Red; exit 1 }
if (-not (Test-Path -LiteralPath $Fixtures -PathType Container)) { Write-Host "Analyzer CLI smoke: no fixtures at $Fixtures" -ForegroundColor Red; exit 1 }

$multi = Join-Path $Fixtures 'multi.sql'
$sem1 = Join-Path $Fixtures 'sem1.sql'
$pe004Off = Join-Path $Fixtures 'pe004-off.casettings'

function Invoke-Analyzer([string[]] $Arguments) {
    $output = & $Exe @Arguments 2>&1 | Out-String
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

$failures = New-Object System.Collections.Generic.List[string]
function Assert-Check([string] $Name, [string[]] $Arguments, [scriptblock] $Condition) {
    $run = Invoke-Analyzer $Arguments
    $ok = & $Condition $run
    if ($ok) { Write-Host "  ok  $Name" -ForegroundColor Green }
    else {
        $failures.Add("$Name`n    command: $Exe $($Arguments -join ' ')`n    exit: $($run.ExitCode)`n$($run.Output)")
        Write-Host "  FAIL $Name" -ForegroundColor Red
    }
}

Assert-Check 'multi.sql reports PE002, PE003 and PE004' @('--file', $multi) {
    param($r) $r.Output -match 'PE002' -and $r.Output -match 'PE003' -and $r.Output -match 'PE004'
}
Assert-Check '--check exits 1 on multi.sql' @('--file', $multi, '--check') {
    param($r) $r.ExitCode -eq 1
}
Assert-Check 'sem1.sql --rules ST004 reports one information ST004' @('--file', $sem1, '--rules', 'ST004') {
    param($r) @($r.Output -split "`n" | Where-Object { $_ -match 'ST004' -and $_ -match 'information' }).Count -eq 1
}
Assert-Check '--settings turns PE004 off' @('--file', $multi, '--settings', $pe004Off) {
    param($r) $r.Output -match 'PE003' -and $r.Output -notmatch 'PE004'
}

if ($failures.Count -gt 0) {
    Write-Host "Analyzer CLI smoke failed:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host $_ }
    exit 1
}
exit 0
