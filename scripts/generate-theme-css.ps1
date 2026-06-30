#Requires -Version 5.1
<#
.SYNOPSIS
    Generate AKML SQL web-edition theme CSS from docs/theme-tokens.json.

.DESCRIPTION
    Reads docs/theme-tokens.json (the single source of truth shared with the WPF
    ThemeRegistry per spec 016) and emits three CSS files under
    src/AkmlSql.Web/wwwroot/css/themes/:
        light.css, dark.css, high-contrast.css

    Each file declares CSS custom properties named identically to the WPF brush tokens
    (translated by the rule: drop `Akml.Brush.` prefix, replace `.` with `-`, lowercase,
    prepend `--akml-`). Surfaces consume tokens via `var(--akml-surface-canvas)` etc.

    Spec 021 task T004.

.PARAMETER CheckOnly
    Do not write any file. Re-generate in memory and compare against the on-disk files.
    Exit code 0 if identical, 1 if different. Used by CI to detect theme drift.

.PARAMETER RepoRoot
    Override repo root. Defaults to the script's grandparent ($PSScriptRoot/..).

.EXAMPLE
    pwsh scripts/generate-theme-css.ps1
    pwsh scripts/generate-theme-css.ps1 -CheckOnly
#>
[CmdletBinding()]
param(
    [switch]$CheckOnly,
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$tokensPath = Join-Path $RepoRoot 'docs/theme-tokens.json'
$outDir     = Join-Path $RepoRoot 'src/AkmlSql.Web/wwwroot/css/themes'

if (-not (Test-Path $tokensPath)) {
    throw "theme-tokens.json not found at $tokensPath"
}

$raw = Get-Content $tokensPath -Raw
$json = $raw | ConvertFrom-Json

# --- Token-key → CSS variable name -----------------------------------------
function ConvertTo-CssVarName {
    param([string]$Key)
    # "Akml.Brush.Surface.Canvas" -> "--akml-surface-canvas"
    # "Akml.Scalar.Spacing.XS"    -> "--akml-scalar-spacing-xs"
    # "Akml.Type.Chrome"          -> "--akml-type-chrome"
    $stripped = $Key -replace '^Akml\.Brush\.', '' `
                     -replace '^Akml\.',        ''
    $kebab = ($stripped.ToLowerInvariant() -replace '\.', '-')
    return "--akml-$kebab"
}

# --- WPF ARGB hex (#AARRGGBB) → CSS rgba() ---------------------------------
# CSS doesn't accept #AARRGGBB; convert to rgba(r, g, b, a-fraction).
function ConvertTo-CssColor {
    param([string]$Hex)
    if ($Hex -match '^#([0-9A-Fa-f]{8})$') {
        $a = [Convert]::ToInt32($Matches[1].Substring(0, 2), 16)
        $r = [Convert]::ToInt32($Matches[1].Substring(2, 2), 16)
        $g = [Convert]::ToInt32($Matches[1].Substring(4, 2), 16)
        $b = [Convert]::ToInt32($Matches[1].Substring(6, 2), 16)
        $af = [Math]::Round($a / 255.0, 3)
        return "rgba($r, $g, $b, $af)"
    }
    elseif ($Hex -match '^#([0-9A-Fa-f]{6})$') {
        return $Hex.ToLowerInvariant()
    }
    else {
        # CSS system color keyword (Canvas, CanvasText, Highlight, etc.) -- pass through
        return $Hex
    }
}

# --- Emit one CSS file for a variant ---------------------------------------
function Format-VariantCss {
    param(
        [string]$VariantName,   # "Light" | "Dark" | "HighContrast"
        [string]$VariantKey     # "light" | "dark" | "highContrast"
    )

    $sb = [System.Text.StringBuilder]::new()
    $null = $sb.AppendLine("/*")
    $null = $sb.AppendLine("  AKML SQL -- web edition theme variant: $VariantName")
    $null = $sb.AppendLine("  AUTO-GENERATED from docs/theme-tokens.json by scripts/generate-theme-css.ps1.")
    $null = $sb.AppendLine("  Do not edit by hand. Run the script to regenerate after editing the JSON.")
    $null = $sb.AppendLine("*/")
    $null = $sb.AppendLine(":root {")

    foreach ($groupName in $json.groups.PSObject.Properties.Name) {
        $null = $sb.AppendLine("    /* $groupName */")
        $group = $json.groups.$groupName
        foreach ($tokenKey in $group.PSObject.Properties.Name) {
            $value   = $group.$tokenKey.$VariantKey
            $cssName = ConvertTo-CssVarName $tokenKey
            $cssVal  = ConvertTo-CssColor $value
            $null = $sb.AppendLine("    ${cssName}: ${cssVal};")
        }
    }

    # Spacing (theme-invariant, but emitted in every variant for one-stop consumption)
    $null = $sb.AppendLine("    /* Spacing (theme-invariant scalars in DIU) */")
    foreach ($spaceKey in $json.spacing.PSObject.Properties.Name) {
        $cssName = ConvertTo-CssVarName $spaceKey
        $px = $json.spacing.$spaceKey
        $null = $sb.AppendLine("    ${cssName}: ${px}px;")
    }

    # Radius (theme-invariant scalars; pills use a large px value, not a percentage)
    if ($json.PSObject.Properties.Name -contains 'radius') {
        $null = $sb.AppendLine("    /* Radius (theme-invariant scalars in DIU) */")
        foreach ($radiusKey in $json.radius.PSObject.Properties.Name) {
            $cssName = ConvertTo-CssVarName $radiusKey
            $px = $json.radius.$radiusKey
            $null = $sb.AppendLine("    ${cssName}: ${px}px;")
        }
    }

    # Typography (composite; split into family/size/weight sub-variables)
    $null = $sb.AppendLine("    /* Typography (theme-invariant; split into family/size/weight) */")
    foreach ($typeKey in $json.typography.PSObject.Properties.Name) {
        $spec   = $json.typography.$typeKey
        $base   = ConvertTo-CssVarName $typeKey
        $family = $spec.family
        # Quote multi-word font families
        if ($family -match '\s') { $family = "`"$family`"" }
        $null = $sb.AppendLine("    ${base}-family: ${family};")
        $size = $spec.size
        $null = $sb.AppendLine("    ${base}-size: ${size}px;")
        $weightCss = switch ($spec.weight) {
            'Normal'   { '400' }
            'SemiBold' { '600' }
            'Bold'     { '700' }
            default    { '400' }
        }
        $null = $sb.AppendLine("    ${base}-weight: ${weightCss};")
    }

    $null = $sb.AppendLine("}")
    return $sb.ToString()
}

# --- File map --------------------------------------------------------------
$variants = @(
    @{ Name = 'Light';        Key = 'light';        File = 'light.css' },
    @{ Name = 'Dark';         Key = 'dark';         File = 'dark.css' },
    @{ Name = 'HighContrast'; Key = 'highContrast'; File = 'high-contrast.css' }
)

# --- Generate ---------------------------------------------------------------
$failures = 0
foreach ($v in $variants) {
    $css = Format-VariantCss -VariantName $v.Name -VariantKey $v.Key
    $target = Join-Path $outDir $v.File

    if ($CheckOnly) {
        if (-not (Test-Path $target)) {
            Write-Host "MISMATCH: $($v.File) does not exist on disk." -ForegroundColor Red
            $failures++
            continue
        }
        $existing = Get-Content $target -Raw
        # Normalise line endings before comparison
        $existingN = $existing -replace "`r`n", "`n"
        $newN      = $css      -replace "`r`n", "`n"
        if ($existingN -ne $newN) {
            Write-Host "MISMATCH: $($v.File) is out of date -- re-run scripts/generate-theme-css.ps1." -ForegroundColor Red
            $failures++
        }
        else {
            Write-Host "OK: $($v.File)" -ForegroundColor Green
        }
    }
    else {
        $dir = Split-Path -Parent $target
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        Set-Content -Path $target -Value $css -NoNewline -Encoding UTF8
        Write-Host "Wrote: $target"
    }
}

if ($CheckOnly -and $failures -gt 0) {
    $msg = 'Theme CSS drift detected (' + $failures + ' files).'
    Write-Host $msg -ForegroundColor Red
    exit 1
}
exit 0
