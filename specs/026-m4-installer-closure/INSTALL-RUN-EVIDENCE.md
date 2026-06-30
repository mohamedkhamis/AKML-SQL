# M4 installer — compile + full install run + quickstart audit (2026-06-03)

Same interactive treatment as M6/M3, for the installer: ISCC-compiled `AkmlSqlSetup.iss`, ran a
**real silent install** on this (elevated, IIS-present) machine, verified the footprint, and
audited `doc/WEB/quickstart-m4.md` against the `.iss` + PS helpers. The **uninstall step was NOT
run** — see the safety note at the end.

## Compile

`"C:\Program Files\Inno Setup 7\ISCC.exe" src\AkmlSql.Installer\AkmlSqlSetup.iss` → **Successful
compile** → `Output\AKMLSQLSetup.exe`. (Prereqs published first: `dotnet publish src/AkmlSql.Web -c
Release` + `dotnet publish src/AkmlSql.Engine -c Release -r win-x64`.) One benign warning:
`[UninstallRun]` entries without `RunOnceId`.

## Install run (silent, IIS + localhost)

Command (note `/ACCEPTEULA` is **required** with `/VERYSILENT` — the first attempt aborted without it):

```
AKMLSQLSetup.exe /VERYSILENT /SUPPRESSMSGBOXES /ACCEPTEULA /NORESTART ^
  /WEB_HOST=IIS /WEB_EXPOSURE=LOCALHOST /WEB_PORT=8099 /BRIDGE_PORT=47291 ^
  /COMPONENTS=web,web\iis,web\service
```
→ exit 0. Verified:

| Check | Result |
|---|---|
| Service `AkmlSqlWebEngine` | **Running**, binPath `"…\AKML SQL\Engine\AkmlSql.Engine.exe" --web --config "C:\ProgramData\AKML SQL Web\config.json"` — the spec-026 `--web` + `%CommonAppData%` fix ✓ |
| `config.json` | at `%CommonAppData%\AKML SQL Web\config.json` (bridge enabled, 127.0.0.1, 47291, tokenStorePath set) ✓ |
| Bridge port 47291 | LISTENING ✓ |
| Web bundle | 578 files + `index.html` under `…\AKML SQL\Web` ✓ |
| **SC-007** | `%AppData%\AKML SQL` (IDE config) **untouched** (4 items) ✓ |
| IIS site `AkmlSqlWeb` | **NOT created** — `web-iis-setup.ps1` failed (see findings) |

## Findings + code fixes (this pass)

1. **IIS provisioning failed (environment gap, not an installer defect).** `install.log`:
   `[iis] ERROR: ... CLSID {688EEEE5-…} 0x80040154 Class not registered`. The `WebAdministration`
   COM provider needs the **IIS Management Scripting Tools** Windows feature; W3SVC runs here but
   that feature is absent, so `New-Website` can't run. Browse-verification of the IIS-hosted site
   was therefore impossible on this box. The deployed bundle is byte-identical to the publish
   output already verified to load (M2/M6 dev-server runs).
2. **Install reported success despite IIS failure → FIXED.** The IIS step is non-fatal
   (`web-iis-setup.ps1` exits 0 even on failure), so the install returned exit 0 + a success summary
   (`URL: http://localhost/`) with **no warning** the site wasn't created. **Fix:** `web-iis-setup.ps1`
   now writes an `iis-site.ok` marker only on the success path, and `Web_PostInstall` adds a
   **"WARNING: IIS hosting was selected but the AkmlSqlWeb site was NOT created"** block to the summary
   when the marker is absent. **Verified live** — re-running on this (IIS-scripting-broken) box, the
   summary now carries the warning and names the likely missing Windows feature.
3. **`bridge.pfx` in the engine TLS error strings → FIXED.** `WebSocketTransport.ValidateCertBindingOrThrow`
   accepts a `.cer` (the installer's actual output) but its two `InvalidOperationException` messages
   said `bridge.pfx` / "PFX thumbprint mismatch". Reworded to `bridge.cer` / "certificate thumbprint
   mismatch"; the M3 quickstart troubleshooting quote was synced.
4. **`/WEB_PORT` "not honored" — NOT a bug (my earlier diagnosis was wrong).** A diagnostic build logged
   `[web-flags] host=[IIS] exposure=[LOCALHOST] port=[8099] bridge=[47291]` — Inno's `{param:WEB_PORT|}`
   **does** parse the flag. The earlier "port 80 despite /WEB_PORT=8099" came from invoking the installer
   via PowerShell `Start-Process -ArgumentList @(array...)`, which mangled the args so Inno's `{param}`
   saw nothing → the install fell back to the interactive defaults (IIS/localhost/80) and silently
   ignored the web flags. Passed as a **single argument string**, the flags parse and the install then
   **correctly aborts** (exit 5) because `IsIisInstalled()` returns false on this box (FR-019: silent +
   `/WEB_HOST=IIS` + IIS-missing → abort). No installer change needed; the diagnostic logging was reverted.

   > **Caveat on the earlier "verified" install:** because the array-arg runs ignored the web flags, the
   > install that confirmed service/config/bridge/SC-007 ran with the **defaults** (IIS host, localhost,
   > IIS port 80) — those checks don't depend on the port, so they stand. The IIS port itself (8099) was
   > never exercised here because IIS provisioning is env-blocked.

## Quickstart-m4.md audit — fixed in the doc

| # | Was | Now |
|---|---|---|
| 1 | Service `--config %AppData%/AKML SQL Web/config.json` | `--web --config %CommonAppData%\AKML SQL Web\config.json` (web-installer.iss:93) |
| 2 | Single "Port: [47291]" | two ports — IIS site (default 80, what you browse) + engine bridge (default 47291); must differ (FR-024) |
| 3 | Test plan: browse `http://localhost:47291/` | browse the IIS port `http://localhost/`; 47291 is the WebSocket bridge, not the bundle host |
| 4 | Uninstall: `%AppData%\AKML SQL Web\` holds "wrapped AI keys + connection records" | those live in the **browser's IndexedDB** (`aiKeys`/`connections`); the prompt is about `%CommonAppData%\AKML SQL Web\` (cert/log/summary/`tokens.json`) |
| 5 | Silent example `/WEB_PORT=47291` | collides with default bridge 47291 → rejected; corrected to `/WEB_PORT=80 /BRIDGE_PORT=47291` + noted `/ACCEPTEULA` is required |
| 6 | "three PowerShell helpers"; Files table lists 3 | **four** — added `web-config-bridge.ps1` |
| 7 | Status/"Deferred": "scaffolding"; hookup deferred | spec 026 wired the hooks; compile + silent install verified; T097 re-run + IIS-feature noted as the genuine remaining items |

## Uninstall — manual web-only cleanup (owner-chosen)

This machine already has the **SSMS 22 plugin installed** (`…\SQL Server Management Studio 22\…\
Extensions\AkmlSql`) under the **same fixed Inno AppId** (`{F7E8A9B0-…}`) as `unins000.exe`. Inno's
uninstaller removes *everything in its uninstall log*, so running it would have deleted the SSMS
plugin + shared runtime too — destructive to pre-existing state this test didn't create. So rather
than run the full uninstaller, the owner chose a **manual web-only cleanup**:

- ✅ `sc delete AkmlSqlWebEngine` (the auto-start service this install created) → gone; bridge port 47291 free.
- ✅ removed the web bundle dir `…\AKML SQL\Web`.
- ✅ reverted `%CommonAppData%\AKML SQL Web\` to the pre-test snapshot (my `config.json` removed; the
  pre-existing `bridge.cer`/summary/log restored).
- **Preserved (verified intact):** the SSMS 22 plugin, the shared `Engine` dir + runtime DLLs,
  `unins000.exe`, and — **SC-007** — `%AppData%\AKML SQL` (its `config.json` is untouched, last-write
  5/16/2026, predating this session; the live `history` DB is open in SSMS).

The prior footprint (snapshotted to `.tmp-m4-snapshot/` during the run) was a *stale, non-functioning*
web install (service pointed at a missing `%AppData%` config) plus a leftover LAN cert/firewall — all
removed before the clean install and **not** restored (stale cruft pointing at a deleted service).

> **Note:** the full Inno uninstall flow (`unins000.exe`) was therefore **not** exercised on this
> machine. T097 (re-run preservation) + the full uninstall remain for a disposable VM where the AppId
> isn't shared with a live IDE-plugin install.
