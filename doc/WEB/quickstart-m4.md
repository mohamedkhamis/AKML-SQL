# Quickstart — Web edition M4 (Installer)

This walks an installer-author through the one-click deploy flow: web bundle +
IIS site + LAN TLS cert + firewall rule + Windows service for the engine.

## Status

The M4 installer is **wired and verified**. Spec 026 (M4 closure) integrated the
Pascal hooks (`#include` + `Web_Init`/`Web_NextButton`/`Web_Skip`/`Web_PostInstall`/
`Web_Uninstall` + `ShouldSkipPage`) into `AkmlSqlSetup.iss`, and the installer was
ISCC-compiled and run end-to-end (silent IIS + localhost; service + bridge + config +
SC-007 confirmed — see `specs/025-m3-bridge-closure/`/`026-m4-installer-closure/` evidence).
IIS site provisioning needs the **IIS Management Scripting Tools** Windows feature (the
`WebAdministration` COM provider); without it `web-iis-setup.ps1` fails and the install
continues without an IIS site (host the bundle yourself, see step 1).

## Files

| File | Role |
|------|------|
| `src/AkmlSql.Installer/AkmlSqlSetup.iss` | Main installer script (already exists for the IDE plugins). **`#include`s `web-installer.iss` (integration wired in spec 026).** |
| `src/AkmlSql.Installer/web-installer.iss` | M4 additions -- components, files, run actions, Pascal Script hooks. |
| `src/AkmlSql.Installer/web-iis-setup.ps1` | IIS site provisioning + MIME types + CSP header. |
| `src/AkmlSql.Installer/web-tls-setup.ps1` | Self-signed cert generation (`bridge.cer`; NonExportable key) + `netsh http add sslcert`. |
| `src/AkmlSql.Installer/web-firewall.ps1` | `netsh advfirewall` inbound rule. |
| `src/AkmlSql.Installer/web-config-bridge.ps1` | Writes the `bridge` section into the web-service config (`%CommonAppData%\AKML SQL Web\config.json`). |

## Component-selection page

When the user installs, the existing plugin checkboxes get a new sibling group:

```
Plugins
  [X] SSMS 22
  [ ] SSMS 21
  ...

Web edition (local)
  [ ] Install web edition
       Hosting:
         (*) Host on local IIS    -- recommended
         ( ) Don't host -- I'll serve the files myself
       Network exposure:
         (*) Localhost only       -- only my machine can browse
         ( ) LAN exposed          -- other machines on my network can browse
       IIS site port:   [ 80 ]      -- the port you browse to (http://localhost/)
       Engine bridge port: [ 47291 ] -- the WebSocket port; must differ from the IIS port
```

The IIS site port and the engine bridge port are **two separate values** (two wizard
pages: `WebIisPortPage` default 80, `WebBridgePortPage` default 47291). They must differ
(FR-024). You browse the **IIS** port; the engine serves WebSocket frames on the **bridge**
port.

Component selection is independent: any combination of plugin and web-edition
checkboxes is valid (FR-002).

## What the installer does

1. **Web bundle** to `%ProgramFiles(x86)%\AKML SQL\Web\` — the installer is 32-bit, so
   `DefaultDirName={autopf}\AKML SQL` resolves to **Program Files (x86)** on 64-bit Windows.
2. **Per-machine shared state** at `%CommonAppData%\AKML SQL Web\` (TLS cert + install log +
   `INSTALL-SUMMARY.txt` + the service `config.json` + the bearer-token store `tokens.json`).
   The web edition does **not** create a per-user `%AppData%\AKML SQL Web\` — the service runs as
   LocalSystem and reads only `%CommonAppData%`. (The separate per-user `%AppData%\AKML SQL\` is the
   IDE-plugin state, which the web installer never touches — SC-007.)
3. **IIS site** (if "Host on IIS"): site `AkmlSqlWeb`, MIME types for
   `.wasm` / `.dat` / `.blat` / `.br` / `.dll`, CSP header per
   `specs/021-web-edition/contracts/ai-key-wrapping.md`.
4. **TLS cert** (if "LAN exposed"): `New-SelfSignedCertificate` with
   `KeyExportPolicy NonExportable`, bound via
   `netsh http add sslcert ipport=0.0.0.0:<bridge-port>` (the cert protects the **engine bridge**
   WebSocket, not the IIS site — the IIS bundle is served over plain HTTP in both modes).
5. **Firewall rule** (if "LAN exposed"): inbound TCP on the **bridge** port (default 47291).
6. **Windows service**: `AkmlSqlWebEngine` (via `sc.exe create`) running the
   engine with `--web --config "%CommonAppData%\AKML SQL Web\config.json"` (machine-wide
   service config — **not** the per-user `%AppData%\AKML SQL\config.json` IDE config).
7. **Capture PIN** from `pairing-pin.txt` (the engine writes it there on startup — LAN only)
   + **TLS thumbprint** from the cert script's `thumbprint.txt`, write to
   `%CommonAppData%\AKML SQL Web\INSTALL-SUMMARY.txt`. (Localhost installs need no PIN — the
   summary records "Localhost only -- no LAN access. No pairing PIN required.")

## Re-run (T094)

When the user re-runs with a changed selection (localhost → LAN, or vice
versa), the installer:

- Generates a new cert + binding if LAN was just turned on.
- Adds/removes the firewall rule.
- **Preserves existing bearer tokens** stored by paired browsers -- the user
  doesn't need to re-pair.
- Regenerates the pairing PIN (the engine writes a new one on startup if
  asked).

## Uninstall (T095)

Reverse of install:

1. Stop + delete the `AkmlSqlWebEngine` service.
2. Remove the firewall rule.
3. `netsh http delete sslcert` for the bridge port.
4. Remove the `AkmlSqlWeb` IIS site.
5. Delete `%ProgramFiles(x86)%\AKML SQL\Web\` (auto-removed by Inno from the install log).
6. **Prompt** before deleting `%CommonAppData%\AKML SQL Web\` (cert + install log +
   summary + the bearer-token store `tokens.json`) -- keeping it lets paired browsers
   reconnect without re-pairing after a reinstall. Note: **wrapped AI keys and connection
   records do NOT live here** -- they're in the *browser's* IndexedDB (`aiKeys` /
   `connections` stores), which the installer never touches.
7. **NEVER** touch `%AppData%\AKML SQL\` (the IDE-plugin state). Acceptance
   gate SC-007. (Verified: a web-only install + uninstall leaves it intact.)

## Silent install (T096)

```
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA ^
  /COMPONENTS="web,web\iis,web\service" ^
  /WEB_HOST=IIS /WEB_EXPOSURE=LAN /WEB_PORT=80 /BRIDGE_PORT=47291
```

- `/VERYSILENT` **requires** `/ACCEPTEULA` (the installer aborts otherwise).
- `/WEB_PORT` is the **IIS site** port (default 80); `/BRIDGE_PORT` is the **engine bridge**
  port (default 47291). They must differ (FR-024) — e.g. `/WEB_PORT=47291` alone collides with
  the default bridge port and is rejected.
- Reject `/WEB_HOST=NONE` with `/WEB_EXPOSURE=LAN` (no host means no LAN exposure point).

## First interactive test plan

1. Build the web bundle: `dotnet publish src/AkmlSql.Web -c Release`.
2. Build the engine: `dotnet publish src/AkmlSql.Engine -c Release -r win-x64`.
3. Compile the installer: `"C:\Program Files\Inno Setup 7\ISCC.exe" src/AkmlSql.Installer/AkmlSqlSetup.iss`.
4. Run `AKMLSQLSetup.exe` -- pick `Web edition`, leave IIS + localhost defaults.
5. Browse to the **IIS site port** — `http://localhost/` (default 80), **not** the bridge port
   47291 (that's the WebSocket engine, which does not serve the HTML bundle). Confirm the editor loads.
6. Uninstall via Control Panel. Re-run with `/WEB_EXPOSURE=LAN` -- confirm the cert + firewall rule appear.

## Status of the "deferred" items

- ✅ **Hooks integrated** — `Web_Init` / `Web_NextButton` / `Web_Skip` / `Web_PostInstall` /
  `Web_Uninstall` + `ShouldSkipPage` are wired into `AkmlSqlSetup.iss` (spec 026).
- ✅ **Compile + run** — ISCC-compiles clean; a silent IIS+localhost install was run end-to-end
  (service `Running` with the correct `--web --config %CommonAppData%…` binPath, bridge port
  listening, `config.json` at the service path, SC-007 IDE config untouched).
- ✅ **Silent-flag plumbing (T096)** — `/WEB_HOST` `/WEB_EXPOSURE` `/WEB_PORT` `/BRIDGE_PORT`
  parse + validate (range, ports-differ, NONE+LAN rejected, IIS-missing-in-silent abort).
- ⏳ **Re-run preservation check (T097)** — still unverified interactively.
- ⏳ **IIS provisioning** needs the *IIS Management Scripting Tools* feature; on a box without it,
  `web-iis-setup.ps1` fails (`0x80040154 Class not registered`) and the install **continues
  without an IIS site** while still reporting success — host the bundle yourself in that case.
