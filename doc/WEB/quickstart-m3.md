# Quickstart — Web edition M3 (User Story 2)

This walks an operator through pairing the browser-side web edition with the local `AkmlSql.Engine` over WebSocket, both in localhost-only mode and in LAN mode (engine on Machine A, browser on Machine B). The whole exercise takes about 5 minutes once the install completes.

## Prerequisites

- Windows 10 / 11 with .NET 10 runtime (the installer bundles the engine and its dependencies).
- Inno Setup installer `AKMLSQLSetup.exe` from `src/AkmlSql.Installer/Output/`.
- For LAN mode: two machines on the same LAN. Machine A is the engine host; Machine B is the browser client.

## Section 1 — One-machine demo (localhost mode)

Use this to confirm the bridge is wired before going LAN.

1. **Install with `/WEB_EXPOSURE=LOCALHOST`**:

   ```
   AKMLSQLSetup.exe /WEB_EXPOSURE=LOCALHOST /BRIDGE_PORT=47291
   ```

   (`/BRIDGE_PORT` sets the engine bridge port; `/WEB_PORT` sets the separate **IIS** site port,
   default 80. The two must differ — `/WEB_PORT=47291` alone would collide with the default bridge
   port and be rejected. `/VERYSILENT` runs additionally require `/ACCEPTEULA`.)

   The installer writes `bridge` into `%ProgramData%\AKML SQL Web\config.json` — the config the
   `AkmlSqlWebEngine` Windows service reads (it is launched `--web --config "%CommonAppData%\AKML SQL Web\config.json"`).
   This is deliberately **not** the per-user IDE-plugin config `%AppData%\AKML SQL\config.json`, which the
   web installer never touches (spec 026 M4 closure C3):
   ```json
   {
     "bridge": {
       "enabled": true,
       "bindAddress": "127.0.0.1",
       "port": 47291,
       "tlsCertPath": ""
     }
   }
   ```

   *Verification*: Open `%ProgramData%\AKML SQL Web\config.json` and confirm the `bridge` section is present with `bindAddress: "127.0.0.1"`.

2. **Confirm the engine is listening**:

   ```
   netstat -an | findstr :47291
   ```

   Expected: a `TCP 127.0.0.1:47291 ... LISTENING` row.

   *Verification*: If no LISTENING row, check `%CommonAppData%\AKML SQL Web\install.log` for the bridge-config-writer entry.

3. **Open the web edition in a browser** at the install summary's URL (the IIS site defaults to port 80, i.e. `http://localhost/`; ad-hoc `dotnet run --project src/AkmlSql.Web` is also fine — it serves `http://localhost:5000/`).

4. **Click Add Connection** in the connection picker. For localhost, just enter Host `127.0.0.1` + Port `47291`; leave the PIN field blank (localhost mode auto-accepts).

   *Verification*: The status bar pill transitions `Connecting` → `Open` (shown as **Live**) within ~1 s. The right side panel's schema tree (shipped in US4) renders once the engine has an active SQL Server connection — see step 5.

5. **Type a `SELECT`** in the editor. Live-schema IntelliSense requires the **engine** to have an
   active SQL Server connection — established today by the SSMS/VS shell extension (or another
   client). The web connection picker pairs with the engine *bridge* but does **not** yet have a
   "connect to SQL Server" UI, so a web-only setup has no live schema and the editor falls back to
   keyword/snippet completions (the engine's `SchemaIdentify` reports no active session).

   *Verification*: with a live engine session present, completions stream from the live schema within
   ~100 ms of typing, and the status bar shows the engine version. (The engine's capability list is
   received at handshake and used to gate features, but is not displayed in the status bar.)

## Section 2 — LAN pair from a second machine

This is the operator-facing flow for the M3 PRD §1 promise.

1. **Install on Machine A with `/WEB_EXPOSURE=LAN`**:

   ```
   AKMLSQLSetup.exe /WEB_EXPOSURE=LAN /BRIDGE_PORT=47291
   ```

   The installer runs four PowerShell helpers (the first three during the install `[Run]` phase,
   then `web-config-bridge.ps1` in the post-install hook after the service is created + the
   shared-state dir is ACL-locked):
   - `web-iis-setup.ps1` — provisions an IIS site for the web bundle (on the IIS port, default 80).
   - `web-tls-setup.ps1` — generates a self-signed cert (RSA-2048, private key **NonExportable** in `LocalMachine\My`), exports the public part to `%ProgramData%\AKML SQL Web\certs\bridge.cer`, and binds the cert to the bridge port via `netsh http add sslcert ipport=0.0.0.0:<bridge-port> certhash=<thumb>` (default 47291; the TLS handshake uses the cert-store binding by thumbprint — no PFX file is written).
   - `web-firewall.ps1` — adds the inbound `AKML SQL Web Engine` rule for the bridge port on all profiles.
   - `web-config-bridge.ps1` (post-install) — writes the `bridge` section into `%ProgramData%\AKML SQL Web\config.json` with `bindAddress: "0.0.0.0"` and `tlsCertPath` pointing at `bridge.cer` (the engine loads it only to cross-check its thumbprint against the netsh binding).

   *Verification*: After install, `%CommonAppData%\AKML SQL Web\INSTALL-SUMMARY.txt` contains the LAN URL, the pairing PIN, and the TLS thumbprint. The Windows Firewall rule is visible in `netsh advfirewall firewall show rule name="AKML SQL Web Engine"`.

2. **Accept the Windows Firewall prompt** (if it appears on first engine start). The installer-created inbound rule should cover this preemptively; the prompt only appears if the rule didn't take.

   *Verification*: `netstat -an | findstr :47291` on Machine A shows a `TCP 0.0.0.0:47291 ... LISTENING` row. If the engine is bound but Machine B can't connect, the firewall rule is the most likely cause.

3. **On Machine B, open the web edition** at the IIS URL from Machine A's install summary —
   `http://<machine-a-hostname-or-ip>/` (the IIS site serves the bundle over HTTP on the IIS port,
   default 80). **Not** `:47291` — that's the WebSocket engine bridge and does not serve the HTML
   bundle. The bundle's JavaScript later opens the TLS bridge socket `wss://<machine-a>:47291`; for
   that the browser must trust the self-signed cert — accept it for trusted-LAN deployments, or
   pre-install `%ProgramData%\AKML SQL Web\certs\bridge.cer` into Trusted Root on Machine B (per the
   install summary's instructions).

   *Verification*: The editor page loads; the connection picker is empty (no connections yet); the bridge state pill shows `Disconnected`.

4. **Click Add Connection on Machine B**:
   - **Name**: e.g. "Office engine"
   - **Host**: Machine A's hostname or IP
   - **Port**: 47291
   - **IsLocalhost**: unchecked
   - **PIN**: the 6-digit value from Machine A's `INSTALL-SUMMARY.txt`

   Click **Pair**.

   *Verification*: The bridge transitions through `Connecting` → `Open` within a few seconds. The diagnostics ring buffer logs `Pinned TLS fingerprint for connection 'Office engine': …<last-12>`. The connection record is persisted to IndexedDB (visible in DevTools → Application → IndexedDB).

5. **Type a `SELECT`** in the editor. As in Section 1 step 5, live-schema completions require Machine A's
   **engine** to have an active SQL Server connection (via the shell extension); the web edition has no
   "connect to SQL Server" UI yet, so a web-only pairing gets keyword/snippet completions until such a
   session exists.

   *Verification*: with a live engine session, completions arrive within ~200 ms of typing; the status
   bar shows the engine version.

Close the tab and re-open the web edition. The active connection **auto-reconnects on startup** with **no PIN prompt** — localhost auto-accepts; LAN replays the wrapped bearer token from IndexedDB. (You can also reconnect manually with **Connect**; and if an *already-established* connection drops mid-session, the bridge auto-reconnects with exponential backoff — see the status-bar `Reconnecting · next try in Ns` countdown.)

## Section 3 — Troubleshooting

| Symptom | Probable cause | Fix |
|---------|---------------|-----|
| `netstat` shows nothing on the bridge port. | `config.json` has no `bridge` section or `enabled=false`. | Re-run the installer, or hand-edit `%ProgramData%\AKML SQL Web\config.json` (the service config) to add the `bridge` section per Section 1 step 1. |
| Engine refuses to start with "TlsCertPath does not exist…". | LAN mode chosen but the cert wasn't generated, or its path moved. | Re-run `web-tls-setup.ps1` from the installer payload, or set `bridge.tlsCertPath` in `config.json` to point at the actual cert (`bridge.cer`). |
| Engine refuses to start with "certificate thumbprint mismatch with netsh binding". | The netsh sslcert binding points at a different cert than the one at `bridge.tlsCertPath` (e.g., after a partial re-install). | The error message names both thumbprints — pick the one you want, then re-run `web-tls-setup.ps1` (regenerates the cert + netsh binding together). |
| Browser shows "Pairing PIN was wrong or expired". | The PIN expired (24-hour TTL), was already consumed, or you typed it wrong. | Restart the engine to mint a fresh PIN. The new value lands in `%CommonAppData%\AKML SQL Web\pairing-pin.txt`. |
| Machine B can't reach Machine A's port. | Windows Firewall blocked the inbound, or the engine is bound to 127.0.0.1 (localhost mode). | `netstat -an \| findstr :47291` on Machine A: expect `0.0.0.0:47291 LISTENING` for LAN. If `127.0.0.1`, run the installer with `/WEB_EXPOSURE=LAN`. Check the firewall rule with `netsh advfirewall firewall show rule name="AKML SQL Web Engine"`. |
| Diagnostics log shows `TLS fingerprint … changed from … to …`. | The cert was regenerated (installer re-run, or a manual `New-SelfSignedCertificate`). Per spec 025 §Out of Scope #1 there is no modal — just the log entry. | If expected (you re-ran the installer), no action needed. If unexpected, treat as a security incident: remove the connection from the picker and re-pair. |
| Browser fails to reach `wss://`, browser console says cert untrusted. | Self-signed cert not yet trusted on Machine B. | Install `%ProgramData%\AKML SQL Web\certs\bridge.cer` to "Trusted Root Certification Authorities" on Machine B (the install summary has the steps). |

## What is *not* in M3

- **Tab colouring per connection** — PRD §5 explicitly defers.
- **Snippets / refactoring / AI** — M5 / M6.
- **Multi-engine connections from one browser** — one connection at a time per browser.
- **A browser "connect to SQL Server" UI** — the web connection picker pairs with the engine *bridge* only; choosing the actual SQL database (so the engine has a live schema to serve) is done from the SSMS/VS shell extension. Until a web connect-to-SQL UI lands, the live-schema IntelliSense in steps 5 requires an engine session established by another client.
- **TLS fingerprint mismatch modal** — see `doc/m3-security.md` §"What is NOT covered" #1.
- **Engine-side tray pairing pane** — spec 021 T065, deferred.

## See also

- Threat model: [`doc/m3-security.md`](../m3-security.md)
- M1 / M2 quickstarts: [quickstart-m2.md](quickstart-m2.md) (M2 — in-browser format + analyse)
- M4 quickstart: [quickstart-m4.md](quickstart-m4.md) (the installer details)
- PRD: [M3-websocket-transport.md](M3-websocket-transport.md)
- Closure spec: [`specs/025-m3-bridge-closure/`](../../specs/025-m3-bridge-closure/)
