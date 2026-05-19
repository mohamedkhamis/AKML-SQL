# Quickstart — Web edition M4 (Installer)

This walks an installer-author through the one-click deploy flow: web bundle +
IIS site + LAN TLS cert + firewall rule + Windows service for the engine.

## Status

**This document describes the M4 surface as it lands.** The actual installer
run is the acceptance test for T081-T099 -- it needs Windows + Inno Setup 7 +
optional IIS + admin rights. The Pascal Script + three PowerShell helpers ship
as scaffolding; the first interactive install captures any deltas.

## Files

| File | Role |
|------|------|
| `src/AkmlSql.Installer/AkmlSqlSetup.iss` | Main installer script (already exists for the IDE plugins). |
| `src/AkmlSql.Installer/web-installer.iss` | M4 additions -- components, files, run actions, Pascal Script hooks. **Include from the main `.iss` per the integration note at the top of the file.** |
| `src/AkmlSql.Installer/web-iis-setup.ps1` | IIS site provisioning + MIME types + CSP header. |
| `src/AkmlSql.Installer/web-tls-setup.ps1` | Self-signed cert generation + `netsh http add sslcert`. |
| `src/AkmlSql.Installer/web-firewall.ps1` | `netsh advfirewall` inbound rule. |

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
       Port: [ 47291 ]
```

Component selection is independent: any combination of plugin and web-edition
checkboxes is valid (FR-002).

## What the installer does

1. **Web bundle** to `%ProgramFiles%\AKML SQL\Web\`.
2. **AppData**: `%AppData%\AKML SQL Web\` for the invoking user; per-machine
   shared state at `%CommonAppData%\AKML SQL Web\` (cert + install log + summary).
3. **IIS site** (if "Host on IIS"): site `AkmlSqlWeb`, MIME types for
   `.wasm` / `.dat` / `.blat` / `.br`, CSP header from
   `contracts/ai-key-wrapping.md`.
4. **TLS cert** (if "LAN exposed"): `New-SelfSignedCertificate` with
   `KeyExportPolicy NonExportable`, bound via
   `netsh http add sslcert ipport=0.0.0.0:<port>`.
5. **Firewall rule** (if "LAN exposed"): inbound TCP on the chosen port.
6. **Windows service**: `AkmlSqlWebEngine` (via `sc.exe create`) running the
   engine with `--config %AppData%/AKML SQL Web/config.json`.
7. **Capture PIN** from the engine log + **TLS thumbprint** from the cert
   script, write to `%CommonAppData%/AKML SQL Web/INSTALL-SUMMARY.txt`.

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
5. Delete `%ProgramFiles%\AKML SQL\Web\`.
6. **Prompt** before deleting `%AppData%\AKML SQL Web\` -- the user might
   want to keep their wrapped AI keys + connection records.
7. **NEVER** touch `%AppData%\AKML SQL\` (the IDE-plugin state). Acceptance
   gate SC-007.

## Silent install (T096)

```
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA \
  /COMPONENTS="web,web\iis,web\service" \
  /WEB_HOST=IIS /WEB_EXPOSURE=LAN /WEB_PORT=47291
```

Reject `/WEB_HOST=NONE` with `/WEB_EXPOSURE=LAN` (no host means no LAN
exposure point).

## First interactive test plan

1. Build the web bundle: `dotnet publish src/AkmlSql.Web -c Release`.
2. Build the engine: `dotnet publish src/AkmlSql.Engine -c Release -r win-x64`.
3. Compile the installer: `"C:\Program Files\Inno Setup 7\ISCC.exe" src/AkmlSql.Installer/AkmlSqlSetup.iss`.
4. Run `AKMLSQLSetup.exe` -- pick `Web edition`, leave IIS + localhost defaults.
5. Browse to `http://localhost:47291/`. Confirm the editor loads.
6. Uninstall via Control Panel. Re-run with `--LAN` -- confirm the cert + firewall rule appear.

## Deferred until first interactive run

- The hookup of `Web_Init` / `Web_NextButton` / `Web_Skip` / `Web_PostInstall`
  / `Web_Uninstall` into `AkmlSqlSetup.iss`'s existing event procedures (see
  the integration note at the top of `web-installer.iss`).
- The actual installer compile + run + screenshot capture.
- The re-run preservation check (T097).
- The silent-flag plumbing (T096).
