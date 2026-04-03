# AKML SQL — Deployment Guide

## Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| .NET SDK | 10.0+ | Build Engine, Updater, Tests |
| MSBuild | 17.x (VS 2022) | Build Shell extensions |
| Inno Setup | 7.x | Build installer |
| Visual Studio | 2022 | Required for MSBuild |

---

## Build Commands

### Shell Extensions (MSBuild only — never `dotnet build`)

Shell projects must be built individually with MSBuild to avoid VSCT `.cto` cross-contamination:

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"

# Restore and build each target separately
for TARGET in Ssms20 Ssms21 Ssms22 VS2019 VS2022 VS2026; do
  "$MSBUILD" "src/AkmlSql.$TARGET/AkmlSql.$TARGET.csproj" \
    -t:Restore -p:Configuration=Release -v:quiet
  "$MSBUILD" "src/AkmlSql.$TARGET/AkmlSql.$TARGET.csproj" \
    -t:Build  -p:Configuration=Release -v:minimal
done
```

> **Critical**: Never `dotnet build` shell projects. Never build via the `.slnx` solution — VSCT CTO files will collide.

### Engine (out-of-process IntelliSense host)

```bash
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj \
  -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Output: `src/AkmlSql.Engine/bin/Release/net10.0/win-x64/publish/AkmlSql.Engine.exe`

### Updater

```bash
dotnet publish src/AkmlSql.Updater/AkmlSql.Updater.csproj \
  -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

### Tests

```bash
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj -v minimal
```

### Installer (Inno Setup 7)

Requires that Engine and all shell targets are already built/published:

```bash
"/c/Program Files/Inno Setup 7/ISCC.exe" src/AkmlSql.Installer/AkmlSqlSetup.iss
```

Output: `src/AkmlSql.Installer/Output/AKMLSQLSetup.exe`

---

## Extension Install Paths

| Target | Extension Directory |
|--------|---------------------|
| SSMS 20 | `%CommonProgramFiles(x86)%\Microsoft SQL Server\150\Tools\Binn\ManagementStudio\Extensions\AkmlSql\` |
| SSMS 21 | `<SSMS21Root>\Common7\IDE\Extensions\AkmlSql\` |
| SSMS 22 | `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql\` |
| VS 2019 | `%LocalAppData%\Microsoft\VisualStudio\16.0_*\Extensions\AkmlSql\` |
| VS 2022 | `%LocalAppData%\Microsoft\VisualStudio\17.0_*\Extensions\AkmlSql\` |
| VS 2026 | `%LocalAppData%\Microsoft\VisualStudio\18.0_*\Extensions\AkmlSql\` |

> **SSMS 22 note**: The extension lives under the `Release/` subdirectory, not the root.

---

## MEF Cache Clearing

After installing, updating, or changing extension files, clear the MEF/component-model cache so the IDE picks up the new DLLs:

| Target | MEF Cache Path |
|--------|---------------|
| SSMS 20 | `%LocalAppData%\Microsoft\SQL Server Management Studio\20.0_IsoShell\ComponentModelCache\` |
| SSMS 21 | `%LocalAppData%\Microsoft\SSMS\21.0_*\ComponentModelCache\` |
| SSMS 22 | `%LocalAppData%\Microsoft\SSMS\22.0_*\ComponentModelCache\` |
| VS 2019 | `%LocalAppData%\Microsoft\VisualStudio\16.0_*\ComponentModelCache\` |
| VS 2022 | `%LocalAppData%\Microsoft\VisualStudio\17.0_*\ComponentModelCache\` |
| VS 2026 | `%LocalAppData%\Microsoft\VisualStudio\18.0_*\ComponentModelCache\` |

```powershell
# PowerShell: clear all SSMS 22 MEF caches
Remove-Item "$env:LOCALAPPDATA\Microsoft\SSMS\22*\ComponentModelCache" -Recurse -Force
```

The installer script (`AkmlSqlSetup.iss`) runs this automatically via Pascal Script after file copy.

---

## Silent Installation

The installer supports fully unattended installation for scripted deployments, group policy, and CI/CD pipelines.

### Basic Usage

```
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA
```

### Examples

```bash
# Install to specific targets only
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /TARGETS=ssms22,vs2022

# Install with verbose logging (for troubleshooting)
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /LOG="C:\Logs\akmlsql-install.log"

# Install with auto-update and telemetry disabled
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /NOUPDATE /NOTELEMETRY

# Force-close running SSMS/VS instances before installing
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /FORCECLOSEAPPS

# Import SQL Prompt formatting styles during installation
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /IMPORTSQLPROMPT
```

### Flags

| Flag | Description |
|------|-------------|
| `/VERYSILENT` | No UI, no progress dialog |
| `/ACCEPTEULA` | Accept the EULA (required when `/VERYSILENT` is used) |
| `/TARGETS=ssms22,vs2022` | Comma-separated target list: `ssms20`, `ssms21`, `ssms22`, `vs2019`, `vs2022`, `vs2026`. If omitted, all detected targets are selected. |
| `/NOUPDATE` | Disable the built-in auto-update check |
| `/TELEMETRY` | Enable anonymous usage telemetry (off by default) |
| `/NOTELEMETRY` | Explicitly disable telemetry |
| `/FORCECLOSEAPPS` | Force-close running SSMS/VS instances without prompting |
| `/IMPORTSQLPROMPT` | Import SQL Prompt formatting styles if SQL Prompt config is detected |
| `/LOG[=path]` | Write detailed install log. This is a native Inno Setup flag. If a path is given (`/LOG="C:\install.log"`), logs are written there. If no path is given (`/LOG`), Inno Setup writes to `%TEMP%\Setup Log YYYY-MM-DD #NNN.txt`. |

### Repair / Upgrade Behavior

The installer uses a fixed `AppId` and `UsePreviousAppDir=yes`, so re-running the installer over an existing installation performs an in-place upgrade. No prior uninstall is needed. User configuration (`config.json`, profiles, snippets) is preserved across upgrades.

---

## Application Data Paths

| Artifact | Path |
|----------|------|
| Config file | `%AppData%\AKML SQL\config.json` |
| Logs | `%AppData%\AKML SQL\logs\akmlsql-YYYYMMDD.log` |
| Schema cache | `%LocalAppData%\AKML SQL\cache\` |
| Formatting profiles | `%AppData%\AKML SQL\profiles\` |
| Personal snippets | `%AppData%\AKML SQL\snippets\personal\` |
| Update result | `%AppData%\AKML SQL\update-available.json` |

---

## Uninstall

Via Windows Settings → Apps → "AKML SQL" → Uninstall, or:

```
AKMLSQLSetup.exe /UNINSTALL /VERYSILENT
```

The uninstaller removes extension files and MEF caches but leaves user data (config, snippets, profiles) intact.

---

## Activity Logs and Diagnostics

| Target | Activity Log |
|--------|-------------|
| SSMS 20 | `%AppData%\Microsoft\SQL Server Management Studio\20.0_IsoShell\ActivityLog.xml` |
| SSMS 22 | `%AppData%\Microsoft\SSMS\22.0_*\ActivityLog.xml` |
| VS 2022 | `%AppData%\Microsoft\VisualStudio\17.0_*\ActivityLog.xml` |

To enable VS/SSMS activity logging, launch with `/log`:

```
ssms.exe /log
devenv.exe /log
```

AKML SQL writes its own rolling logs to `%AppData%\AKML SQL\logs\`. Set `logMinimumLevel` in `config.json` to `"Verbose"` or `"Debug"` for maximum detail.

---

## Troubleshooting

### Extension not loading

1. Check `ActivityLog.xml` for MEF composition errors.
2. Clear the MEF cache for the target IDE and restart.
3. Verify the extension files are in the correct directory (see [Extension Install Paths](#extension-install-paths)).
4. For SSMS 22, confirm files are in the `Release/` subdirectory.

### Engine process not starting

1. Check `%AppData%\AKML SQL\logs\` for startup errors.
2. Verify `AkmlSql.Engine.exe` is present alongside the shell DLL.
3. Run `AkmlSql.Engine.exe` from a command prompt — it will print any startup errors.
4. Ensure .NET 10 runtime is not required (the engine is self-contained).

### IntelliSense not appearing

1. Verify the engine is running: check Task Manager for `AkmlSql.Engine.exe`.
2. Check config: `intelliSense.enabled` must be `true`.
3. If native SSMS IntelliSense conflicts: open Options → AKML SQL → IntelliSense, enable "Disable native IntelliSense".
4. Check the engine log for connection errors on the named pipe.

### Schema not loading

1. Verify the connection has `VIEW DATABASE STATE` and `VIEW ANY DEFINITION` permissions.
2. Check `%AppData%\AKML SQL\logs\` for `SchemaMetadataService` errors.
3. Try a manual refresh: Tools → AKML SQL → Refresh Schema Cache.

### Build failures

| Symptom | Cause | Fix |
|---------|-------|-----|
| `CodeTaskFactory` error | Built with `dotnet build` | Use MSBuild directly |
| Wrong assembly version | Stale NuGet/obj cache | Delete `obj/` and `bin/` then restore |
| CTO file missing | Built via solution | Build each project individually |
| `Shell.15.0.0.0` not found | Wrong VS SDK version for SSMS 20 | Verify VSSDK.BuildTools 15.* is restored |

---

## Version Targeting Matrix

| Target | VS SDK | VSSDK.BuildTools | Platform | Shell Version |
|--------|--------|-----------------|----------|--------------|
| SSMS 20 | 15.9.3 | 15.* | x86 | 15.0.0.0 |
| SSMS 21 | 17.14.* | 17.* | x64 | 17.0.0.0 |
| SSMS 22 | 17.14.* | 17.* | x64 | 17.0.0.0 |
| VS 2019 | 16.0.208 | 16.* | x86 | 16.0.0.0 |
| VS 2022 | 17.14.* | 17.* | x64 | 17.0.0.0 |
| VS 2026 | 17.14.* | 17.* | x64 | 17.0.0.0 |
