# Data Model: M4 — Installer (IIS Deployment Option) Closure

Six conceptual entities. Only one is a new persisted artefact (the PIN file); the rest are wizard state, parsed CLI flags, engine composition state, a generated text file, and test scaffolding. None introduces a new IndexedDB store, config schema, or IPC message type.

---

## E1 — WebEditionInstallChoice

The state the wizard collects across the web-edition pages and feeds to the PowerShell helpers.

| Field | Type | Values / Default | Source |
|-------|------|------------------|--------|
| `InstallWeb` | bool | from `[Components]` "web" tick | component page |
| `HostMode` | enum | `IIS` (default) \| `DontHost` | `WebHostPage` |
| `Exposure` | enum | `Localhost` (default) \| `Lan` | `WebNetworkPage` |
| `IisPort` | int | `80` default; `80` or `[1024..65535]` | `WebIisPortPage` (NEW) |
| `BridgePort` | int | `47291` default; `[1024..65535]` | `WebBridgePortPage` (NEW) |

**Invariants**:

- `IisPort != BridgePort` (FR-003) — enforced in `Web_NextButton` and the silent-flag validator.
- `BridgePort` in use → `Web_NextButton` shows a non-blocking warning (FR-003a); never hard-blocks; degrades to no-warning if the probe can't run.
- `HostMode == DontHost` ⇒ `web-iis-setup.ps1` is skipped; the bundle still lands at `{app}\Web\` and the service still installs (FR-020).
- `Exposure == Lan` ⇒ `web-tls-setup.ps1` + `web-firewall.ps1` run and the bridge binds `0.0.0.0`; `RequirePairingToken == true`.
- `HostMode == DontHost` with `Exposure == Lan` is invalid (FR-023) — there is no hosting endpoint to expose.

**Routing** (the port-split fix): `IisPort` → `web-iis-setup.ps1 -Port`; `BridgePort` → `web-config-bridge.ps1 -Port` + `web-tls-setup.ps1 -Port` + `web-firewall.ps1 -Port`.

---

## E2 — PairingPinFile

The on-disk PIN artefact (the only new persisted state).

| Field | Value |
|-------|-------|
| Path | `%CommonAppData%\AKML SQL Web\pairing-pin.txt` |
| Format | exactly the 6-digit decimal PIN, UTF-8, no trailing newline, no BOM |
| Writer | `PairingPinFile.Publish(string)` (NEW class), invoked from `EngineHost` on `PairingService.PinChanged` + a one-shot publish of `CurrentPin` after subscription |
| Write method | atomic temp + rename (`File.Replace` / `File.Move(overwrite:true)`), mirroring `ConfigManager.Save` |
| ACL | Administrators + SYSTEM read+write only (set by the installer when it creates the parent dir, before the service starts) |
| Reader | `Web_PostInstall` polls for appearance (30 s timeout), reads, bakes into `INSTALL-SUMMARY.txt` |
| Failure mode | write error is caught + Serilog-logged; engine startup never fails (FR-013); reader falls back to "not yet generated" text |

**Lifecycle**: minted in the `PairingService` constructor (initial) → written by the one-shot publish → overwritten on every `RegeneratePin()` → read by the installer post-install. Single-use consumption (`CurrentPin` returns empty after a PIN is consumed) does **not** blank the file; the file always reflects the last *minted* PIN so a re-read after the engine restarts shows a usable PIN.

---

## E3 — SilentInstallFlags

The parsed unattended-install CLI surface, read in `InitializeSetup`.

| Flag | Values | Maps to (E1) |
|------|--------|--------------|
| `/WEB_HOST` | `IIS` \| `NONE` | `HostMode` (`NONE` ⇒ `DontHost`) |
| `/WEB_EXPOSURE` | `LOCALHOST` \| `LAN` | `Exposure` |
| `/WEB_PORT` | `<int>` | `IisPort` |
| `/BRIDGE_PORT` | `<int>` | `BridgePort` |

**Validation rules** (both in `InitializeSetup`, both fail with a non-zero exit + a clear log line, before any state is created):

1. `/WEB_HOST=NONE` + `/WEB_EXPOSURE=LAN` → invalid ("LAN exposure requires a hosting mode (use /WEB_HOST=IIS)").
2. `/WEB_PORT == /BRIDGE_PORT` → invalid ("IIS port and Bridge port must differ").

**Behaviour**: present flags drive component selection + page values so `/VERYSILENT` completes unattended (FR-022); absent flags fall back to wizard defaults (E1). Sub-step failure ⇒ non-zero exit + rollback (FR-025).

---

## E4 — LanAuthComposition

The engine-side wiring that determines whether the handshake enforces the PIN. Not persisted — it's the composition decision made at `EngineHost` startup from `BridgeOptions`.

| Mode | Condition | `HandshakeHandler` registration | Services constructed |
|------|-----------|--------------------------------|----------------------|
| **LAN** | `BridgeOptions.IsLoopback == false` | full ctor: `pairingRequired: () => true`, `pinValidator`→`PairingService`, `bearerValidator`/`bearerMinter`→`BearerTokenStore`, identity provider | `PairingService`, `BearerTokenStore(bridge.TokenStorePath)` |
| **Loopback** | `IsLoopback == true` | parameterless ctor (auto-accept) — unchanged from today | none |
| **No bridge** | bridge section absent / disabled | not registered (named-pipe path only) | none |

**Invariants**:

- LAN mode: a wrong/expired/rate-limited PIN → `PinInvalid`, no bearer (FR-013c). A correct PIN → `Ok` + minted bearer, PIN consumed single-use (FR-013d). Stored bearer on reconnect → `Ok` without PIN; revoked → `PinRequired`.
- Loopback mode: no-PIN handshake → auto-accept (FR-013b).
- Both modes share one `RpcRouter` for all non-handshake handlers (no regression to spec 025's dual-transport composition).

---

## E5 — InstallSummary

The generated `%CommonAppData%\AKML SQL Web\INSTALL-SUMMARY.txt` (and the matching wizard success page). Existing format from spec 021 T093; this closure fills the PIN line (E2) and corrects the URL form (two-port).

| Line | Localhost mode | LAN mode |
|------|----------------|----------|
| `URL:` | `http://localhost[:IisPort]/` (port omitted when 80) | `http://<hostname>[:IisPort]/` (IIS bundle is HTTP; only the bridge is `wss`/TLS) |
| Pairing PIN | *(omitted — loopback needs no PIN)* | `Pairing PIN: <6-digit>` or the "not yet generated" fallback |
| TLS thumbprint | *(omitted)* | `TLS thumb: <SHA-1 hex>` + "How to trust" steps |
| Service status | warning line only if `AkmlSqlWebEngine` not `Running` within 10 s (FR-007a) | same |
| Don't-host note | host-it-yourself path + Python `http.server` example | same, when `HostMode == DontHost` |

---

## E6 — InstallerSmokeAssertion

One checkpoint asserted by `tests/AkmlSql.Installer.Tests` after a silent install. Not persisted — a test concept.

| ID | Assertion | Mode |
|----|-----------|------|
| a | `Get-Website -Name AkmlSqlWeb` bound on `IisPort` | IIS |
| b | MIME types `.wasm` `.dat` `.blat` `.br` `.dll` registered | IIS |
| c | `Content-Security-Policy` header present on `HEAD http://localhost:<IisPort>/` | IIS |
| d | `netsh http show sslcert ipport=0.0.0.0:<BridgePort>` thumbprint matches summary | LAN |
| e | firewall rule "AKML SQL Web Engine" exists on `BridgePort` | LAN |
| f | `INSTALL-SUMMARY.txt` non-empty, contains a `URL:` line | any |
| g | `Get-FileHash %AppData%\AKML SQL\config.json` identical before/after install + after uninstall | any (SC-007) |

**Gating**: every assertion runs only when `IsAdministrator() && IsIisInstalled()`; otherwise the test is skipped (not failed) via `Xunit.SkippableFact`.
