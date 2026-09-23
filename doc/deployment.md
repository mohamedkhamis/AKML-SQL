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
for TARGET in Ssms22 VS2026; do
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

The updater CLI has two modes (spec 036 US5):

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

---

## Extension Install Paths

| Target | Extension Directory |
|--------|---------------------|
| SSMS 22 | `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql\` |
| VS 2026 | `%LocalAppData%\Microsoft\VisualStudio\18.0_*\Extensions\AkmlSql\` |

> **SSMS 22 note**: The extension lives under the `Release/` subdirectory, not the root.

---

## MEF Cache Clearing

After installing, updating, or changing extension files, clear the MEF/component-model cache so the IDE picks up the new DLLs:

| Target | MEF Cache Path |
|--------|---------------|
| SSMS 22 | `%LocalAppData%\Microsoft\SSMS\22.0_*\ComponentModelCache\` |
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

## Web edition

The installer can also deploy the **web edition** — the browser-based AKML SQL served from local IIS, talking to the same engine binary over a WebSocket bridge. The web edition installs **independently** of the IDE plugins (pick plugins only, web only, or both) and keeps its own state under `%AppData%\AKML SQL Web\` — it never touches the IDE-plugin state at `%AppData%\AKML SQL\`.

> New in this release. The component is exercised end-to-end by the spec-026 first-interactive-run checklist; the flow below is the operator reference.

### Prerequisites

- Administrative rights (the installer already requires elevation).
- Windows 10 / 11 (or Windows Server with the Web Server role).
- **IIS** for the recommended "Host on local IIS" path. If IIS is absent, the installer offers to enable it (`dism /online /enable-feature /featurename:IIS-WebServerRole`), or you can choose "Don't host" and serve the files yourself.

### Component selection

Ticking **Web edition** adds four wizard pages:

| Page | Choices | Default |
|------|---------|---------|
| Hosting | Host on local IIS / Don't host | Host on local IIS |
| Network exposure | Localhost only / LAN exposed | Localhost only |
| IIS site port | the port you browse to | `80` |
| Engine bridge port | the WebSocket transport port | `47291` |

The two ports **must differ** — IIS serves the static bundle and the engine's WebSocket bridge is a separate listener; they cannot share a TCP port. The IIS-served bundle is plain **HTTP**; only the **bridge** uses TLS (`wss`) in LAN mode.

### Localhost mode

Browse to `http://localhost/` (or `http://localhost:<IISPort>/` if you changed the port). The engine bridge binds `127.0.0.1` and auto-accepts the loopback connection — no pairing PIN required.

### LAN mode

For pairing a browser on another machine:

1. The installer generates a self-signed TLS cert, binds it to the bridge port (`netsh http add sslcert`), and opens a firewall rule ("AKML SQL Web Engine").
2. The engine **enforces** a 6-digit pairing PIN at the handshake (wrong PIN → refused; correct PIN → a bearer token is minted and reused on later reconnects).
3. The install summary at `%CommonAppData%\AKML SQL Web\INSTALL-SUMMARY.txt` shows the browse URL, the bridge port, the **pairing PIN**, and the TLS thumbprint.
4. On the second machine: open **Settings → Engine connections → Add**, enter the engine computer's name or IP (a pasted `host:port` or `wss://…` URL also works), the **Bridge port** from the install summary — it is often *not* the default `47291` — and the PIN. The PIN field appears by itself for any address that is not this computer.
5. Trust the certificate, one of two ways:
   - **Per browser:** open `https://<host>:<bridge port>/akmlsql`, accept the certificate warning, and you should see *"AKML SQL engine: reachable"*. The pairing form links to this address when a connect fails.
   - **Per machine:** import `%ProgramData%\AKML SQL Web\certs\bridge.cer` into **Local Machine → Trusted Root Certification Authorities**. This needs the address you connect by to be in the certificate. The certificate lists the machine name, FQDN and the machine's own IPs; a public IP or DNS name the machine does not own (a cloud VM, NAT) must be added:

     ```powershell
     & "C:\Program Files (x86)\AKML SQL\Support\web-tls-setup.ps1" -Port <bridge port> `
         -PfxPath "C:\ProgramData\AKML SQL Web\certs\bridge.pfx" -ExtraNames 203.0.113.10 -RestartEngine
     ```

     Re-issuing changes the certificate, so browsers already paired will be asked to re-pair.

#### Do I need a remote engine?

Usually not. **To query a SQL Server on another machine, use the engine on your own computer** and add the server in **Connect to SQL Server** with its address (`host`, `host\instance` or `host,port`) and **SQL Server authentication**. The SQL Server machine needs nothing from AKML.

Windows authentication is limited to a SQL Server on the engine's own machine, as is any named-pipe/UNC address. The engine runs as a service (LocalSystem), so either would sign in as the service's account rather than as you — and would let a hostile page make the engine authenticate somewhere of its choosing. The web client explains the refusal; the engine enforces it again (`BridgeSqlTargetGuard`) for every request that arrives over the WebSocket bridge. Requests from the SSMS / Visual Studio extension (named pipe, running as the signed-in user) are not restricted.

Add a **remote engine** only when AKML SQL Web is installed in LAN mode on the other computer — for example to use Windows authentication *as that machine* against its local SQL Server.

#### When pairing fails

The browser reports every WebSocket failure the same way, so the pairing form shows ordered checks instead of the raw error:

| Symptom | Cause | Fix |
|---|---|---|
| "blocked by this page's Content-Security-Policy" | The page you are pairing *from* is a localhost install older than this release, whose policy only allowed local engines | Update AKML SQL Web on that computer, or run **Repair AKML SQL Web hosting** after updating |
| `https://<host>:<port>/akmlsql` does not load at all | Wrong port, engine service stopped, or the port is closed in Windows Firewall / a cloud firewall | Check the **Bridge port** in the install summary; `sc query AkmlSqlWebEngine`; open the port |
| The page loads after a certificate warning | The certificate was not trusted yet | You have just trusted it for this browser — pair again |

In localhost mode the engine also refuses WebSocket connections whose `Origin` is not this computer (localhost, a loopback IP, or the machine name), so an arbitrary website cannot drive the PIN-less local engine.

### Don't host (serve it yourself)

Choosing **Don't host** still lays the bundle down at `%ProgramFiles%\AKML SQL\Web\` and installs the engine service; only the IIS site is skipped. Serve the files with any static host, e.g.:

```
cd "C:\Program Files\AKML SQL\Web"
python -m http.server 8080
```

Then browse to `http://localhost:8080/`.

### Silent install

Component selection uses the native `/COMPONENTS` flag; the web sub-options use dedicated flags:

```
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA ^
  /COMPONENTS="web,web\iis,web\service" ^
  /WEB_HOST=IIS /WEB_EXPOSURE=LOCALHOST /WEB_PORT=80 /BRIDGE_PORT=47291
```

| Flag | Values | Meaning |
|------|--------|---------|
| `/WEB_HOST` | `IIS` \| `NONE` | host on IIS, or lay files down only |
| `/WEB_EXPOSURE` | `LOCALHOST` \| `LAN` | bridge binding + LAN cert/firewall |
| `/WEB_PORT` | `<int>` | IIS site port |
| `/BRIDGE_PORT` | `<int>` | engine bridge port |

Invalid combinations abort before any state is created: `/WEB_HOST=NONE /WEB_EXPOSURE=LAN` (LAN exposure needs a hosting mode) and `/WEB_PORT == /BRIDGE_PORT` (ports must differ).

### Uninstall

The web edition's uninstall stops + deletes the `AkmlSqlWebEngine` service, removes the firewall rule, deletes the `netsh` sslcert binding, removes the `AkmlSqlWeb` IIS site, deletes `%ProgramFiles%\AKML SQL\Web\`, and **prompts** before deleting `%AppData%\AKML SQL Web\` (your wrapped AI keys + connection records). `%AppData%\AKML SQL\` (IDE-plugin state) is never touched.

### Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| "Install succeeded" but the URL doesn't load | IIS wasn't installed. Re-run and choose "Enable IIS now", or pick "Don't host" and serve the files yourself. |
| Install fails / engine won't bind the port | Port collision. The wizard warns if the bridge port is in use — pick another. Check `netstat -ano \| findstr <port>`. |
| Silent install does nothing for the web component | Missing `/COMPONENTS="web,..."`, or admin rights. The installer requires elevation. |
| Service not running after install | Check **Event Viewer** + `%CommonAppData%\AKML SQL Web\install.log`; the install summary flags a non-running service. Start it: `sc start AkmlSqlWebEngine`. |
| Engine service stops on its own | It should not stay stopped: every install configures Windows to restart it (after 5 s, 10 s, then every 60 s, indefinitely) and starts it with *delayed* automatic start. Confirm with `sc qfailure AkmlSqlWebEngine` (expect three `RESTART` actions) and `sc qfailureflag AkmlSqlWebEngine` (expect `TRUE`). If either is missing, re-run the installer — it re-applies both on every run, including upgrades. Before this, recovery was never set by the installer, and even a hand-set recovery could not fire: the engine reported its failures to Windows as clean stops. |
| Engine service keeps restarting | Windows is doing its job; the engine is failing for a reason it logs. The service runs as LocalSystem, so its log is **not** under your profile: `C:\Windows\System32\config\systemprofile\AppData\Roaming\AKML SQL\logs\akmlsql-<date>.log`. Look for a `[FTL]` line — a disabled bridge section, a port already in use, or a missing TLS certificate are the usual causes. A normal stop logs "stop requested by the service control manager"; its absence before a restart means the process ended without being asked to. |

---

## Activity Logs and Diagnostics

| Target | Activity Log |
|--------|-------------|
| SSMS 22 | `%AppData%\Microsoft\SSMS\22.0_*\ActivityLog.xml` |
| VS 2026 | `%AppData%\Microsoft\VisualStudio\18.0_*\ActivityLog.xml` |

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

---

## Version Targeting Matrix

| Target | VS SDK | VSSDK.BuildTools | Platform | Shell Version |
|--------|--------|-----------------|----------|--------------|
| SSMS 22 | 17.14.* | 17.* | x64 | 17.0.0.0 |
| VS 2026 | 17.14.* | 17.* | x64 | 17.0.0.0 |
