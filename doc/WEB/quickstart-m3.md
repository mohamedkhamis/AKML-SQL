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
   AKMLSQLSetup.exe /WEB_EXPOSURE=LOCALHOST /WEB_PORT=47291
   ```

   The installer writes `bridge` into `%AppData%\AKML SQL\config.json`:
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

   *Verification*: Open `%AppData%\AKML SQL\config.json` and confirm the `bridge` section is present with `bindAddress: "127.0.0.1"`.

2. **Confirm the engine is listening**:

   ```
   netstat -an | findstr :47291
   ```

   Expected: a `TCP 127.0.0.1:47291 ... LISTENING` row.

   *Verification*: If no LISTENING row, check `%CommonAppData%\AKML SQL Web\install.log` for the bridge-config-writer entry.

3. **Open the web edition in a browser** at the install summary's URL (defaults to `http://localhost:5081/` for the IIS site; ad-hoc `dotnet run --project src/AkmlSql.Web` is also fine).

4. **Click Add Connection** in the connection picker. For localhost, just enter Host `127.0.0.1` + Port `47291`; leave the PIN field blank (localhost mode auto-accepts).

   *Verification*: The status bar transitions through `Connecting` → `Open` within 1 s. The right side panel renders the live schema tree (once US4 lands — until then, only the bridge state pill changes).

5. **Type a `SELECT`** in the editor; observe IntelliSense from the live schema (requires a database connection picked in the connection picker).

   *Verification*: Completions arrive within ~100 ms of typing; the status bar shows the engine version and capability list.

## Section 2 — LAN pair from a second machine

This is the operator-facing flow for the M3 PRD §1 promise.

1. **Install on Machine A with `/WEB_EXPOSURE=LAN`**:

   ```
   AKMLSQLSetup.exe /WEB_EXPOSURE=LAN /WEB_PORT=47291
   ```

   The installer runs four PowerShell helpers in sequence:
   - `web-iis-setup.ps1` — provisions an IIS site for the web bundle.
   - `web-tls-setup.ps1` — generates `%ProgramData%\AKML SQL Web\certs\bridge.pfx` and binds it to the port via `netsh http add sslcert ipport=0.0.0.0:47291 certhash=<thumb>`.
   - `web-firewall.ps1` — adds the inbound `AKML SQL Web Engine` rule for TCP 47291 on all profiles.
   - `web-config-bridge.ps1` — writes the `bridge` section into `%AppData%\AKML SQL\config.json` with `bindAddress: "0.0.0.0"` and the PFX path.

   *Verification*: After install, `%CommonAppData%\AKML SQL Web\INSTALL-SUMMARY.txt` contains the LAN URL, the pairing PIN, and the TLS thumbprint. The Windows Firewall rule is visible in `netsh advfirewall firewall show rule name="AKML SQL Web Engine"`.

2. **Accept the Windows Firewall prompt** (if it appears on first engine start). The installer-created inbound rule should cover this preemptively; the prompt only appears if the rule didn't take.

   *Verification*: `netstat -an | findstr :47291` on Machine A shows a `TCP 0.0.0.0:47291 ... LISTENING` row. If the engine is bound but Machine B can't connect, the firewall rule is the most likely cause.

3. **On Machine B, open the web edition** at `https://<machine-a-hostname-or-ip>:47291/` (the LAN URL from Machine A's install summary). The browser will warn about the self-signed cert — accept it for trusted-LAN deployments (or pre-install the public cert from `%ProgramData%\AKML SQL Web\certs\bridge.cer` per the install summary's instructions).

   *Verification*: The editor page loads; the connection picker is empty (no connections yet); the bridge state pill shows `Disconnected`.

4. **Click Add Connection on Machine B**:
   - **Name**: e.g. "Office engine"
   - **Host**: Machine A's hostname or IP
   - **Port**: 47291
   - **IsLocalhost**: unchecked
   - **PIN**: the 6-digit value from Machine A's `INSTALL-SUMMARY.txt`

   Click **Pair**.

   *Verification*: The bridge transitions through `Connecting` → `Open` within a few seconds. The diagnostics ring buffer logs `Pinned TLS fingerprint for connection 'Office engine': …<last-12>`. The connection record is persisted to IndexedDB (visible in DevTools → Application → IndexedDB).

5. **Type a `SELECT`** in the editor; observe completions arriving from Machine A's live schema.

   *Verification*: Completions arrive within ~200 ms of typing. The status bar shows the engine version + capability list.

Close the tab and re-open the web edition. The bridge auto-connects without a PIN prompt — the wrapped bearer token in IndexedDB authenticates the reconnect.

## Section 3 — Troubleshooting

| Symptom | Probable cause | Fix |
|---------|---------------|-----|
| `netstat` shows nothing on the bridge port. | `config.json` has no `bridge` section or `enabled=false`. | Re-run the installer, or hand-edit `%AppData%\AKML SQL\config.json` to add the `bridge` section per Section 1 step 1. |
| Engine refuses to start with "TlsCertPath does not exist…". | LAN mode chosen but the PFX wasn't generated, or its path moved. | Re-run `web-tls-setup.ps1` from the installer payload, or set `bridge.tlsCertPath` in `config.json` to point at the actual PFX. |
| Engine refuses to start with "PFX thumbprint mismatch with netsh binding". | The netsh sslcert binding points at a different cert than the configured PFX (e.g., after a partial re-install). | The error message names both thumbprints — pick the one you want, then either re-run `web-tls-setup.ps1` (regenerates the netsh binding from the PFX) or replace the PFX. |
| Browser shows "Pairing PIN was wrong or expired". | The PIN expired (5-min TTL), was already consumed, or you typed it wrong. | Restart the engine to mint a fresh PIN. The new value lands in `%CommonAppData%\AKML SQL Web\pairing-pin.txt`. |
| Machine B can't reach Machine A's port. | Windows Firewall blocked the inbound, or the engine is bound to 127.0.0.1 (localhost mode). | `netstat -an \| findstr :47291` on Machine A: expect `0.0.0.0:47291 LISTENING` for LAN. If `127.0.0.1`, run the installer with `/WEB_EXPOSURE=LAN`. Check the firewall rule with `netsh advfirewall firewall show rule name="AKML SQL Web Engine"`. |
| Diagnostics log shows `TLS fingerprint … changed from … to …`. | The cert was regenerated (installer re-run, or a manual `New-SelfSignedCertificate`). Per spec 025 §Out of Scope #1 there is no modal — just the log entry. | If expected (you re-ran the installer), no action needed. If unexpected, treat as a security incident: remove the connection from the picker and re-pair. |
| Browser fails to reach `wss://`, browser console says cert untrusted. | Self-signed cert not yet trusted on Machine B. | Install `%ProgramData%\AKML SQL Web\certs\bridge.cer` to "Trusted Root Certification Authorities" on Machine B (the install summary has the steps). |

## What is *not* in M3

- **Tab colouring per connection** — PRD §5 explicitly defers.
- **Snippets / refactoring / AI** — M5 / M6.
- **Multi-engine connections from one browser** — one connection at a time per browser.
- **TLS fingerprint mismatch modal** — see `doc/m3-security.md` §"What is NOT covered" #1.
- **Engine-side tray pairing pane** — spec 021 T065, deferred.

## See also

- Threat model: [`doc/m3-security.md`](../m3-security.md)
- M1 / M2 quickstarts: [quickstart-m2.md](quickstart-m2.md) (M2 — in-browser format + analyse)
- M4 quickstart: [quickstart-m4.md](quickstart-m4.md) (the installer details)
- PRD: [M3-websocket-transport.md](M3-websocket-transport.md)
- Closure spec: [`specs/025-m3-bridge-closure/`](../../specs/025-m3-bridge-closure/)
