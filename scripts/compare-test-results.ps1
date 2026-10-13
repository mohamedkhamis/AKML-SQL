#Requires -Version 5.1
<#
.SYNOPSIS
    Compare two sets of .trx test results check by check (spec 041 SC-008).

.DESCRIPTION
    Reads every *.trx file under -Before (and -After, when given). The suite name is the
    file name before its first '.', so "Web.before.trx" and "Web.after.trx" are both suite
    "Web"; several files of one suite are merged.

    Each UnitTestResult is indexed by its testName. A test name that occurs more than once
    in a suite (a theory with identical display names) gets a " [#n]" suffix so no result
    is lost.

    Writes <OutDir>/<suite>.passed.txt (the sorted names of the passed tests) for the newest
    side: After when it is given, otherwise Before. With -After it also prints, per suite,
    the tests that passed before and are not passed after (failed, skipped or missing), and
    the skipped<->passed transitions, then a summary table.

    No module dependencies; runs under Windows PowerShell 5.1.

.PARAMETER Before
    Folder holding the "before" .trx files (searched recursively).

.PARAMETER After
    Folder holding the "after" .trx files. Omit it to only record the before pass-lists.

.PARAMETER OutDir
    Folder for the <suite>.passed.txt pass-lists. Created when missing.

.PARAMETER AllowList
    Optional text file of test names (wildcards allowed, '#' starts a comment) that may stop
    passing by design. They are reported but do not fail the comparison.

.EXAMPLE
    # Record the baseline pass-lists before the first change.
    powershell -NoProfile -File scripts/compare-test-results.ps1 -Before "$env:LOCALAPPDATA\AKML SQL\spec041-tests\before" -OutDir specs/041-web-ssms-ux-parity/baseline/tests/before
.EXAMPLE
    # Compare after a slice; exit code 1 when a test that passed before no longer passes.
    powershell -NoProfile -File scripts/compare-test-results.ps1 -Before "$env:LOCALAPPDATA\AKML SQL\spec041-tests\before" -After "$env:LOCALAPPDATA\AKML SQL\spec041-tests\after" -OutDir specs/041-web-ssms-ux-parity/baseline/tests/after
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Before,
    [string]$After = '',
    [Parameter(Mandatory = $true)][string]$OutDir,
    [string]$AllowList = ''
)

$ErrorActionPreference = 'Stop'

# --- Read one side: suite -> (test name -> outcome) ---------------------------
function Read-TrxFolder {
    param([string]$Folder)

    if (-not (Test-Path $Folder)) { throw "Folder not found: $Folder" }
    $suites = @{}
    $files = Get-ChildItem -Path $Folder -Filter '*.trx' -Recurse -File
    if ($files.Count -eq 0) { Write-Warning "No .trx files under $Folder" }

    foreach ($file in $files) {
        $suite = $file.Name.Split('.')[0]
        if (-not $suites.ContainsKey($suite)) { $suites[$suite] = @{} }
        $tests = $suites[$suite]

        [xml]$doc = Get-Content -Path $file.FullName -Raw -Encoding UTF8
        # PowerShell's XML adapter ignores the TeamTest default namespace on element access.
        $results = $doc.TestRun.Results.UnitTestResult
        foreach ($r in @($results)) {
            if ($null -eq $r) { continue }
            $name = [string]$r.testName
            $key = $name
            $n = 1
            while ($tests.ContainsKey($key)) {
                $n++
                $key = "$name [#$n]"
            }
            $tests[$key] = [string]$r.outcome
        }
    }
    return $suites
}

function Write-PassLists {
    param([hashtable]$Suites, [string]$Folder)

    if (-not (Test-Path $Folder)) { New-Item -ItemType Directory -Path $Folder -Force | Out-Null }
    foreach ($suite in ($Suites.Keys | Sort-Object)) {
        $passed = $Suites[$suite].GetEnumerator() |
            Where-Object { $_.Value -eq 'Passed' } |
            ForEach-Object { $_.Key } |
            Sort-Object
        $path = Join-Path $Folder "$suite.passed.txt"
        # UTF-8 without BOM, LF endings: the lists are committed text.
        $text = (@($passed) -join "`n") + "`n"
        [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host ("{0,-28} {1,6} passed -> {2}" -f $suite, @($passed).Count, $path)
    }
}

function Read-AllowList {
    param([string]$Path)

    $patterns = @()
    if ([string]::IsNullOrWhiteSpace($Path)) { return $patterns }
    if (-not (Test-Path $Path)) { throw "Allow-list not found: $Path" }
    foreach ($line in Get-Content -Path $Path -Encoding UTF8) {
        $t = $line.Trim()
        if ($t.Length -eq 0 -or $t.StartsWith('#')) { continue }
        $patterns += $t
    }
    return $patterns
}

function Test-Allowed {
    param([string]$Name, [string[]]$Patterns)

    foreach ($p in $Patterns) {
        if ($Name -like $p) { return $true }
    }
    return $false
}

# --- Before only: record the baseline ------------------------------------------
$beforeSuites = Read-TrxFolder -Folder $Before
if ([string]::IsNullOrWhiteSpace($After)) {
    Write-PassLists -Suites $beforeSuites -Folder $OutDir
    exit 0
}

# --- Before and after: compare check by check -----------------------------------
$afterSuites = Read-TrxFolder -Folder $After
Write-PassLists -Suites $afterSuites -Folder $OutDir
$allowed = Read-AllowList -Path $AllowList

$summary = @()
$blocking = 0

$allSuites = @($beforeSuites.Keys) + @($afterSuites.Keys) | Sort-Object -Unique
foreach ($suite in $allSuites) {
    $b = @{}
    if ($beforeSuites.ContainsKey($suite)) { $b = $beforeSuites[$suite] }
    $a = @{}
    if ($afterSuites.ContainsKey($suite)) { $a = $afterSuites[$suite] }

    $regressed = @()   # passed before, failed or skipped after
    $missing = @()     # passed before, absent after
    $nowPassing = @()  # skipped before, passed after
    $nowSkipped = @()  # passed before, skipped after (also counted as regressed)

    foreach ($name in ($b.Keys | Sort-Object)) {
        if ($b[$name] -ne 'Passed') {
            if ($a.ContainsKey($name) -and $a[$name] -eq 'Passed' -and $b[$name] -eq 'NotExecuted') {
                $nowPassing += $name
            }
            continue
        }
        if (-not $a.ContainsKey($name)) { $missing += $name; continue }
        if ($a[$name] -ne 'Passed') {
            $regressed += $name
            if ($a[$name] -eq 'NotExecuted') { $nowSkipped += $name }
        }
    }

    $countPassed = { param($h) @($h.Values | Where-Object { $_ -eq 'Passed' }).Count }
    $unexpected = @($regressed + $missing | Where-Object { -not (Test-Allowed -Name $_ -Patterns $allowed) })
    $blocking += $unexpected.Count

    if ($regressed.Count + $missing.Count + $nowPassing.Count -gt 0) {
        Write-Host ''
        Write-Host "== $suite"
        foreach ($name in $regressed) {
            $mark = ''
            if (Test-Allowed -Name $name -Patterns $allowed) { $mark = '  (allowed)' }
            Write-Host ("  passed -> {0,-11} {1}{2}" -f $a[$name], $name, $mark)
        }
        foreach ($name in $missing) {
            $mark = ''
            if (Test-Allowed -Name $name -Patterns $allowed) { $mark = '  (allowed)' }
            Write-Host ("  passed -> missing     {0}{1}" -f $name, $mark)
        }
        foreach ($name in $nowPassing) {
            Write-Host ("  skipped -> passed     {0}" -f $name)
        }
    }

    $summary += [pscustomobject]@{
        Suite         = $suite
        BeforePassed  = & $countPassed $b
        AfterPassed   = & $countPassed $a
        Regressed     = $regressed.Count
        Missing       = $missing.Count
        NowSkipped    = $nowSkipped.Count
        NowPassing    = $nowPassing.Count
        Unexpected    = $unexpected.Count
    }
}

Write-Host ''
$summary | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

if ($blocking -gt 0) {
    Write-Host "$blocking test(s) passed before and do not pass after (not on the allow-list)."
    exit 1
}
Write-Host 'No test that passed before has stopped passing.'
exit 0
