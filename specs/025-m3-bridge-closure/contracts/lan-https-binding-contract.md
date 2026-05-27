# Contract: LAN HTTPS binding for WebSocketTransport

**Spec**: 025-m3-bridge-closure
**Consumers**: US1 (FR-001 / FR-002 / FR-003 / FR-005 / FR-006 / FR-027)
**Related**: spec 021 T056 / T057 / T058 / T087 / T088; Research Decision 1, 5, 6

## Engine-host composition (FR-027)

Before any of the LAN HTTPS work matters, `EngineHost.RunAsync` MUST actually start a `WebSocketTransport`. Today it only starts `NamedPipeTransport` (`EngineHost.cs:97`).

Required composition:

1. Load `config.json` via `ConfigManager.Load()` (already shipped).
2. If `config.Bridge != null && config.Bridge.Enabled`, construct `WebSocketTransportOptions` from the `Bridge` section per the schema in Research Decision 6.
3. Construct `new WebSocketTransport(options)`.
4. Wire `webSocketTransport.RequestReceived += async (msg, ct) => await composition.Router.RouteAsync(msg, composition.Context, ct);` — the same handler the named pipe uses.
5. Run both `transport.StartAsync(token)` and `webSocketTransport.StartAsync(token)` concurrently.
6. On shutdown, await both transports' `DisposeAsync` calls.

When `config.Bridge == null` or `Enabled=false`, the WebSocket transport is not constructed — the named-pipe-only behaviour is byte-for-byte identical to the IDE-plugin-only deployment.

Test (`tests/AkmlSql.Engine.Tests/EngineHostTests.cs`) MUST assert:

| Test | Asserts |
|------|---------|
| `DualTransportCompositionRoutesViaSameRouter` | With `Bridge.Enabled=true` in a temp config, both `NamedPipeTransport` and `WebSocketTransport` are started; a fake `Ping` over each transport reaches the same `RpcRouter` instance (verified via a counting handler). |
| `NoBridgeSectionStartsPipeOnly` | With `Bridge` absent from config, only `NamedPipeTransport` is constructed; no listener appears on port 47291. |
| `BridgeDisabledFlagStartsPipeOnly` | With `Bridge.Enabled=false` explicitly, behaviour matches the absent-section case. |

## Engine startup sequence (non-loopback binding)

When `WebSocketTransportOptions.BindAddress != "127.0.0.1" && BindAddress != "::1" && BindAddress != "localhost"`:

1. **Construction-time refusal** (already shipped; do not change): `WebSocketTransport`'s constructor throws `InvalidOperationException` if `TlsCertPath` is empty. Message (verbatim from current code):
   > WebSocketTransport: LAN-mode binding (BindAddress != loopback) requires TlsCertPath. Spec 021 FR-013a forbids plaintext WebSocket over LAN. Set TlsCertPath in config.json or bind to 127.0.0.1 for localhost-only mode.

2. **Startup-time PFX existence check** (US1 / FR-002): At the top of `StartAsync`, before any listener is opened:
   - If `File.Exists(TlsCertPath)` is false, throw `InvalidOperationException`:
     > WebSocketTransport: TlsCertPath does not exist on disk: '<path>'. Re-run AKMLSQLSetup.exe or check `%ProgramData%/AKML SQL Web/certs/bridge.pfx`.

3. **PFX thumbprint extraction**:
   - Load the PFX via `new X509Certificate2(TlsCertPath, password: <from TlsCertPasswordRef env var>)`.
   - Read `Thumbprint` (SHA-1, hex string, no separators).

4. **netsh binding thumbprint extraction**:
   - Shell out to `netsh http show sslcert ipport=0.0.0.0:<Port>` and parse the `Certificate Hash` line.
   - If the command returns non-zero or the parse finds no certificate hash, throw `InvalidOperationException`:
     > WebSocketTransport: No netsh http sslcert binding found for port <Port>. Run `web-tls-setup.ps1` or re-run AKMLSQLSetup.exe.

5. **Thumbprint match check** (US1 / FR-002 / Research Decision 5):
   - Case-insensitive string compare.
   - On mismatch, throw `InvalidOperationException`:
     > WebSocketTransport: PFX thumbprint mismatch with netsh binding. PFX (<TlsCertPath>) reports <pfx-thumb>; netsh binding for 0.0.0.0:<Port> reports <netsh-thumb>. Re-run `web-tls-setup.ps1` or update TlsCertPath.

6. **Prefix construction**: Use `https://<BindAddress>:<Port>/` (was `http://...` on the loopback path).

7. **HttpListener.Start()**: identical to the loopback path; the WinHTTP layer terminates TLS using the netsh-bound cert.

8. **AcceptWebSocketAsync**: identical to the loopback path; the existing `HandshakeHandler` runs after upgrade.

## Loopback binding (unchanged)

`WebSocketTransportOptions.BindAddress == "127.0.0.1" || "::1" || "localhost"`:

- `TlsCertPath` is ignored (per FR-003).
- Prefix stays `http://127.0.0.1:<Port>/`.
- No netsh check.
- This path MUST be byte-for-byte identical to the spec 021 T056 implementation.

## Error-message contract

Every refusal message MUST:
1. Start with `WebSocketTransport: `.
2. Name the offending file path or option (`TlsCertPath`, port number).
3. Name the relevant FR / spec reference (FR-013a, spec 021 T088).
4. End with the concrete operator action ("Re-run AKMLSQLSetup.exe", "bind to 127.0.0.1", "Run `web-tls-setup.ps1`").

## Fingerprint diagnostic (FR-005)

On every successful WSS handshake (`HandshakeStatus.Ok` response received), the browser bridge MUST:

1. Read the server's TLS cert thumbprint via the WebSocket platform API (in WASM, this is available through the JS-interop layer that already drives the bridge).
2. Compare to `EngineConnection.TlsFingerprint`.
3. On first connect (`TlsFingerprint` was null): set it to the observed value and persist via `IConnectionStore.UpdateAsync`.
4. On subsequent connect with mismatch: log a `DiagnosticLevel.Warn` entry to `IDiagnosticsRingBuffer`:
   > Bridge: TLS fingerprint for connection '<Name>' changed from <old-thumb-last-12> to <new-thumb-last-12>. This is expected after a cert regeneration on the engine host.

   The user-facing modal that lets the user re-trust or refuse is a deferred follow-up; the diagnostic log entry is the closure-spec deliverable.

## Tests (FR-006)

`tests/AkmlSql.Engine.Tests/Transports/WebSocketTransportTests.cs` MUST add (alongside the existing 5 tests):

| Test | Asserts |
|------|---------|
| `LanModeRoundTrip` | Creates a `WebSocketTransport` bound to `127.0.0.2:<picked>` with a unit-test self-signed PFX whose thumbprint has been bound via `netsh`. Issues a `Ping` over `wss://` and asserts a `Pong` response. |
| `LanModeRefusesWhenPfxMissing` | Constructs options with `BindAddress="0.0.0.0", TlsCertPath="C:/nonexistent.pfx"`. Asserts `StartAsync` throws with the FR-002 message. |
| `LanModeRefusesOnThumbprintMismatch` | Binds a different cert via netsh, points `TlsCertPath` at a PFX with a different thumbprint. Asserts `StartAsync` throws naming both thumbprints. |

The `netsh` setup + teardown for the LAN round-trip test MUST run as an `xUnit.IAsyncLifetime` collection fixture that requires elevated permissions. xUnit's `[Trait(...)]` is a filter-only mechanism, **not** a skip mechanism, so the test MUST do **both** of:

1. Carry `[Trait("Category","Elevated")]` so default `dotnet test` runs can exclude it via `--filter "Category!=Elevated"`.
2. Use `[SkippableFact]` from the `Xunit.SkippableFact` NuGet package (or an inline `if (!IsRunningElevated) { Skip.IfNot(false, "Requires elevation"); return; }` guard) so a developer who runs the trait anyway without elevation gets a green "skipped" result, not a red "errored" result.

`IsRunningElevated` is checked via `new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)`.

**Locale-dependency note**: `netsh http show sslcert ipport=...` output is localised by Windows display language — the `Certificate Hash` line label changes on non-English installs. The contract requires English-Windows for the CI / developer machine (matches the engine's `RuntimeIdentifier=win-x64` and the existing PowerShell-script assumption in `web-tls-setup.ps1`). A locale-agnostic alternative is the `System.Net.Http.HttpListenerConfiguration` P/Invoke (`HttpQueryServiceConfiguration`), but that adds an unmanaged-interop surface; the parse path is the simpler bet and matches every realistic deployment. Document this limitation in the contract; revisit if international support becomes a goal.
