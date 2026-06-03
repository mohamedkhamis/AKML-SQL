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
| 5 | "Re-open the web edition → the bridge **auto-connects** … bearer authenticates the reconnect." | The web app **never auto-initiates** a connection on page load — `ConnectionPickerComponent.OnInitializedAsync` only lists connections; the only `Bridge.ConnectAsync` callers are the Pair/Connect buttons. The connection + (LAN) wrapped bearer persist, so reconnect is **one click of Connect, no PIN**. The exponential-backoff reconnect loop only retries an *already-established* connection that drops mid-session. | → reworded to "persists; click **Connect**, no PIN; mid-session drops auto-reconnect with backoff" |

> **Note for the owner (finding 5):** this is the M3 analog of the M6 orphaned-panel finding —
> documented behavior that isn't wired. Unlike M6 it's *not clearly a bug*: the feature works via
> Connect (no PIN) and the code reads as an intentional explicit-Connect design
> (`IEngineBridge.cs:364` deliberately surfaces Disconnected for manual retry on some closes). I
> corrected the doc rather than change behavior. **If you want true auto-connect-on-reopen**, it's
> a small add: in `ConnectionPickerComponent.OnInitializedAsync`, after `ReloadAsync`, if an active
> connection exists and the bridge is `Disconnected`, call `ConnectAsync(active)` (localhost
> auto-accepts; LAN uses the stored bearer). Say the word and I'll wire + verify it.

## Minor (not doc-fixed; flagged)

- **Connection-picker default Port is `5081`** (`ConnectionPickerComponent` `AddForm.Port`), but the
  engine bridge listens on **47291**. A user accepting the default would fail to connect; the
  quickstart correctly tells them to enter 47291. Consider defaulting the field to 47291.
- The engine's two TLS error strings still say `bridge.pfx` (`WebSocketTransport.cs:375,423-424`)
  though the surrounding code/comments correctly handle `.cer`. Cosmetic; left as-is.

## Not exercised here

- **LAN (Section 2)** — needs a second machine + self-signed-cert trust; audited statically only.
  The localhost path proves the handshake/version/auto-accept; the LAN delta is TLS + PIN, whose
  wire-level behavior is covered by `tests/AkmlSql.E2E.Tests/BridgeHandshakeTests`.
