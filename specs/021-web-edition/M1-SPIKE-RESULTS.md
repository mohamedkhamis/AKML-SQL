# M1 Spike Results — WASM Viability + Blazor Scaffold

**Spec**: 021-web-edition
**Tasks**: T027 (spike), T028 (Program.cs bootstrap), T029 (layout components), T030 (this document)
**Date**: 2026-05-16
**Status**: M0/M1 scaffold landed; runtime spike (T027 in-browser execution) deferred — see "Open follow-ups".

---

## 1. Compile-time viability — confirmed

`AkmlSql.Web` (.NET 10 Blazor WASM standalone) builds clean against the project tree, with project references to:

- `AkmlSql.Core` (netstandard2.0)
- `AkmlSql.Formatting` (netstandard2.0)
- `AkmlSql.Web.Shared` (netstandard2.0)

Build output: `src/AkmlSql.Web/bin/Release/net10.0/wwwroot/` contains a working WASM bundle.

```text
0 Warning(s)
0 Error(s)
Time Elapsed 00:00:38
```

`ScriptDom`, `MessagePack`, `System.Text.Json`, `Serilog` (with in-memory sink only) all participate in the link without WASM-specific errors.

## 2. Bundle size — measured

**Uncompressed `_framework/` total: 45 MB.** Top contributors:

| Component | Size | Why it lands here |
|-----------|------|-------------------|
| `Microsoft.SqlServer.TransactSql.ScriptDom` | 6.1 MB | Required for the formatter pipeline (parses every T-SQL document). Not removable. |
| `System.Private.CoreLib` | 4.7 MB | .NET 10 BCL. Trims further with AOT/IL trimming. |
| `System.Private.Xml` | 3.0 MB | Used by `.akmlstyle` / `.sqlpromptstylev2` round-trip parsing. Required. |
| `dotnet.native.wasm` | 2.9 MB | Mono WASM runtime. Fixed cost. |
| `System.Data.Common` | 988 KB | Transitive (column metadata types). |
| `System.Private.DataContractSerialization` | 832 KB | XML serializer for spec-020 profile files. |

With Brotli compression (the default for production publishes) the wire size will be ≈ 10–15 MB. That fits comfortably inside the SC-001 "5 minutes install-to-edit" budget on a typical broadband connection but justifies an explicit `BlazorWebAssemblyLazyLoad` pass during M2 (T054 audit) for analysis rules and rarely-used UI surfaces.

## 3. Architectural finding — `AkmlSql.Analyzer` is NOT the analysis library

The plan's `Phase 1 T001` originally referenced `AkmlSql.Analyzer` from `AkmlSql.Web`. **That was wrong.** `AkmlSql.Analyzer` is the **CLI executable** (`<OutputType>Exe</OutputType>`) that references `AkmlSql.Engine` transitively, dragging in 37 MB of unrelated assemblies — OpenAI, ClosedXML, OpenXml, SqlClient, Mscc.GenerativeAI — none of which can or should run in a browser.

The actual analysis logic lives inside `AkmlSql.Engine/Analysis/`. **This needs a separate library extraction**, parallel to the M5 `AkmlSql.IntelliSense` and M6 `AkmlSql.AI` extractions already in the plan.

**Resolution applied at T030**:

1. Removed the `<ProjectReference Include="..\AkmlSql.Analyzer\..." />` from `AkmlSql.Web.csproj`.
2. Bundle shrank from 82 MB to 45 MB.
3. **The M2 analyser wiring (T043, T045, T047) will not work until the analysis library is extracted into a standalone `netstandard2.0` project.** This is a new follow-up task — see § 6.

## 4. M2.1 editor recommendation — DEFERRED

The plan's T027 spike was supposed to compare Monaco vs CodeMirror 6 in-browser. That requires:

1. A running WASM bundle in a real browser.
2. A 10 KLOC SQL test file pasted into each editor.
3. Side-by-side measurements of cold load, typing latency, and syntax-highlight rendering.

This session landed the **scaffolding** (T028 Program.cs + DI, T029 layout components) but did not boot the WASM bundle in a browser. The editor decision must come from a future task that actually runs the spike — recommend folding T027 + T031 (the M2.1 spike task) into a single dedicated browser session.

**Provisional default**: **CodeMirror 6**. Reasoning:

- 4× smaller bundle (~500 KB vs ~2 MB Monaco).
- `@codemirror/lang-sql` is a first-class SQL grammar (Monaco's SQL is heuristic).
- The 45 MB starting bundle pushes us toward saving every kilobyte we can.

If the M2.1 spike measures CodeMirror's API ergonomics as too lossy for AKML SQL's interaction needs, Monaco remains a clean swap behind `EditorComponent.razor`.

## 5. Performance baseline — DEFERRED

T006 (engine perf baseline capture) and T025 (post-refactor regression check) require running the existing `PipeRpcServer` against a real corpus. That belongs in the M0 handler-migration session, not the M0 abstraction-skeleton session. Marking T006 as **not started** in `tasks.md`.

## 6. Open follow-ups (not blocking M2 scaffold work, but blocking M2 wire-up)

| # | Item | Why blocking |
|---|------|--------------|
| **F1** | **Extract `AkmlSql.Analysis` library** (analogous to M5 `AkmlSql.IntelliSense` extraction). Move analyser engine, `RuleRegistry`, `AnalysisContext`, `CaSettingsLoader`, and all 120+ rule classes from `AkmlSql.Engine/Analysis/` into a new `netstandard2.0` library `AkmlSql.Analysis`. Have `AkmlSql.Engine` reference it; have `AkmlSql.Web` reference it. | T043 / T045 / T047 (M2 analyser surface) cannot ship until this is done. |
| F2 | Run the actual in-browser M2.1 editor spike (Monaco vs CodeMirror 6) | T031 / T032 (Editor choice + EditorComponent) gates on it. |
| F3 | Run T006 perf baseline against the existing `PipeRpcServer` corpus | T011 onwards (handler migration) gates on it. |
| F4 | Decide bundle-size budget for the M2 ship | T054 audit needs a concrete target. |
| F5 | `e_sqlite3.a` NativeFileReference warning | Cosmetic for now; ensure no code path in `AkmlSql.Web` triggers a SQLite call (it shouldn't — `AkmlSql.Core` doesn't expose SQLite directly). |

## 7. M0 transport abstraction — landed, no perf regression risk

The M0 surface (T007–T010) is in place and exercised end-to-end by 5 passing unit tests in `tests/AkmlSql.Engine.Tests/Transports/InProcessRoundTripTests.cs`:

- `Round_trip_through_InProcessTransport_invokes_handler_and_returns_response`
- `Unregistered_MessageType_returns_null_response`
- `Notification_handler_runs_but_returns_no_response` (`ResponseMessageType = 0`)
- `Duplicate_registration_throws`
- `SendAsync_before_StartAsync_throws`

**No handler migration was performed.** `PipeRpcServer` still owns the existing 53-case switch dispatch. The new types live alongside, unused by production code. This matches M0.1 from `doc/WEB/M0-dispatcher-transport.md`: "Add `IRpcTransport`, `IRpcRequestHandler<,>`, `RpcRouter`, `RpcContext`. No handler moved yet. `PipeRpcServer` continues to work via its old switch; the new types live alongside, unused."

Per the M0 success metric ("zero behavioural change to the SSMS and VS shell extensions") — confirmed: no file under `src/AkmlSql.Shell.Shared/` was touched, all shell tests remain green, frame format unchanged.

## 8. M1 scaffold deliverables

- `src/AkmlSql.Web/Program.cs` — `WebAssemblyHostBuilder` bootstrap with DI registrations for `IFormatterService`, `IAnalyserService`, `IDiagnosticsRingBuffer`, `IThemeService` (all stub implementations).
- `src/AkmlSql.Web/Services/` — four interface/stub pairs for the M2 surfaces.
- `src/AkmlSql.Web/App.razor` — `Router` wired with `MainLayout` as default and not-found fallback.
- `src/AkmlSql.Web/Shared/MainLayout.razor` — grid layout consuming theme tokens via CSS variables.
- `src/AkmlSql.Web/Shared/NavMenu.razor` — top navigation with brand + tagline + nav links (Settings / Diagnostics disabled until T034 / T049).
- `src/AkmlSql.Web/Shared/StatusBar.razor` — footer status bar (current state: "Skeleton" pill).

## 9. Definition of done — this document

- [x] AkmlSql.Web builds clean against the WASM target.
- [x] Bundle size measured and recorded (45 MB uncompressed before lazy-loading).
- [x] M0 transport abstraction exercised by ≥ 4 unit tests.
- [x] Architectural finding (Analyzer CLI vs analysis library) recorded with a concrete follow-up (F1).
- [ ] In-browser cold-load timing for the WASM bundle — **deferred (F2)**.
- [ ] M2.1 editor decision based on measurement — **deferred (F2)**.
- [ ] Engine perf baseline captured — **deferred (F3)**.
