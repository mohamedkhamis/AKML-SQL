# M4 — Installer: IIS Deployment Option

**Status**: Draft
**Phase**: M4 (deployment)
**Estimated effort**: 1 week
**Branch prefix**: `m4-iis-installer`
**Depends on**: M2 shipped (M3 not strictly required; M4 can ship while M3 is in flight)

---

## 1. Executive summary

By the end of M4, the AKML SQL installer offers an additional component on the component-selection page: **"Web edition (local IIS)"**. When selected, the installer:

1. Detects whether IIS is installed; if not, prompts the user with an explanation and links to enable the Windows feature.
2. Asks whether the site should be **localhost-only** or **LAN-exposed**.
3. Creates an IIS site (or application under the Default Web Site) pointing at the WASM bundle directory.
4. Configures MIME types for `.wasm`, `.dat`, `.dll`, `.blat`, `.br` so Blazor WASM loads correctly.
5. Sets the engine's WebSocket transport to match the user's localhost/LAN choice from step 2.
6. Creates a Windows Firewall inbound rule if LAN mode was chosen.
7. Optionally creates a desktop shortcut to `http://localhost/akmlsql/` (or the configured URL).

This is the user-facing answer to "when to install it and also install to local IIS." The plugins and the web edition install independently — the user can pick plugins only, web only, or both, in any order.

---

## 2. Why now

M2 shipped a usable WASM bundle but the user had to figure out how to serve it. M3 added live schema but the WebSocket binding choice was a manual `config.json` edit. M4 makes the whole thing installable in one click. This is also the milestone that locks in the production deployment story — after M4, users don't run `dotnet publish` to use the web edition; they run an `.exe`.

---

## 3. Current state

End of M3:

- `AkmlSql.Installer` (Inno Setup 7) installs the SSMS / VS plugins, the engine binary, and the updater
- The component-selection page lists the 6 host targets (SSMS 20/21/22, VS 2019/22/26) as checkboxes
- No web edition component exists in the installer
- WASM bundle is built but distributed only as a `dotnet publish` output for developer testing
- Engine WebSocket transport binding is set manually in `config.json`

---

## 4. Proposed work

### 4.1 Component-selection page

A new component group is added:

```
[X] Plugins
    [X] SSMS 22
    [ ] SSMS 21
    [ ] SSMS 20
    [X] Visual Studio 2022
    [ ] Visual Studio 2026
    [ ] Visual Studio 2019

[X] Web edition (local)
    (•) Host on local IIS (recommended)
    ( ) Don't host — I'll serve the files myself

    Network exposure:
    (•) Localhost only — only my machine can browse
    ( ) LAN exposed — other machines on my network can browse
```

The "Don't host" option lays down the WASM bundle to `%ProgramFiles%/AKML SQL/Web/` but creates no IIS site. The user can serve it via any static host (their own IIS site, a Python `http.server`, etc.) or open `index.html` directly.

### 4.2 IIS detection logic

Inno Setup Pascal script:

```pascal
function IsIisInstalled(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\Microsoft\InetStp')
        and FileExists(ExpandConstant('{sys}\inetsrv\appcmd.exe'));
end;
```

If `Web edition → Host on local IIS` is chosen but `IsIisInstalled()` returns false:

- Show a dialog explaining IIS is required for this option
- Offer three paths:
  - "Enable IIS now" → run `dism /online /enable-feature /featurename:IIS-WebServerRole` (requires admin; show the command for transparency)
  - "Switch to 'Don't host' option" (and tell user how to enable IIS later)
  - "Cancel install"

### 4.3 IIS site / application creation

Using `appcmd.exe` (no PowerShell dependency):

```bat
appcmd.exe add app /site.name:"Default Web Site" /path:/akmlsql ^
    /physicalPath:"C:\Program Files\AKML SQL\Web"
appcmd.exe set config "Default Web Site/akmlsql" -section:staticContent ^
    /+[fileExtension='.wasm',mimeType='application/wasm']
appcmd.exe set config "Default Web Site/akmlsql" -section:staticContent ^
    /+[fileExtension='.dat',mimeType='application/octet-stream']
appcmd.exe set config "Default Web Site/akmlsql" -section:staticContent ^
    /+[fileExtension='.blat',mimeType='application/octet-stream']
appcmd.exe set config "Default Web Site/akmlsql" -section:staticContent ^
    /+[fileExtension='.br',mimeType='application/octet-stream']
```

URL after install: `http://localhost/akmlsql/`

If the user chose LAN mode, the URL becomes `http://<machine-name>/akmlsql/` and the firewall rule for port 80 is verified (it's typically open by default for IIS).

### 4.4 Engine WebSocket configuration

The installer writes to `%AppData%/AKML SQL Web/config.json` (separate from the plugin's `%AppData%/AKML SQL/config.json` — per the independence decision):

```json
{
  "webSocketTransport": {
    "enabled": true,
    "bindAddress": "127.0.0.1",   // or "0.0.0.0" if LAN
    "port": 47291,
    "requirePairingToken": false   // true if LAN
  }
}
```

The engine reads this on startup and binds accordingly.

### 4.5 Firewall rule (LAN mode only)

```bat
netsh advfirewall firewall add rule name="AKML SQL Engine (LAN)" ^
    dir=in action=allow protocol=TCP localport=47291 profile=private,domain
```

The rule is named distinctly so the uninstaller can remove only what we created.

### 4.6 Uninstall path

The installer's uninstall script:

1. Removes the IIS application/site
2. Removes the firewall rule if present
3. Removes the WASM bundle from `%ProgramFiles%`
4. Asks the user whether to delete `%AppData%/AKML SQL Web/` (per the convention used for the plugin's config)

---

## 5. Component independence (the "no shared state" answer)

The plugin and web edition install independently. This is the user-facing manifestation of the "independent — no shared state" decision:

| | Plugins | Web edition |
|---|---|---|
| Config | `%AppData%/AKML SQL/config.json` | `%AppData%/AKML SQL Web/config.json` |
| Logs | `%AppData%/AKML SQL/logs/` | `%AppData%/AKML SQL Web/logs/` |
| Schema cache | per-DB, in-engine memory | per-DB, in-engine memory (separate engine process) |
| Engine process | `akmlsql-engine.exe` started by shell | `akmlsql-engine.exe` started by Windows service / scheduled task / on-demand |
| Profiles | `%AppData%/AKML SQL/profiles/` | IndexedDB in browser; import/export only |

The same `akmlsql-engine.exe` binary is used by both — one shared executable, two independent runtime instances.

### 5.1 Engine lifecycle for the web edition

The plugins spawn the engine on demand (when SSMS/VS starts). The web edition has no "host process" — the user just opens a browser. Three options for engine lifecycle:

| Option | Pros | Cons |
|--------|------|------|
| Windows service | Always-on; survives reboots | Service install requires admin every time the engine is updated |
| Scheduled task at logon | Survives reboots; user-context | Doesn't survive log-off / RDP disconnect |
| On-demand via a tray app | Cheapest resources; clean lifecycle | Requires the tray app to be running |

**M4 ships with the tray-app option as the default.** A small `AkmlSql.Tray.exe` is added (~50 KB; existing `AkmlSql.Updater` patterns reused) that:

- Runs at logon (Run key)
- Owns the engine process for the web edition
- Shows a tray icon: green = engine running, yellow = starting, red = error
- Right-click menu: "Pair a device" (shows current PIN if LAN mode), "View logs", "Restart engine", "Exit"

Windows service mode can be added later (M4.6 or post-M4) as an advanced option for users who want it.

---

## 6. Milestones

### M4.1 — Component-selection page + plumbing (days 1–2)

Inno Setup script changes. Component groups. Conditional pages based on selection. No actual IIS or firewall work yet — wire the UI first.

### M4.2 — IIS detection + site creation (day 3)

`IsIisInstalled()`. Site/app creation via `appcmd.exe`. MIME type configuration. Tested on a clean Windows install with IIS pre-installed.

### M4.3 — IIS-not-installed handling (day 4)

The "enable IIS now" dialog. Verify the dism command actually works. Document the alternative "Don't host" path.

### M4.4 — Engine config writing + tray app (days 5–6)

`AkmlSql.Tray.exe` written. Run-at-logon registry key. Config writing. WebSocket config wiring tested with M3's transport.

### M4.5 — LAN mode + firewall + pairing UX (day 7)

`0.0.0.0` binding. Firewall rule. Tray "Pair a device" menu shows current PIN. End-to-end test: install with LAN option, open browser on a second machine, pair, see live schema.

### M4.6 — Uninstall + idempotency (week end)

Uninstall removes only what we created. Re-running the installer with different options reconfigures cleanly. Silent install flag (`/COMPONENTS=plugins,web /IISMODE=localhost`).

---

## 7. Risks and mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| User chooses "Host on local IIS" without admin rights | Medium | High | Installer is already admin-required; document; refuse gracefully |
| IIS is installed but the Default Web Site is bound to a non-standard port | Medium | Medium | `appcmd` query for the site's binding; use the actual port in the success URL |
| `appcmd.exe` is missing despite registry says IIS is installed | Low | Medium | Fall back to PowerShell `Import-Module IISAdministration`; if that's missing too, abort with a clear message |
| Tray app is killed by the user; engine dies; web edition unreachable | High | Medium | Engine restart attempts when tray reconnects; user sees a "Engine not running — start it from the tray" page |
| User installs the plugins later — they share the engine binary but should have independent configs | Medium | Medium | Tested: plugin install never touches `%AppData%/AKML SQL Web/`; web install never touches `%AppData%/AKML SQL/` |
| Port 47291 is already in use | Low | Low | On first start, scan upward; record actual port in config |

---

## 8. Success metrics

- Clean Windows machine: install with web edition + localhost → 60 seconds total, browser opens to working app
- Clean Windows machine with no IIS: install with web edition → user sees clear "enable IIS" guidance with a working `dism` command
- Two-machine LAN test: install on machine A with LAN mode, browser on machine B reaches the app, pairs, sees live schema
- Uninstall removes IIS site, firewall rule, tray run-at-logon entry, and program files — leaves user config behind unless user opts to delete
- Re-running installer to change from localhost to LAN mode (or back) works without uninstalling first

---

## 9. Out of scope

- HTTPS / TLS — needs a cert. Defer to a follow-up; for now, plain HTTP on local network
- Kestrel-as-Windows-service alternative — was a design option, rejected per user choice; document the option exists if a future user asks
- Reverse proxy support (nginx, IIS ARR) — out of scope
- Domain-joined enterprise install (GPO-driven) — separate planning cycle
- macOS / Linux deployment — Phase 16 in the product roadmap; not this plan
- Auto-update for the web edition — already handled by `AkmlSql.Updater`; M4 just adds the WASM bundle to its update manifest

---

## 10. Open questions

1. **IIS Express as a fallback for non-IIS users?** — Probably not; IIS Express is dev-only and not present on user machines by default. Stick with "enable IIS or use Don't host"
2. **Default URL — `/akmlsql` or `/akml-sql` or root?** — `/akmlsql` is shortest and matches the brand. Confirm in M4.1
3. **Tray app — same brand identity (Arabic "أكمل" tooltip) as the rest?** — Yes; the tray icon uses the AKML logo, tooltip text matches install language

---

## 11. Definition of done

- [ ] Installer offers web edition as a component
- [ ] Installer offers localhost vs LAN choice
- [ ] IIS detection works on machines with and without IIS
- [ ] IIS site created with correct MIME types
- [ ] WASM bundle deployed to `%ProgramFiles%/AKML SQL/Web/`
- [ ] Engine config written to `%AppData%/AKML SQL Web/`
- [ ] Tray app installed and runs at logon
- [ ] Firewall rule created in LAN mode
- [ ] Uninstall is clean
- [ ] Silent install works with `/COMPONENTS=` and `/IISMODE=`
- [ ] Documentation written: install guide, troubleshooting, "host it yourself" path
- [ ] Branch `m4-iis-installer` merged to master via PR
