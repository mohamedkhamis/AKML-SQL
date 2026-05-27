# Contract: Bridge E2E test harness

**Spec**: 025-m3-bridge-closure
**Consumers**: US5 (FR-023 / FR-024 / FR-025 / FR-026)
**Related**: spec 021 T078 + T079 deferred; spec 024's `DotnetRunFixture`; Research Decision 4; data-model.md E5

## Fixture lifecycle

`EngineLaunchFixture : IAsyncLifetime` (under `tests/AkmlSql.Web.E2E.Tests/Harness/EngineLaunchFixture.cs`) MUST follow this state machine:

```
NotStarted
   │
   │ InitializeAsync called
   ▼
Building
   │
   │ dotnet build src/AkmlSql.Engine -c Release
   ├──── exit 0 ─────────────────────┐
   │                                  ▼
   │                                Launching
   │                                  │
   │                                  │ pick free port, spawn AkmlSql.Engine.exe
   │                                  ├──── ws:// accept inside 30s ────► Ready
   │                                  │
   │                                  └──── 30s timeout ────────────────► LaunchTimedOut (throw)
   │
   └──── exit ≠ 0 ─────────────────► BuildFailed (throw, include BuildOutput)


Ready (tests run)
   │
   │ DisposeAsync called
   ▼
TornDown
```

Concrete sequence inside `InitializeAsync`:

1. Set `State = Building`.
2. Run `dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release` with stdout+stderr captured.
   - On non-zero exit, set `State = BuildFailed`, store output in `BuildOutput`, throw `InvalidOperationException` with the captured output appended.
3. Pick a free port: `var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();`. Persist as `Port`.
4. Set `State = Launching`.
5. Spawn `bin/Release/net10.0/win-x64/AkmlSql.Engine.exe --bridge-port=<port> --bridge-mode=localhost`. Capture process handle as `EngineProcess`.
6. Poll a `TcpClient.ConnectAsync("127.0.0.1", port)` every 250 ms; on first success, set `State = Ready` and `LaunchedAt = DateTimeOffset.UtcNow`.
7. If 30 s elapse without success, set `State = LaunchTimedOut` and throw.

Concrete sequence inside `DisposeAsync`:

1. If `EngineProcess != null && !EngineProcess.HasExited`: `EngineProcess.Kill(entireProcessTree: true)`.
2. `EngineProcess?.WaitForExitAsync` with a 5 s budget.
3. Set `State = TornDown`.

## Opt-in convention (FR-026)

Every test class that consumes this fixture MUST carry `[Trait("Category","BridgeE2E")]` at the class level. Both new suites comply:

```csharp
[Trait("Category", "BridgeE2E")]
public sealed class UserStory2Tests : IClassFixture<EngineLaunchFixture> { ... }

[Trait("Category", "BridgeE2E")]
public sealed class BridgeHandshakeTests : IClassFixture<EngineLaunchFixture> { ... }
```

Default `dotnet test` runs MUST NOT execute these classes. Developers opt in via:

```
dotnet test tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj --filter Category=BridgeE2E
dotnet test tests/AkmlSql.E2E.Tests/AkmlSql.E2E.Tests.csproj --filter Category=BridgeE2E
```

## `UserStory2Tests` (FR-023)

Drives the four spec-021 US2 acceptance scenarios via Playwright Chromium + the shared fixture. Test methods:

| Method | Scenario | Asserts |
|--------|----------|---------|
| `LocalhostPair_FirstConnect_ReachesOpen` | Spec 021 US2 Scenario 1 (analogue): browser does Add Connection with the fixture's port + `IsLocalhost=true`; assert the status bar reaches `Open` and a sample completion fires. |
| `LocalhostPair_Reload_PreservesBearer` | Spec 021 US2 Scenario 2: close the browser tab, re-open; assert the bridge reaches `Open` without a PIN prompt. |
| `RevocationFails_RetryRespectsPinRequired` | Spec 021 US2 Scenario 3: while paired, the test invokes `BearerTokenStore.RevokeAll` via the engine's tray API (a temp test-only endpoint or direct dll call); assert the next bridge action receives `PinRequired` and the browser surfaces the re-pair UI. |
| `EngineKill_ReconnectRestoresLive` | Spec 021 US2 Scenario 4: while paired, kill the engine process; verify the bridge transitions to `Reconnecting`; restart the engine via the fixture's `RelaunchAsync` helper; assert `Open` returns within 10 s and live completions resume. |

The Playwright `IPage` is provided by the existing `tests/AkmlSql.Web.E2E.Tests/Harness/` setup (per spec 024). `WebStartupFixture` (the Blazor host) is composed with `EngineLaunchFixture` via xUnit `IClassFixture<>` — both run in the same test class scope.

## `BridgeHandshakeTests` (FR-024)

Same engine fixture; no browser. Pure xUnit + the production `EngineBridge` (the WebAssembly entry-point is not exercised here — only the .NET 10 side):

| Method | Asserts |
|--------|---------|
| `LocalhostHandshake_ReturnsOkAndCapabilities` | `EngineBridge.ConnectAsync` against fixture port returns `HandshakeStatus.Ok` and a non-empty capability list. |
| `BearerReplay_OnSecondConnect_Succeeds` | Connect; capture minted bearer; disconnect; reconnect with the same bearer; assert `Ok`. |
| `RevokedBearer_OnReconnect_ReturnsPinRequired` | Connect; revoke via `BearerTokenStore.RevokeAll`; reconnect; assert `HandshakeStatus.PinRequired`. |
| `EngineRestart_ReconnectSucceedsWithStoredBearer` | Connect; call `fixture.RelaunchAsync`; assert the reconnect succeeds within 10 s of engine readiness. |
| `BackoffRespectsContract` | Drive a forced socket-close scenario; collect emitted retry intervals; assert they match the schedule from `backoff-schedule-contract.md` (allowing ±100 ms jitter). |

## Build hygiene (FR-025)

`EngineLaunchFixture` MUST always run `dotnet build` (not `--no-build`). A green test run is meaningless if it could be running against a stale engine binary; the few seconds the build costs are the price of trustworthy E2E.

For Playwright tests that also need the web bundle built, the existing `DotnetRunFixture` (spec 024) handles the web side; `EngineLaunchFixture` handles the engine side; both are composed via `IClassFixture<>` and run independently — no shared mutable state between them.

## Cleanup invariants

After every test run (pass or fail):

- The engine child process MUST be killed (Disposer guarantees this).
- The picked port MUST be released (the process kill releases the listening socket).
- No test artefacts on disk other than `bin/Release/`'s normal build output.
- No persistent IndexedDB state for `BridgeHandshakeTests` (pure RPC; no browser context).
- `UserStory2Tests` runs in a fresh Chromium context per test method — no IndexedDB carryover between methods.
