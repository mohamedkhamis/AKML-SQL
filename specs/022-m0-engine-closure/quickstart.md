# Quickstart — Verifying the M0 Engine Transport Closure

Use this guide when picking up the closure work, when reviewing a closure PR, or after a long enough gap that the spec's intent has faded. It walks through every claim the closure makes and how to confirm it from a clean clone.

## Prerequisites

- A clean checkout of the repository on the `022-m0-engine-closure` branch (or a branch that has merged it).
- Visual Studio 2022 Enterprise installed at `C:\Program Files\Microsoft Visual Studio\2022\Enterprise\` (for the shell-extension builds). Build commands assume this path.
- .NET 10 SDK on the `PATH`.
- Inno Setup 7 installed at `C:\Program Files\Inno Setup 7\` (only needed if you also build the installer).
- The `AKML-SQL.slnx` solution open in the IDE OR closed — both work.

## 1 — Establish a clean baseline

Before believing any post-closure measurement, confirm the test suite is green at the current branch tip.

```bash
dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release
dotnet test  tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Release
```

Expected: build succeeds with `0 Error(s)`; test run finishes with `Passed!` and a non-zero count. Note any pre-existing skipped tests so they are not blamed on later steps.

## 2 — Verify Gap 1: settings cache has one owner

```bash
grep -rn "_cachedSettings" src/AkmlSql.Engine/
```

Expected: exactly one match — the private field declaration inside `src/AkmlSql.Engine/RpcContext.cs`. Any other match (especially `src/AkmlSql.Engine/Server/PipeRpcServer.cs:35` or `…Server/PipeRpcServer.Handlers.cs`) means P1 is incomplete.

```bash
grep -rn "ConfigManager.Load" src/AkmlSql.Engine/Handlers/
```

Expected: zero matches. Handlers read settings via `ctx.EnsureSettings()`; only the composition root wires `ConfigManager.Load` as the `SettingsLoader`.

Unit-test confirmation:

```bash
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj \
    --filter "RpcContextTests" -c Release
```

Expected: tests `EnsureSettings_loads_once_and_caches` and `InvalidateSettings_forces_reload_on_next_call` pass.

## 3 — Verify Gap 2: named-pipe transport file is renamed and ≤ 150 LOC

```bash
ls src/AkmlSql.Engine/Transports/
ls src/AkmlSql.Engine/Server/
```

Expected: `Transports/NamedPipeTransport.cs` exists; `Server/PipeRpcServer.cs` and `Server/PipeRpcServer.Handlers.cs` do NOT.

```bash
wc -l src/AkmlSql.Engine/Transports/NamedPipeTransport.cs
```

Expected: ≤ 150 (the PRD's success metric).

```bash
ls src/AkmlSql.Engine/EngineComposition.cs src/AkmlSql.Engine/EngineHandlerRegistry.cs
```

Expected: both files exist.

Source search:

```bash
grep -rn "PipeRpcServer" src/ tests/
```

Expected: zero `class PipeRpcServer` declarations remain; doc-comment references are OK if any survived the rename — the bytes-on-the-wire test is what matters next.

Round-trip test:

```bash
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj \
    --filter "PipeRoundTripTests" -c Release
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj \
    --filter "AllMessageTypesInProcess" -c Release
```

Expected: both pass. The matrix test asserts every shell-to-engine message type code has a registered handler in `RpcRouter.RegisteredMessageTypes`.

## 4 — Verify Gap 3: AI handlers are split + each ≤ 80 LOC

```bash
ls src/AkmlSql.Engine/Handlers/Ai/
```

Expected files:
- `AiHandlerBase.cs`
- `AiTextToSqlHandler.cs`
- `AiExplainHandler.cs`
- `AiFixHandler.cs`
- `AiOptimizeHandler.cs`
- `AiIndexAnalysisHandler.cs`
- `AiChatHandler.cs`
- `AiGhostTextHandler.cs`

Expected to NOT exist: `AiMessageHandlers.cs` (the legacy bridge — deleted at the end of P3).

```bash
ls src/AkmlSql.Engine/Ai/AiRequestHandler.cs 2>&1
```

Expected: "No such file or directory" — the monolith is deleted.

LOC budget:

```bash
wc -l src/AkmlSql.Engine/Handlers/Ai/*.cs
```

Expected: every concrete `*Handler.cs` file is ≤ 80 lines; `AiHandlerBase.cs` is ≤ 100 lines. (The base has slightly more slack because it carries the consent-check helper.)

Behavioural smoke check:

```bash
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj \
    --filter "Handlers.Ai" -c Release
```

Expected: `AiHandlerBaseTests` passes (covers privacy-consent gate + happy path); each of the seven per-handler smoke tests passes (covers local-provider direct dispatch).

Bridge verification:

```bash
grep -rn "AiMessageHandler\b" src/AkmlSql.Engine/
```

Expected: zero matches. The bridge class is gone.

## 5 — Verify Gap 4: perf gate at 5 % with heavier workloads

Open `tests/AkmlSql.Engine.Tests/PerformanceBaselineTests.cs` and confirm:

```csharp
private const double MaxRegressionFraction = 0.05;
```

Plus:
- `CorpusSql` is generated via `BuildCorpus(repeats: 10)` (or equivalent) — not a hand-written ~30-statement constant
- A `MeasureBulkFormat()` method exists alongside `MeasureCompletion()` and `MeasureFormat()`
- `BaselineDocument` carries a `BulkFormatRequest` field

Capture a fresh baseline:

```bash
rm -f tests/AkmlSql.Engine.Tests/baselines/m0-baseline.json
AKML_UPDATE_BASELINE=1 dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj \
    --filter "Capture_or_compare_M0_baseline" -c Release
```

Expected: PASS. The new baseline file lists three workload entries, each with `p50Ms ≥ 20` (≥ 30 for BulkFormat).

Three-runs-clean check:

```bash
for i in 1 2 3; do
    dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj \
        --filter "Capture_or_compare_M0_baseline" -c Release
done
```

Expected: all three runs PASS.

Synthetic-regression check (optional, manual):

1. Temporarily add `Thread.SpinWait(50000);` to `CompletionEngine.GetCompletions` (~10 % slowdown).
2. Re-run the perf gate.
3. Expected: FAIL with a message naming `CompletionRequest.p50` and the regression percentage.
4. Revert the change.

## 6 — Verify shell hosts still build

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
for proj in Ssms20 Ssms21 Ssms22 VS2019 VS2022 VS2026; do
    "$MSBUILD" "src/AkmlSql.${proj}/AkmlSql.${proj}.csproj" \
        -t:Restore -p:Configuration=Release -v:quiet
    "$MSBUILD" "src/AkmlSql.${proj}/AkmlSql.${proj}.csproj" \
        -t:Build -p:Configuration=Release -v:minimal \
        || echo "FAIL: ${proj}"
done
```

Expected: all six succeed. Any failure means the closure has accidentally touched code that the shells depend on — diagnose before merging.

## 7 — Verify the engine still publishes

```bash
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64
```

Expected: single-file output appears under `bin/Release/net10.0/win-x64/publish/AkmlSql.Engine.exe`.

## 8 — One-paragraph health summary

A successful closure looks like this:

> The engine builds clean. `_cachedSettings` lives in one file. `NamedPipeTransport.cs` exists, is ≤ 150 LOC, and the dispatch path goes through `RpcRouter.RouteAsync`. Every AI handler is its own file under `Handlers/Ai/`, each ≤ 80 LOC, deriving from `AiHandlerBase`. The performance gate runs at 5 % with p50 ≥ 20 ms per workload, passing three consecutive runs. The matrix test sees every registered message type. The six shell hosts and the engine all build. Nothing under `src/AkmlSql.Shell.Shared/` or the six shell project folders changed.

Anything that doesn't match means the closure is not complete; reopen the failing gap and reference the corresponding contract under `specs/022-m0-engine-closure/contracts/`.
