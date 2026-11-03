# AKML SQL — Deployment Guide

## Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| .NET SDK | 10.0+ | Build Engine, Updater, Tests |
| MSBuild | 18.x (Visual Studio 18 Insiders) | Build the SSMS extension and the solution |
| Inno Setup | 7.x | Build installer |
| Visual Studio | 18 (Insiders) | Provides MSBuild and the VS SDK build tools |

---

## Build Commands

### Shell Extensions (MSBuild only — never `dotnet build`)

The whole solution builds in one pass with MSBuild (restore first); the SSMS extension can also be
built on its own:

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/18/Insiders/MSBuild/Current/Bin/MSBuild.exe"

# Whole solution
"$MSBUILD" AKML-SQL.slnx -t:Restore -v:quiet
"$MSBUILD" AKML-SQL.slnx -t:Build -p:Configuration=Release -m -v:minimal

# Restore and build the SSMS 22 extension (the only shell target; Visual Studio 2026 support was removed)
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build  -p:Configuration=Release -v:minimal
```

> **Critical**: Never `dotnet build` shell projects — the VS SDK's `CodeTaskFactory` needs full MSBuild.
> Building through the `.slnx` is fine again: the VSCT `.cto` cross-contamination is fixed (see
> CLAUDE.md › Build Gotchas). If `ctoFiles.json`, `resources.json` or `mergeCto.cache` ever appear at a
> drive root, it has come back.

### Engine (out-of-process IntelliSense host)

```bash
# Self-contained; single-file and trimming are OFF (Microsoft.Data.SqlClient's native SNI
# interop can't run from a single-file bundle)
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64
```

Output: `src/AkmlSql.Engine/bin/Release/net10.0/win-x64/publish/AkmlSql.Engine.exe`

PostgreSQL support (spec 042) adds a **native parser library** (`libpg_query`, loaded by the `pgsqlparser` package) to the engine publish, beside the managed `AkmlSql.PostgreSql.dll`
and `Npgsql.dll`. It needs nothing installed on the machine (it imports only the Windows base library) and the installer already packs `publish\*`, so no installer step is added;
that is also why single-file stays off. `postgreSql.enabled` in `config.json` (default true) switches the whole dialect off without removing any file. The engine publish must be
replaced as a whole: the `AkmlSql.*` assemblies are versioned per build, so a partial swap fails at load time.

### Updater

```bash
dotnet publish src/AkmlSql.Updater/AkmlSql.Updater.csproj \
  -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Output: `src/AkmlSql.Updater/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/AkmlSql.Updater.exe`
(a Windows target framework, for the update notifications; minimum Windows 10 1809 / Server 2019).

The updater CLI modes (spec 036 US5, plus the scheduled-update modes):

- `--scheduled` — what the *AKML SQL\Update Check* scheduled task runs: check (at most every 12
  hours), download + verify, then one Windows notification per version. Never installs.
- `--check-now` — the Start-menu *Check for AKML SQL updates*: the same, but always checks and
  always answers (ready / up to date / could not check), even with automatic updates off.
- `--install [akmlsql-update:install|details]` — what the notification's buttons run: re-verifies the
  downloaded installer and launches it with its normal UI, or opens the release notes.
- `--configure auto-update=on|off error-reports=on|off` — the installer records its options page
  through this, run as the signed-in user so the values land in their own `config.json`.

- `AkmlSql.Updater.exe --check` — fetches the update manifest from `Constants.UpdateManifestUrl`
  (`https://akml.khamis.work/update-manifest.json`), compares versions (strictly-newer only,
  SemVer pre-release tags stripped), and atomically writes `%AppData%\AKML SQL\update-available.json`
  when a newer version exists. Always exits 0 — a failed check is logged, never user-visible.
- `AkmlSql.Updater.exe --download` — reads the result file, downloads the installer to
  `%LocalAppData%\AKML SQL\cache\AKMLSQLSetup-<version>.exe.partial`, verifies its SHA-256
  against the manifest, and on success renames it and records `verifiedInstallerPath` with
  `downloadState: "verified"`. Exit codes: `0` success/nothing to do/cancelled, `1` usage error,
  `2` verification or download failure (`failureReason` set, partial file deleted). HTTPS-only,
  anonymous.

The shell's guided update flow (spec 036 FR-039) drives `--download` with visible progress and
a working cancel, asks for one confirmation naming the applications that must close, then
launches the verified installer with its normal UI. `/VERYSILENT` is never used by the
in-product flow — it remains the unattended-deployment path documented below only.

### Tests

```bash
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj -v minimal
```

**Live PostgreSQL tests (spec 042).** The tests that talk to a server read `AKML_PG_TEST_CONNECTION`, an Npgsql connection string, and skip when it is unset or the server is unreachable.
`scripts/pg-test-server.ps1 -Action Start|Status|Stop` runs a private cluster on port 55432 from a local PostgreSQL install (it never touches the installed service):

```powershell
scripts/pg-test-server.ps1 -Action Start
$env:AKML_PG_TEST_CONNECTION = 'Host=localhost;Port=55432;Database=postgres;Username=postgres;Password=akml'
dotnet test tests/AkmlSql.PostgreSql.Tests -v minimal                                         # language library, corpora, rules
dotnet test tests/AkmlSql.Engine.Tests --filter Category=PostgreSql -v minimal                # engine against the server
```

The browser tests (`tests/AkmlSql.Web.E2E.Tests`, class `PostgresWorkspaceTests`) need Debug builds of `AkmlSql.Web` and `AkmlSql.Engine`, the same variable, and a serial run
(`-- xUnit.ParallelizeTestCollections=false`).

### Installer (Inno Setup 7)

Requires that Engine and all shell targets are already built/published:

```bash
"/c/Program Files/Inno Setup 7/ISCC.exe" src/AkmlSql.Installer/AkmlSqlSetup.iss
```

Output: `src/AkmlSql.Installer/Output/AKMLSQLSetup.exe`

### Release publishing & the update manifest (spec 036)

`scripts/deploy-site-iis.ps1` stages a release and publishes the product site. As part of the
staging block (before the site publish step) it now writes **two** files from the same in-memory
release entry — one computation of version and SHA-256, two consumers (FR-036):

- `src/AkmlSql.Site/wwwroot/releases.json` — the download page's list (unchanged behaviour);
- `src/AkmlSql.Site/wwwroot/update-manifest.json` — the updater's manifest, served at
  `https://akml.khamis.work/update-manifest.json` (this is `Constants.UpdateManifestUrl`).

The manifest's `downloadUrl` is always absolute HTTPS: the GitHub CDN asset when the release
upload succeeded, otherwise the site's own `/dl/<file>` URL. Never hand-edit either file — the
consistency invariant is enforced by `tests/AkmlSql.Site.Tests`. The manifest must be written
before the site publish because `MapStaticAssets` resolves its asset list at build time; a file
dropped into `wwwroot` afterwards would 404 silently.

After a successful deploy (smoke tests passed) the script commits the files it generated —
`docs-metadata.json`, `releases.json` and `update-manifest.json` under `src/AkmlSql.Site/wwwroot` —
and pushes the current branch to `origin`. Only those three paths go into the commit (other staged or
modified files are left alone), nothing is committed when they did not change, and a git failure is
logged as a warning without failing the deploy. `-SkipGitPush` leaves them uncommitted.

---

## Extension Install Paths

| Target | Extension Directory |
|--------|---------------------|
| SSMS 22 | `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql\` |

> **SSMS 22 note**: The extension lives under the `Release/` subdirectory, not the root.

> **Visual Studio 2026**: no longer supported. On install, setup deletes any `Common7\IDE\Extensions\AkmlSql\`
> folder an earlier release put into a Visual Studio 2026 (`\2026\` or `\18\`) installation, closing Visual Studio
> first if it is running, and clears its `18.0_*\ComponentModelCache\`.

---

## MEF Cache Clearing

After installing, updating, or changing extension files, clear the MEF/component-model cache so the IDE picks up the new DLLs:

| Target | MEF Cache Path |
|--------|---------------|
| SSMS 22 | `%LocalAppData%\Microsoft\SSMS\22.0_*\ComponentModelCache\` |

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

# Install with automatic updates and anonymous error reports turned off
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /NOUPDATE /NOTELEMETRY

# Force-close running SSMS instances before installing
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /FORCECLOSEAPPS

# Import SQL Prompt formatting styles during installation
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /IMPORTSQLPROMPT
```

### Flags

| Flag | Description |
|------|-------------|
| `/VERYSILENT` | No UI, no progress dialog |
| `/ACCEPTEULA` | Accept the EULA (required when `/VERYSILENT` is used) |
| `/TARGETS=ssms22` | Target list. `ssms22` is the only target; `vs2026` from older scripts is accepted and ignored. If omitted, SSMS 22 is selected when it is found. |
| `/NOUPDATE` | Turn automatic updates off and do not create the update-check scheduled task |
| `/NOTELEMETRY` | Turn anonymous error reports off (they are on by default) |
| `/TELEMETRY` | Turn anonymous error reports on (the default; kept for older scripts) |
| `/FORCECLOSEAPPS` | Force-close running SSMS instances without prompting |
| `/IMPORTSQLPROMPT` | Import SQL Prompt formatting styles if SQL Prompt config is detected |
| `/LOG[=path]` | Write detailed install log. This is a native Inno Setup flag. If a path is given (`/LOG="C:\install.log"`), logs are written there. If no path is given (`/LOG`), Inno Setup writes to `%TEMP%\Setup Log YYYY-MM-DD #NNN.txt`. |

### Repair / Upgrade Behavior

The installer uses a fixed `AppId` and `UsePreviousAppDir=yes`, so re-running it over an existing installation performs an in-place upgrade. No prior uninstall is needed. User configuration (`config.json`, profiles, snippets, history) is preserved, and so are the user's choices for automatic updates and error reports: the options page shows their current values, and a silent upgrade leaves them alone unless `/NOUPDATE`, `/TELEMETRY` or `/NOTELEMETRY` says otherwise.

**64-bit install, and installs from before it.** The installer is 64-bit (`SetupArchitecture=x64`) and new installs go to `C:\Program Files\AKML SQL`. Installs made by the earlier 32-bit installer live in `C:\Program Files (x86)\AKML SQL`, with their uninstall entry in the 32-bit registry view, where a 64-bit installer does not look — on its own it would install a *second* copy. Setup takes such an install over instead (`MigrateLegacy32BitInstall` in `AkmlSqlSetup.iss`): it keeps the folder (and skips the folder page, as any upgrade does), restores the components chosen last time, starts a fresh uninstall log, and removes the old 32-bit Apps & features entry once the install succeeds (if setup fails, the old uninstaller is put back). The web edition's settings are read from either registry view. Each step was proven with probe installers: upgrade, upgrade again, uninstall, and a fresh install.

### Automatic updates

The installer registers a scheduled task, **Task Scheduler Library > AKML SQL > Update Check** (`update-task.ps1`, installed to `{app}\Support`), that runs `AkmlSql.Updater.exe --scheduled`:

- daily at 10:00 with a random delay of up to 4 hours, and 10 minutes after each sign-in;
- as the signed-in user (the Users group, least privilege), so it reads that user's own settings and cache;
- only with a network connection, hidden, at most one run at a time, 30-minute limit.

Each run honours the user's *Check for updates automatically* setting, skips the check if one (by the task or by SSMS) happened in the last 12 hours, downloads and verifies an offered installer against the manifest's SHA-256, and then shows a Windows notification **once per version**: *AKML SQL x.y is ready to install — Install now / Later*. **It never installs anything by itself**; *Install now* runs `AkmlSql.Updater.exe --install`, which re-verifies the downloaded file and launches the installer with its normal UI and Windows' admin prompt. SSMS and Visual Studio also offer a waiting update once at startup. The Start-menu shortcut *Check for AKML SQL updates* (`--check-now`) checks immediately and always answers — ready, up to date, or could not check.

Notifications need the Start-menu shortcut: Windows only shows notifications from an unpackaged app whose AppUserModelID (`AKML.AKMLSQL`) is on a Start-menu shortcut. The buttons use the `akmlsql-update:` URL scheme registered in `HKLM\Software\Classes`; it carries no data the updater acts on.

Unticking the option removes the task on the next install; `/NOUPDATE` does the same for silent installs. Old downloaded installers are removed from `%LocalAppData%\AKML SQL\cache` by the next check.

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
| Downloaded updates | `%LocalAppData%\AKML SQL\cache\AKMLSQLSetup-<version>.exe` |
| Program files | `C:\Program Files\AKML SQL\` (installs from before the 64-bit installer: `C:\Program Files (x86)\AKML SQL\`) |

---

## Uninstall

Via Windows Settings → Apps → "AKML SQL" → Uninstall, or:

```
AKMLSQLSetup.exe /UNINSTALL /VERYSILENT
```

The uninstaller removes extension files and MEF caches but leaves user data (config, snippets, profiles) intact.

---

## Web edition

The web edition installs independently of the SSMS extension. Its default host is
the **Standalone Windows service** (`AkmlSqlWebEngine`). One address serves both
the browser app and its engine: `http://localhost:47291/` by default. Open the URL
in `%ProgramData%\AKML SQL Web\INSTALL-SUMMARY.txt`; the actual chosen port wins.
The engine-served app connects to that address automatically when no saved engine
is active. An existing active connection remains the user's choice.

| Hosting choice | Pages | Service startup | Prerequisite |
| --- | --- | --- | --- |
| Standalone Windows service (recommended) | Engine port; `bridge.webRoot` points to `{app}\Web` | Automatic, no delay | No IIS requirement |
| IIS | IIS site port; engine serves the bridge only | Delayed automatic | IIS Ready |
| Don't host (files only) | Operator supplies a static host | Delayed automatic | None; engine service is still installed |

IIS Ready means IIS 7+ with W3SVC, Static Content, Default Document and HTTP Errors;
W3SVC and WAS must not be disabled, and the native PowerShell WebAdministration
probe must work. An unavailable IIS choice shows its reason. Setup never enables
Windows features. After enabling the named feature, go Back and Next to recheck.

### Exposure and ports

Localhost limits access to this computer. Standalone uses one engine/web port,
1024â€“65535. LAN standalone uses `https://COMPUTERNAME:<engine-port>/` on every
computer, including its own host, and only one inbound **AKML SQL Web Engine**
firewall rule. The page connects with `wss` and prefills the PIN form when needed.
Trust the exported `bridge.cer` on the client before pairing. The summary includes
the current PIN, certificate thumbprint and trust steps; keep those credentials private.

IIS keeps separate HTTP site, engine, and (in LAN mode) HTTPS site ports. Site port
80 and HTTPS port 443 are allowed; otherwise use 1024â€“65535. IIS ports must differ
from the engine port. LAN IIS has the engine rule and **AKML SQL Web Site (HTTPS)**
rule. Setup adds no HTTP-site firewall rule; IIS's existing HTTP binding remains
unchanged. Firewall rules apply to every profile, including Public. Use a Private
or Domain network. Localhost mode removes both named rules.

### Silent install

```text
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /COMPONENTS=web /WEB_HOST=SERVICE /WEB_EXPOSURE=LOCALHOST /BRIDGE_PORT=47291
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /COMPONENTS=web /WEB_HOST=IIS /WEB_PORT=8090 /BRIDGE_PORT=47291
```

`/WEB_HOST=SERVICE|IIS|NONE` selects the host. Without it, a new install selects
Service and an upgrade preserves its previous choice. If a previous IIS host is
no longer ready and no host flag is given, setup falls back to Service and logs
the reason and new address. An explicit `/WEB_HOST=IIS` stops when IIS is not ready.
`/WEB_EXPOSURE=LOCALHOST|LAN` and `/BRIDGE_PORT` preserve previous values when omitted.
`/WEB_PORT` and `/WEB_HTTPS_PORT` apply to IIS only. Explicit `NONE` plus `LAN`,
unknown flag values, conflicting ports and a standalone port held by another
program stop setup before it replaces application files.

### Upgrades, repair and uninstall

Changing host, port or standalone exposure can change the browser origin. Setup
shows both addresses before installation. Connections, query tabs and other browser
data at the old origin do not appear at the new one. Query history stays with the
engine. Use **Settings â€º Exportâ€¦** before switching and import at the new address.
Upgrades preserve a working host and its ports. Leaving IIS removes only the AKML
site, its unused pool, owned HTTPS binding and obsolete `web.config`.

Use **Repair AKML SQL Web hosting** from the Start menu to repair the recorded host.
The installed Support script validates its location and folder permissions before
and after elevation. An address that answers with another program's page is reported
as a failure, with the reason and repair steps in the install summary.

Machine state is `%ProgramData%\AKML SQL Web` (Administrators and SYSTEM only),
separate from the public `{app}\Web` bundle and desktop `%AppData%\AKML SQL`.
Custom installation trees must have a trusted owner, contain no reparse points,
and be protected before elevated Support scripts are used. Uninstall stops and
deletes the service, removes the named firewall rules, AKML IIS resources, unbound
AKML certificate and web bundle. Interactive uninstall asks about machine data;
silent uninstall keeps it and keeps desktop settings without prompts.

On a multi-session machine in localhost mode, another local user can occupy the
page origin while the service is stopped. Use a trusted machine/session; localhost
mode does not provide isolation between local Windows users.

## Activity Logs and Diagnostics

| Target | Activity Log |
|--------|-------------|
| SSMS 22 | `%AppData%\Microsoft\SSMS\22.0_*\ActivityLog.xml` |

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
3. Try a manual refresh: AKML SQL → Help → Refresh Schema Cache.

### Build failures

| Symptom | Cause | Fix |
|---------|-------|-----|
| `CodeTaskFactory` error | Built with `dotnet build` | Use MSBuild directly |
| Wrong assembly version | Stale NuGet/obj cache | Delete `obj/` and `bin/` then restore |
| CTO file missing | Built via solution | Build each project individually |

---

## Version Targeting Matrix

| Target | VS SDK | VSSDK.BuildTools | Platform | Shell Version |
|--------|--------|-----------------|----------|--------------|
| SSMS 22 | 17.14.* | 17.* | x64 | 17.0.0.0 |
