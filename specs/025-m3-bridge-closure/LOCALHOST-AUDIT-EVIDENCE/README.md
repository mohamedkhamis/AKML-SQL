# M3 bridge — localhost run + quickstart accuracy audit (2026-06-03)

Same interactive treatment as the M6 pass: ran the real `AkmlSql.Engine` in web/bridge mode and
paired the browser with it (localhost), and cross-checked every concrete claim in
`doc/WEB/quickstart-m3.md` against the code. LAN (Section 2) audited statically — it needs two
machines.

## Localhost run (quickstart Section 1) — verified end-to-end

- **Engine launched** `AkmlSql.Engine.exe --web --config <localhost-bridge config>` (bridge
  `enabled`, `bindAddress 127.0.0.1`, `port 47291`, `tlsCertPath ""`). It logged
  `Application started` and **listened on `127.0.0.1:47291`** (`netstat` confirmed; the LISTENING
  socket is owned by http.sys/System, expected for `HttpListener`).
- **Browser paired** via Settings → Engine connections → Add (Host `127.0.0.1`, Port `47291`,
  Localhost ✓, **blank PIN**) → Pair. The status bar went **Connecting → Live**, showing
  *"Live IntelliSense available."* + **`Engine 1.26.0603.1657+00336ce…`** (screenshot
  `m3-bridge-localhost-live.png`). Localhost auto-accept (no PIN) works as documented.
- **Reconnect:** after a full reload the connection record **persists** (IndexedDB `connections`),
  and clicking **Connect** re-reaches **Live with no PIN prompt**. (See finding 5 on "auto".)

## Quickstart accuracy audit — findings (all fixed in the doc)

| # | Doc claim (before) | Reality (code) | Fix |
|---|---|---|---|
| 1 | IIS site defaults to `http://localhost:5081/` | Installer default IIS port is **80** (`web-installer.iss:174,196`) | → `http://localhost/` (and `dotnet run` serves `:5000`) |
| 2 | Bridge config in `%AppData%\AKML SQL\config.json` | Web service reads `%ProgramData%\AKML SQL Web\config.json` (`web-installer.iss:93` `--web --config …`); `%AppData%\AKML SQL\config.json` is the per-user IDE config the installer never touches (spec 026 C3) | → `%ProgramData%\AKML SQL Web\config.json` (Section 1 + Section 2 + troubleshooting) |
| 3 | `web-tls-setup.ps1` generates `bridge.pfx` | It exports `bridge.cer` (public); the private key is **NonExportable** in `LocalMachine\My`; TLS uses the netsh cert-store binding (`web-tls-setup.ps1:62,72-83`). The engine loads the `.cer` only to cross-check the thumbprint (`WebSocketTransport.cs:388-398`) | → `bridge.cer` + clarify the store/netsh binding |
| 4 | PIN has a **5-min** TTL | `PairingService.DefaultPinTtl = TimeSpan.FromHours(24)` | → **24-hour** TTL |
| 5 | "Re-open the web edition → the bridge **auto-connects** … bearer authenticates the reconnect." | At audit time the web app **never auto-initiated** a connection on page load — `ConnectionPickerComponent.OnInitializedAsync` only listed connections (and the picker lives only on `/settings`); the only `Bridge.ConnectAsync` callers were the Pair/Connect buttons. So re-open left the bridge **Offline** until a manual Connect. | → **WIRED** (owner-approved): added a layout-level startup hook `MainLayout.TryAutoConnectActiveAsync` that auto-connects the active connection on app start (localhost auto-accepts; LAN replays the wrapped bearer; no PIN). The doc now correctly states auto-connect. **Verified live** — fresh editor-landing load reaches `Live` with no Connect click (`m3-bridge-autoconnect-on-reopen.png`). |

> **Finding 5 — resolved (owner-approved, 2026-06-03):** rather than document around it (the M3
> analog of the M6 orphaned-panel finding), the owner asked to wire it. Auto-connect-on-startup is
> now implemented at the layout level (`MainLayout.TryAutoConnectActiveAsync`, fire-and-forget so a
> slow/unreachable engine never blocks first paint) and verified live. The `IEngineBridge` backoff
> reconnect loop (for mid-session drops) is unchanged.

## Minor

- **Connection-picker default Port** — **FIXED** (owner-approved): `AddForm.Port` and the
  `EngineConnection.Port` model default were `5081` (matched nothing the engine listens on); both
  now default to **47291** (the bridge default). Verified live: the Add dialog pre-fills `47291`.
- The engine's two TLS error strings still say `bridge.pfx` (`WebSocketTransport.cs:375,423-424`)
  though the surrounding code/comments correctly handle `.cer`. Cosmetic; left as-is (engine code,
  out of this doc-audit's scope).

## Not exercised here

- **LAN (Section 2)** — needs a second machine + self-signed-cert trust; audited statically only.
  The localhost path proves the handshake/version/auto-accept; the LAN delta is TLS + PIN, whose
  wire-level behavior is covered by `tests/AkmlSql.E2E.Tests/BridgeHandshakeTests`.
