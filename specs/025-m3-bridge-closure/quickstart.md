# Quickstart: M3 — WebSocket Transport & Local-Agent Bridge Closure

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Research**: [research.md](./research.md)
**Branch**: `025-m3-bridge-closure` · **Date**: 2026-05-27

This document walks a developer through implementing each of the five user stories. Estimated total effort: 4–6 days of focused work (the bulk of M3 is already merged in spec 021 Phase 4).

---

## US1 — LAN HTTPS plumbing + engine-host composition (P1, ~1 day)

**Goal**: `WebSocketTransport` serves `wss://` on non-loopback bindings using the installer-produced netsh-bound cert, **and** the engine actually starts a `WebSocketTransport` when the bridge is enabled in config.

**Steps**:

1. **Wire the engine-host composition first** (FR-027). Open `src/AkmlSql.Engine/EngineHost.cs`. Around line 96 (where `NamedPipeTransport` is constructed), read the `Bridge` section from config:

   ```csharp
   var config = ConfigManager.Load();
   var composition = EngineComposition.Build();
   await using var pipeTransport = new NamedPipeTransport(pipeName);
   pipeTransport.RequestReceived += async (msg, ct) => await composition.Router.RouteAsync(msg, composition.Context, ct);

   WebSocketTransport? wsTransport = null;
   if (config.Bridge?.Enabled == true)
   {
       var wsOptions = new WebSocketTransportOptions
       {
           BindAddress = config.Bridge.BindAddress,
           Port = config.Bridge.Port,
           TlsCertPath = config.Bridge.TlsCertPath,
           TlsCertPasswordRef = config.Bridge.TlsCertPasswordRef,
           TokenStorePath = config.Bridge.TokenStorePath,
           TokenTtl = TimeSpan.FromDays(config.Bridge.TokenTtlDays),
           RequirePairingToken = !wsOptions.IsLoopback,
       };
       wsTransport = new WebSocketTransport(wsOptions);
       wsTransport.RequestReceived += async (msg, ct) => await composition.Router.RouteAsync(msg, composition.Context, ct);
   }

   await Task.WhenAll(
       pipeTransport.StartAsync(token),
       wsTransport?.StartAsync(token) ?? Task.CompletedTask);
   ```

   Add `Bridge` (a new POCO) to `AppSettings`; default to `null` so existing IDE-plugin-only configs stay untouched.

2. **Write `tests/AkmlSql.Engine.Tests/EngineHostTests.cs`** with the three tests from `contracts/lan-https-binding-contract.md` §"Engine-host composition" — `DualTransportCompositionRoutesViaSameRouter`, `NoBridgeSectionStartsPipeOnly`, `BridgeDisabledFlagStartsPipeOnly`.

3. **Open `src/AkmlSql.Engine/Transports/WebSocketTransport.cs`.** Locate `StartAsync` and change the prefix construction:

   ```csharp
   var host = _options.IsLoopback ? "127.0.0.1" : _options.BindAddress;
   var scheme = _options.IsLoopback ? "http" : "https";
   var prefix = $"{scheme}://{host}:{_options.Port}/";
   ```

4. **Add the PFX + netsh thumbprint match check.** Before `_listener.Start()`, when `!_options.IsLoopback`:

   ```csharp
   ValidateCertBindingOrThrow(_options.TlsCertPath, _options.Port);
   ```

   Implement `ValidateCertBindingOrThrow` per `contracts/lan-https-binding-contract.md` §"Engine startup sequence": existence check → PFX thumbprint extract → `netsh http show sslcert` parse → case-insensitive compare → throw `InvalidOperationException` on mismatch with both thumbprints named.

5. **Browser side: derive scheme from `IsLocalhost`** in `src/AkmlSql.Web/Services/IEngineBridge.cs` (`ConnectAsync`):

   ```csharp
   var url = (connection.IsLocalhost ? "ws://" : "wss://") + connection.Host + ":" + connection.Port + "/akmlsql";
   ```

   This is already the shape in the existing code — verify and keep.

6. **Fingerprint diagnostic** (FR-005): in `EngineBridge` after a successful handshake, compare the observed cert thumbprint (from the JS-interop side) against `EngineConnection.TlsFingerprint`. On first connect, persist via `IConnectionStore.UpdateAsync`. On mismatch, log a `DiagnosticLevel.Warn` entry per the contract. **Do not** show a modal — that's deferred follow-up #1.

7. **Tests**: extend `tests/AkmlSql.Engine.Tests/Transports/WebSocketTransportTests.cs` with the three tests from the contract — `LanModeRoundTrip` (elevated, uses `[SkippableFact]` from `Xunit.SkippableFact` so unprivileged developer runs get a green skip), `LanModeRefusesWhenPfxMissing`, `LanModeRefusesOnThumbprintMismatch`. Tag with `[Trait("Category","Elevated")]` so CI runs can exclude via `--filter "Category!=Elevated"`.

8. **Manual smoke test**: install via `AKMLSQLSetup.exe /WEB_EXPOSURE=LAN /WEB_PORT=47291` on a clean VM; start the engine; from a second machine on the LAN, open the web edition and pair.

**DoD**: M3 PRD §12 checkbox 2 closed; FR-027 dual-transport composition lands.

---

## US2 — Threat model + firewall + quickstart-m3 docs (P1, ~½ day)

**Goal**: `doc/m3-security.md` and `doc/WEB/quickstart-m3.md` exist and pass review.

**Steps**:

1. **Write `doc/m3-security.md`** following the entity shape in `data-model.md` E3:
   - Header + "Last reviewed" date + cross-link to PRD `doc/WEB/M3-websocket-transport.md`.
   - **Threat model** table with the 8 rows (6 from PRD §8 + 2 added).
   - **On-disk artefacts** section listing every path the engine actually writes to (with ACL notes):
     - `%CommonAppData%/AKML SQL Web/tokens.json` (hashed bearer tokens; engine-user only)
     - `%CommonAppData%/AKML SQL Web/pairing-pin.txt` (current PIN; engine-user only; rotated every 24 h)
     - `%ProgramData%/AKML SQL Web/certs/bridge.pfx` (self-signed cert; non-exportable private key; engine-user only)
     - `%AppData%/AKML SQL Web/config.json` (engine config; engine-user only)
   - **Plaintext-on-LAN refusal** section quoting the `WebSocketTransport` construction-time error message verbatim, with a note that the installer never produces this configuration but a hand-edited `config.json` would.
   - **What is NOT covered** section listing the three deferred follow-ups from spec.md §"Out of Scope" so an auditor sees the explicit gaps.

2. **Write `doc/WEB/quickstart-m3.md`** matching the format of existing `quickstart-m2.md` / `quickstart-m4.md`:
   - **Section 1: One-machine demo** (localhost mode) — install with `AKMLSQLSetup.exe /WEB_EXPOSURE=LOCALHOST`; open `http://localhost:<port>/`; verify the editor loads. (≈ 5 minutes wall-clock.)
   - **Section 2: LAN pair from a second machine** — the 5 numbered steps in `data-model.md` E4. Each step has a Verification subsection. (≈ 5 minutes after install.)
   - **Section 3: Troubleshooting** — firewall blocked, wrong PIN, wrong port, stale netsh binding, missing PFX. Each entry has the symptom + the operator action.

3. **Cross-link from the WEB index**: edit `doc/WEB/00-INDEX.md` to add the quickstart-m3 link below quickstart-m2.

4. **Cross-link from `doc/architecture.md` §9d** (the existing bridge section): add a "Threat model: see [m3-security.md](m3-security.md)" pointer at the end of the section.

**DoD**: M3 PRD §12 checkboxes 6 and 7 closed.

---

## US3 — Exponential-backoff reconnect (P2, ~1 day)

**Goal**: `BridgeState.Reconnecting` is reachable; the receive loop retries with exponential back-off; bearer token replays on retry; revocation halts the loop.

**Steps**:

1. **Add `BackoffSchedule`** as a private class in `src/AkmlSql.Web/Services/IEngineBridge.cs` per `data-model.md` E1. Make it `internal` so `ReconnectLoopTests` can construct it directly with a deterministic jitter source.

2. **Rewrite the `EngineBridge.ReceiveLoopAsync` `finally` block**: instead of unconditionally setting `State = Disconnected`, check whether the disconnect was user-initiated (a `_userDisconnectRequested` flag set by `DisconnectAsync`). If not, set `State = Reconnecting` and schedule a retry via `Task.Run`.

3. **Add `ReconnectLoopAsync`** as a private method that:
   - Reads from `BackoffSchedule.NextDelay()`.
   - Sleeps via `PeriodicTimer` or `Task.Delay`.
   - Updates the status-bar countdown via the `StateChanged` event (extend the event to carry the next-retry timestamp).
   - On wake, calls `ConnectAsync` against the current connection with the stored bearer.
   - On `HandshakeStatus.Ok`: transition to `Open`, reset `BackoffSchedule`.
   - On `HandshakeStatus.PinRequired`: transition to `Failed`, call `IPairingTokenVault.RemoveAsync` + `IConnectionStore.UpdateAsync` to clear the wrapped ref, stop retrying.
   - On any other error: stay in `Reconnecting`, schedule next retry.

4. **Extend `StatusBar.razor`**: add the retry-info text when `State == Reconnecting` per the contract format ("Reconnecting · next try in 4s" / "Reconnecting · trying now…"). Read the countdown from the bridge.

5. **Tests**: write `tests/AkmlSql.Web.Tests/Bridge/ReconnectLoopTests.cs` with the 7 cases from `contracts/backoff-schedule-contract.md` §Tests. Use `FakeBridgeWebSocket` (already shipped) for all cases.

6. **Manual smoke test**: pair the browser, type SQL (completions arrive); kill the engine; observe status bar shows `Reconnecting · next try in 1s` then `· trying now…`; restart the engine within 10 s; verify the bar returns to `Open` without a re-pair prompt.

**DoD**: PRD §5 "Auto-reconnect on transient drops: Yes" is now actually true; PRD §9 SC-002 (reconnect within 10 s) measurable.

---

## US4 — Schema object tree (P2, ~1½ days)

**Goal**: `SchemaTreeComponent.razor` renders the cached Phase A/B snapshots; clicking inserts the qualifier.

**Steps**:

1. **Build `src/AkmlSql.Web/Shared/SchemaTreeComponent.razor`** per `contracts/schema-tree-contract.md`:
   - `@inject` `ISchemaCacheStore`, `IEngineBridge`, `ISchemaSync`.
   - Build the node tree from `SchemaSnapshot.PhaseA` + `PhaseB` (use the same JSON shape `ISchemaSync.FetchPhaseAAsync` persists).
   - Render Database → Schema → Object-Kind → Object → Column per the hierarchy.
   - Maintain a `HashSet<string>` of expanded paths in component state.
   - Subscribe to `IEngineBridge.StateChanged` (for the stale badge) and `ISchemaSync.ChecksumDrifted` (for refresh).
   - Use `<Virtualize>` past 200 children.
   - Raise an `EventCallback<string>` parameter `OnObjectClicked` carrying the bracketed qualifier.

2. **Mount on `Editor.razor`**: place `<SchemaTreeComponent OnObjectClicked="HandleObjectInsert" />` in the editor's right sidebar (or a collapsible side panel — match the existing layout). `HandleObjectInsert` calls the existing CodeMirror JS interop `akmlEditor.insertAtCaret(text)`.

3. **Theming**: use only `--akml-*` CSS custom properties from `wwwroot/css/themes/` per the contract. Reuse inline-SVG icons from `Shared/Icons/`.

4. **Tests**: write `tests/AkmlSql.Web.Tests/Bridge/SchemaTreeComponentTests.cs` with the 8 bUnit cases from the contract. Seed `ISchemaCacheStore` via the existing in-memory adapter used by spec 021 tests.

5. **Manual smoke test**: connect to a test SQL Server (sample db with a dozen tables); observe the tree renders Database → dbo → Tables → ... within a couple seconds; expand a table; observe columns; click — qualifier appears at caret in the editor.

**DoD**: M3 PRD §12 checkbox 3 fully closed ("renders tree").

---

## US5 — E2E coverage on the wire (P3, ~1½ days)

**Goal**: `dotnet test --filter Category=BridgeE2E` runs both new suites against a real engine and reports pass.

**Steps**:

1. **Write `tests/AkmlSql.Web.E2E.Tests/Harness/EngineLaunchFixture.cs`** per `contracts/bridge-e2e-harness-contract.md` §Fixture lifecycle. Implement `IAsyncLifetime.InitializeAsync` and `DisposeAsync`. Expose `Port`, `EngineProcess`, `LaunchedAt`, plus a `RelaunchAsync()` helper for the engine-restart scenario.

2. **Write `tests/AkmlSql.E2E.Tests/BridgeHandshakeTests.cs`** with the 5 methods from the contract. Use `IClassFixture<EngineLaunchFixture>` and the production `EngineBridge` over `JsBridgeWebSocket` (or its server-side equivalent — a thin wrapper around `ClientWebSocket` since this is a .NET-10 test, not WASM).

3. **Write `tests/AkmlSql.Web.E2E.Tests/UserStory2Tests.cs`** with the 4 methods from the contract. Compose `IClassFixture<EngineLaunchFixture>` with the existing `IClassFixture<WebStartupFixture>` from spec 024.

4. **Run**:

   ```
   dotnet test tests/AkmlSql.E2E.Tests/AkmlSql.E2E.Tests.csproj --filter Category=BridgeE2E
   dotnet test tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj --filter Category=BridgeE2E
   ```

   Both should report green.

5. **Confirm the default `dotnet test` run still skips them**:

   ```
   dotnet test --logger "console;verbosity=normal"
   ```

   No `BridgeE2E`-labelled tests should appear in the output.

**DoD**: M3 PRD §12 checkbox 5 closed (E2E coverage on the wire).

---

## Wrap-up

After all five stories land:

1. **Run the full test suite** to confirm no regressions:

   ```
   dotnet test
   dotnet test --filter Category=BridgeE2E
   ```

2. **Update `doc/progress.md`** with a spec 025 closure summary (the rolling development log).

3. **Mark spec 021 T058, T068 (reconnect note), T078, T079** as `[X]` with completion notes citing this spec's FRs and contract docs.

4. **Verify M3 PRD §12 DoD** — every checkbox should map to either a merged-by-021 feature or a spec-025 FR. The remaining open follow-ups (TLS fingerprint dialog, engine tray UI, in-flight revocation) are explicitly out of scope and named in `spec.md`'s "Out of Scope" section.

5. **Ready to merge**: branch is single-commit + PR; reviewer reads the spec, plan, and the four contract docs in order; landed feature work is visible in the diff.
