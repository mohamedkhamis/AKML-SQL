# AKML SQL — Development Progress Log

## Overview

This document tracks the development progress, issues encountered, root causes identified, and solutions applied during the AKML SQL extension development. It serves as institutional knowledge for future debugging sessions.

---

## Phase 1: Foundation and Installer

### Milestone 1: Project Scaffolding

**Status**: Complete

- Created solution with 6 shell extension projects (SSMS 20/21/22, VS 2019/2022/2026)
- Created shared project (`AkmlSql.Shell.Shared`) with menu commands, dialogs, status bar, update launcher, and load validator
- Created core library (`AkmlSql.Core`) with config manager, logging, update models
- Created self-contained updater (`AkmlSql.Updater`)
- Created Inno Setup 7 installer with environment scanner (registry + vswhere + filesystem fallback)
- Created Specify framework specs under `specs/001-phase1-foundation-installer/`

### Milestone 2: SSMS 20 Extension Loading

**Status**: Complete — verified working

#### Issue 1: Wrong Shell Assembly Version

- **Symptom**: Extension fails to load; activity log shows assembly binding failure
- **Root Cause**: VS SDK 16.0.208 references `Shell.15.0` version `16.0.0.0`, but SSMS 20 (VS 2017 IsolatedShell) ships `15.0.0.0`
- **Fix**: Downgraded VS SDK from `16.0.208` to `15.9.3` and VSSDK.BuildTools from `16.*` to `15.*`
- **Lesson**: Always clean `bin/obj` folders after SDK version changes — stale NuGet cache causes wrong assembly references

#### Issue 2: Menu Not Appearing — Missing CTO Resource

- **Symptom**: `HrLoadNativeUILibrary failed with 0x800a006f` in activity log
- **Root Cause**: SDK-style projects do not have a `.resx` file by default. Without `VSPackage.resx` with `<MergeWithCTO>true</MergeWithCTO>`, the VSCT-compiled CTO (`Menus.ctmenu`) is never embedded as a managed resource in the output DLL
- **Build Warning**: `VSSDK1205: There are no resources to merge the cto files into`
- **Fix**: Created `VSPackage.resx` in all 6 shell projects with `<EmbeddedResource Update="VSPackage.resx"><MergeWithCTO>true</MergeWithCTO><ManifestResourceName>VSPackage</ManifestResourceName></EmbeddedResource>` in each `.csproj`
- **Gotcha**: Must use `Update=` not `Include=` — SDK-style projects auto-include `.resx` files, causing `NETSDK1022: Duplicate EmbeddedResource`

#### Issue 3: Menu Appears But Clicks Do Nothing

- **Symptom**: AKML SQL menu visible, but clicking any item produces no response
- **Root Cause 1 — AsyncPackage**: SSMS 20 (VS 2017 shell) does not properly wire up command handlers when using `AsyncPackage` with background loading before menu clicks arrive
- **Fix**: Changed `AkmlSqlPackage` from `AsyncPackage` to synchronous `Package` for SSMS 20
- **Root Cause 2 — Initialization Order**: `LoggerFactory.Initialize()` or `LoadValidator.Validate()` threw exceptions that were silently caught, but this happened BEFORE command registration, so no commands were ever registered
- **Fix**: Reordered `Initialize()` to register all menu commands FIRST, then perform non-critical initialization (logging, validation, update check) in a separate try-catch
- **Root Cause 3 — Missing Dependency DLLs**: `System.Text.Json`, `System.Memory`, `System.Buffers`, and other transitive NuGet dependencies were not deployed to the extension folder
- **Fix**: Changed Inno Setup installer from per-file DLL listing to `*.dll` wildcard pattern for all 6 targets

#### Issue 4: VSCT CTO Cross-Contamination

- **Symptom**: Build errors — all projects look for `AkmlSqlVS2026.cto` regardless of which project is being built
- **Root Cause**: Building via the solution file causes VSCT to use the last project's CTO output path
- **Fix**: Build each shell project individually with MSBuild, never via solution-level build

#### Additional SSMS 20 Fixes

- **pkgdef**: Added `Menus` registration entry, set `AllowsBackgroundLoad=dword:00000000` (synchronous), autoload flags `dword:00000000`
- **vsixmanifest**: Uses Schema 2010 (`<Vsix>` root) with `<IsolatedShell Version="1.0">ssms</IsolatedShell>`
- **Command signatures**: Changed all 5 command classes from `AsyncPackage` parameter to `Package` parameter
- **MEF Cache**: Located at `%LocalAppData%/Microsoft/SQL Server Management Studio/20.0_IsoShell/ComponentModelCache/` (not under `VisualStudio`)

### Milestone 3: SSMS 22 Extension Loading

**Status**: In Progress

#### Issue 5: Extension Visible in Extension Manager But No Menu

- **Symptom**: AKML SQL appears in Extensions > Manage Extensions but no menu item in the top menu bar
- **Root Cause 1 — Wrong Extension Path**: Files were initially deployed to root-level `Common7/IDE/Extensions/AkmlSql/`, but SSMS 22 executable lives under `Release/Common7/IDE/` and loads extensions from `Release/Common7/IDE/Extensions/`
- **Fix**: Deploy to `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql\`

#### Issue 6: Wrong vsixmanifest InstallationTarget

- **Symptom**: Extension not recognized as compatible with SSMS
- **Root Cause**: vsixmanifest targeted `Microsoft.VisualStudio.Community/Pro/Enterprise` instead of `Microsoft.VisualStudio.Ssms`
- **Fix**: Changed to `<InstallationTarget Id="Microsoft.VisualStudio.Ssms" Version="[17.0,)" />` with `AllUsers="true"`
- **Applied to**: SSMS 21 and SSMS 22 vsixmanifest files

#### Issue 7: Package Never AutoLoads — Wrong UI Context

- **Symptom**: pkgdef is imported (visible in activity log) but `Begin package load [AkmlSqlPackage]` never appears; menu never renders
- **Root Cause**: AutoLoad registered for `{e8fbc700-a1bd-11d0-a67c-00a0c9110051}` (`UICONTEXT_ShellInitialized`) which is a standard VS context. SSMS 22 uses its own context: `{B7B07F42-6013-4C67-A504-C771CBC7625A}` (`UICONTEXT_SSMS`)
- **Evidence**: Found in `SSMS.Application.pkgdef`: `[$RootKey$\AutoLoadPackages\{B7B07F42-6013-4C67-A504-C771CBC7625A}] @="UICONTEXT_SSMS"`
- **Fix**: Changed `[ProvideAutoLoad]` attribute and pkgdef to use `{B7B07F42-6013-4C67-A504-C771CBC7625A}`
- **Status**: Fix applied to source; awaiting redeployment and verification

#### Issue 8: PkgDef Cache Not Refreshing

- **Symptom**: Activity log shows `PkgDefCache fast check: timestamps are current` even after deploying new files
- **Root Cause**: The private registry hive (`privateregistry.bin`) caches pkgdef entries and the timestamp check doesn't detect new extension folders
- **Fix**: Delete `%LocalAppData%/Microsoft/SSMS/22.0_05e71b86/privateregistry.bin` (and `.LOG1`, `.LOG2`), plus clear `ComponentModelCache/`, `MEFCacheBackup/`, and CTM files
- **Alternative**: Run `SSMS.exe /updateconfiguration` from PowerShell (not Git Bash — path mangling converts `/updateconfiguration` to a file path)

---

## SSMS Version Differences — Quick Reference

| Aspect | SSMS 20 | SSMS 21/22 |
|--------|---------|------------|
| VS Shell Base | VS 2017 IsolatedShell | VS 2022 Shell |
| Platform | x86 | x64 |
| Package Base Class | `Package` (synchronous) | `AsyncPackage` |
| vsixmanifest Schema | 2010 (`<Vsix>`) | 2011 v2.0 (`<PackageManifest>`) |
| InstallationTarget | `<IsolatedShell>ssms</IsolatedShell>` | `Microsoft.VisualStudio.Ssms` |
| AutoLoad Context | `{e8fbc700-...}` (ShellInitialized) | `{B7B07F42-...}` (UICONTEXT_SSMS) |
| Extension Path | `<Root>/Common7/IDE/Extensions/AkmlSql/` | `<Root>/Release/Common7/IDE/Extensions/AkmlSql/` |
| MEF Cache | `%LocalAppData%/Microsoft/SQL Server Management Studio/20.0_IsoShell/ComponentModelCache/` | `%LocalAppData%/Microsoft/SSMS/22.0_*/ComponentModelCache/` |
| Activity Log | `%AppData%/Microsoft/SQL Server Management Studio/20.0_IsoShell/ActivityLog.xml` | `%AppData%/Microsoft/SSMS/22.0_*/ActivityLog.xml` |
| Activity Log Encoding | UTF-16LE | UTF-16LE |
| VS SDK Version | 15.9.3 | 17.14.x |
| AllowsBackgroundLoad | 0 (disabled) | 1 (enabled) |

## Cache Clearing Procedures

### SSMS 20

```powershell
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SQL Server Management Studio\20.0_IsoShell\ComponentModelCache"
```

### SSMS 22

```powershell
# Full cache reset (required when adding new extensions)
Remove-Item -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_05e71b86\privateregistry.bin*"
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_05e71b86\ComponentModelCache"
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_05e71b86\MEFCacheBackup"
Remove-Item -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_05e71b86\1033\SSMS.CTM*"

# Then rebuild configuration (run from PowerShell, NOT Git Bash)
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\SSMS.exe" /updateconfiguration
```

## Deployment Procedures

### Manual Deployment (Development)

```powershell
# SSMS 20
$src = "src\AkmlSql.Ssms20"
$dest = "<SSMS20Root>\Common7\IDE\Extensions\AkmlSql"
Copy-Item "$src\bin\Release\net472\*.dll" $dest
Copy-Item "$src\AkmlSql.Ssms20.pkgdef" $dest
Copy-Item "$src\source.extension.vsixmanifest" "$dest\extension.vsixmanifest"

# SSMS 22
$src = "src\AkmlSql.Ssms22"
$dest = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql"
Copy-Item "$src\bin\Release\net472\*.dll" $dest
Copy-Item "$src\AkmlSql.Ssms22.pkgdef" $dest
Copy-Item "$src\source.extension.vsixmanifest" "$dest\extension.vsixmanifest"
```

### Debugging with Activity Log

```powershell
# Launch with logging enabled
& "<SSMS_EXE_PATH>" /log

# Activity log location (UTF-16LE encoded)
# SSMS 20: %AppData%\Microsoft\SQL Server Management Studio\20.0_IsoShell\ActivityLog.xml
# SSMS 22: %AppData%\Microsoft\SSMS\22.0_05e71b86\ActivityLog.xml

# Reading from Git Bash (requires iconv for UTF-16)
iconv -f UTF-16LE -t UTF-8 "<path>/ActivityLog.xml" | grep -i "akml"

# Reading from PowerShell
Get-Content "<path>\ActivityLog.xml" -Encoding Unicode | Select-String "akml"
```

---

## Diagnostic Tools Used

1. **Activity Log Analysis**: `SSMS.exe /log` generates XML activity log with package load events and errors
2. **Assembly Reference Inspector**: Custom .NET 4.72 console app using `Assembly.ReflectionOnlyLoadFrom()` to verify DLL assembly references and embedded resources
3. **Inline MessageBox Diagnostic**: Replaced entire package init with inline `MessageBox.Show()` handlers to confirm command wiring works, isolating the issue to initialization order
4. **PkgDef Search Path Inspection**: Activity log reveals `PkgDefSearchPath` entries showing exactly which directories SSMS scans for extensions

---

*Last updated: 2026-03-17*
