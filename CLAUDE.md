# AKML-SQL Development Guidelines

AI-powered SQL development assistance for SSMS 20/21/22 and Visual Studio 2019/2022/2026.
Author: Abdulrahman Khamis | License: MIT | Version: 1.0.0

## Project Structure

```text
AKML-SQL.slnx                          # Solution file (.slnx format)
src/
  AkmlSql.Core/                        # Shared library (netstandard2.0 + net10.0)
  AkmlSql.Shell.Shared/                # Shared project (.projitems) for all shell extensions
  AkmlSql.Ssms20/                      # SSMS 20 extension (net472, x86, VS SDK 15.9.3)
  AkmlSql.Ssms21/                      # SSMS 21 extension (net472, x64, VS SDK 17.14.x)
  AkmlSql.Ssms22/                      # SSMS 22 extension (net472, x64, VS SDK 17.14.x)
  AkmlSql.VS2019/                      # VS 2019 extension (net472, x86, VS SDK 16.0.208)
  AkmlSql.VS2022/                      # VS 2022 extension (net472, x64, VS SDK 17.14.x)
  AkmlSql.VS2026/                      # VS 2026 extension (net472, x64, VS SDK 17.14.x)
  AkmlSql.Updater/                     # Self-contained updater (net10.0, win-x64, trimmed)
  AkmlSql.Installer/                   # Inno Setup 7 installer scripts
tests/
  AkmlSql.Core.Tests/                  # xunit tests (net10.0)
doc/                                   # PRD documents (Phase 1, Phase 2)
specs/                                 # Specify framework feature specs
```

## Technologies

- **Shell Extensions**: C# / .NET Framework 4.7.2, LangVersion latest
- **Core Library**: netstandard2.0 (for shell) + net10.0 (for updater), dual-target
- **Engine**: .NET 10, self-contained single-file, win-x64, PublishTrimmed (out-of-process IntelliSense)
- **Updater**: .NET 10, self-contained single-file, win-x64, PublishTrimmed
- **Installer**: Inno Setup 7 Pascal Script
- **Tests**: xunit 2.x, Microsoft.NET.Test.Sdk 17.x
- **Logging**: Serilog 4.x + Serilog.Sinks.File 6.x
- **JSON**: System.Text.Json 8.x (netstandard2.0 polyfill)

## VS SDK Versions (Critical)

| Target   | VS SDK Version | VSSDK.BuildTools | Platform | Shell Assembly Version |
|----------|---------------|------------------|----------|----------------------|
| SSMS 20  | 15.9.3        | 15.*             | x86      | 15.0.0.0             |
| VS 2019  | 16.0.208      | 16.*             | x86      | 16.0.0.0             |
| SSMS 21  | 17.14.*       | 17.*             | x64      | 17.0.0.0             |
| SSMS 22  | 17.14.*       | 17.*             | x64      | 17.0.0.0             |
| VS 2022  | 17.14.*       | 17.*             | x64      | 17.0.0.0             |
| VS 2026  | 17.14.*       | 17.*             | x64      | 17.0.0.0             |

## Build Commands

Shell projects MUST be built individually with MSBuild (not `dotnet build`) to avoid VSCT cross-contamination:

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"

# Restore then build each project separately
"$MSBUILD" "src/AkmlSql.Ssms20/AkmlSql.Ssms20.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.Ssms20/AkmlSql.Ssms20.csproj" -t:Build -p:Configuration=Release -v:minimal

# Engine (out-of-process IntelliSense, must publish before installer)
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64

# Updater (uses dotnet)
dotnet publish src/AkmlSql.Updater/AkmlSql.Updater.csproj -c Release

# Installer (Inno Setup 7)
"/c/Program Files/Inno Setup 7/ISCC.exe" src/AkmlSql.Installer/AkmlSqlSetup.iss

# Tests
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
```

## Build Gotchas

- **Never use `dotnet build` for shell projects** — CodeTaskFactory in VSSDK requires full MSBuild
- **Never build shell projects via solution** — causes VSCT CTO cross-contamination (all projects look for the last project's .cto file)
- **Always clean obj/bin after SDK version changes** — stale NuGet cache causes wrong assembly version references
- **SSMS 20 uses Schema 2010 vsixmanifest** (`<Vsix>` root) — all other targets use Schema 2011 v2.0 (`<PackageManifest>`)
- **SSMS 20 = VS 2017 IsolatedShell** — Shell.15.0 assembly version must be 15.0.0.0, not 16.0.0.0
- **VSPackage.resx required for CTO embedding** — SDK-style projects need `VSPackage.resx` with `MergeWithCTO=true`; use `Update=` not `Include=` to avoid duplicate resource errors
- **SSMS 22 extension path is under `Release/`** — deploy to `<Root>/Release/Common7/IDE/Extensions/AkmlSql/`, not root-level
- **SSMS 21/22 custom menu bar** — `guidSHLMainMenu:IDG_VS_MM_TOOLSADDINS` is invisible in SSMS; use `CommandPlacement` with `IDG_VS_TOOLS_EXT_TOOLS` to place menus under Tools
- **Register commands before non-critical init** — `LoggerFactory.Initialize()` or `LoadValidator.Validate()` failures will prevent command registration if done first

## AutoLoad UI Contexts (Critical)

Each SSMS/VS host uses different UI contexts for package autoloading:

| Target   | AutoLoad Context GUID                          | Context Name        |
|----------|-------------------------------------------------|---------------------|
| SSMS 20  | `{e8fbc700-a1bd-11d0-a67c-00a0c9110051}`       | ShellInitialized    |
| SSMS 21  | `{B7B07F42-6013-4C67-A504-C771CBC7625A}`       | UICONTEXT_SSMS      |
| SSMS 22  | `{B7B07F42-6013-4C67-A504-C771CBC7625A}`       | UICONTEXT_SSMS      |
| VS 2019  | `{e8fbc700-a1bd-11d0-a67c-00a0c9110051}`       | ShellInitialized    |
| VS 2022  | `{e8fbc700-a1bd-11d0-a67c-00a0c9110051}`       | ShellInitialized    |
| VS 2026  | `{e8fbc700-a1bd-11d0-a67c-00a0c9110051}`       | ShellInitialized    |

## vsixmanifest InstallationTarget

| Target   | InstallationTarget Id              | Schema  |
|----------|------------------------------------|---------|
| SSMS 20  | `<IsolatedShell>ssms</IsolatedShell>` | 2010 |
| SSMS 21  | `Microsoft.VisualStudio.Ssms`      | 2011    |
| SSMS 22  | `Microsoft.VisualStudio.Ssms`      | 2011    |
| VS 2019  | `Microsoft.VisualStudio.Pro`       | 2011    |
| VS 2022  | `Microsoft.VisualStudio.Pro`       | 2011    |
| VS 2026  | `Microsoft.VisualStudio.Pro`       | 2011    |

## Architecture

- **Shared Project Pattern**: `AkmlSql.Shell.Shared` (.projitems) is imported by all 6 shell extension projects — same source compiled against different VS SDK versions
- **Package GUID**: `{A1B2C3D4-1111-2222-3333-444455556666}` (shared across all targets)
- **Command Set GUID**: `{A1B2C3D4-1111-2222-3333-444455557777}`
- **Menu Commands**: About, Check for Updates, Options, Send Feedback, View Logs
- **Atomic Config Writes**: ConfigManager uses temp file + rename pattern
- **Thread-safe Logger Init**: LoggerFactory uses Interlocked.CompareExchange
- **Update Flow**: Shell extension fires updater process → updater writes result JSON → shell reads on next load

### Process Boundary: Shell ↔ Engine

The shell runs inside the .NET Framework 4.7.2 VS/SSMS process. The engine is a separate `.NET 10` self-contained process. They communicate via a named pipe:

```
Pipe name: akmlsql-engine-{user-SID}-{shell-PID}
ACL: owner SID allowed, Network SID denied
Frame: [4-byte length][4-byte XOR CRC][MessagePack(RpcMessage)]
Max frame: 16 MB
```

`RpcMessage` carries: `MessageType` (int), `RequestId` (int, 0 for notifications), `Payload` (byte[]).

See [docs/ipc-api.md](docs/ipc-api.md) for all 30+ message types.

### Engine Components

| Component | Class | Responsibility |
|-----------|-------|---------------|
| IPC server | `PipeRpcServer` | Receives frames, dispatches to handlers |
| Session tracking | `SessionManager` | Holds active editor document text (10 MB limit per doc) |
| Parser | `TsqlParserService` | Thread-safe `TSql170Parser` wrapper |
| IntelliSense | `CompletionEngine` | Merges keywords + schema + snippets + functions |
| Formatter | `FormatRequestHandler` → `FormatterPipeline` | 7-stage formatting pipeline |
| Analysis | `AnalysisEngine` → `RuleRegistry` | 120+ rules across 8 categories |
| Snippets | `SnippetRequestHandler` → `SnippetLoader` | Expand/list/save/delete `.akmlsnippet` files |
| Refactoring | `RefactoringEngine` | Preview + apply (lightweight text + heavyweight schema-aware) |
| Schema cache | `SchemaCacheManager` → `SchemaMetadataService` | Phase A/B loading via `sys.*` views |
| Change detection | `ChangeDetector` | `CHECKSUM_AGG(BINARY_CHECKSUM(...))` over `sys.objects` |

### Schema Cache Lifecycle

1. **Phase A** (`PopulatePhaseAAsync`): `sys.objects` + `sys.schemas` + row-counts — target < 500ms
2. **Phase B** (`PopulatePhaseBAsync`): columns, FKs, parameters, descriptions — background task
3. **Change detection**: periodic `CHECKSUM_AGG` query; DDL regex triggers immediate Phase A refresh
4. **FK index**: `DatabaseCache.RebuildFkIndex()` builds `"schema.table"` → `List<ForeignKey>` for O(1) lookups
5. **LRU eviction**: `SchemaCacheManager` evicts least-recently-used caches when count exceeds `maxDatabases`

### Formatting Pipeline (7 stages)

```
NoformatScanner → SqlcmdPreprocessor → TSql170Parser → AstAnnotator
  → LayoutEngine → CasingEngine → TextEmitter → SemanticValidator → IdempotencyCheck
```

- Stage 6 (SemanticValidator) failure → return original SQL unchanged
- Stage 7 (IdempotencyCheck) controlled by `profile.Metadata.EnableIdempotencyCheck`
- `ProfileMetadata.SkipValidation` allows test pipelines to bypass stage 6

### Analysis Engine

- `RuleRegistry` auto-discovers all `IAnalysisRule` implementations via reflection
- Rules are organized in 8 categories: Performance (PE), BestPractices (BP), Security (SE), Style (ST), Design (DE), Deprecated (DEP), Execution (EX), Naming (NM)
- Per-project overrides via `.casettings` JSON (searched upward from current file's directory)
- Inline suppressions: `-- akml-disable RuleId` / `-- akml-enable RuleId` / `-- akml-disable-line RuleId`

See [docs/analysis-rules.md](docs/analysis-rules.md) for all rules.

## Key Paths at Runtime

- Config: `%AppData%/AKML SQL/config.json`
- Logs: `%AppData%/AKML SQL/logs/akmlsql-*.log`
- Update result: `%AppData%/AKML SQL/cache/update-available.json`

### Extension Install Paths

| Target  | Extension Directory |
|---------|---------------------|
| SSMS 20 | `<SSMS20Root>/Common7/IDE/Extensions/AkmlSql/` |
| SSMS 22 | `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql\` |

### Cache and Log Paths

| Target  | MEF/Component Cache | Activity Log | Private Registry |
|---------|---------------------|-------------|-----------------|
| SSMS 20 | `%LocalAppData%/Microsoft/SQL Server Management Studio/20.0_IsoShell/ComponentModelCache/` | `%AppData%/Microsoft/SQL Server Management Studio/20.0_IsoShell/ActivityLog.xml` | N/A (IsolatedShell) |
| SSMS 22 | `%LocalAppData%/Microsoft/SSMS/22.0_*/ComponentModelCache/` | `%AppData%/Microsoft/SSMS/22.0_*/ActivityLog.xml` | `%LocalAppData%/Microsoft/SSMS/22.0_*/privateregistry.bin` |

## Installer Details

- **Output**: `src/AkmlSql.Installer/Output/AKMLSQLSetup.exe`
- **Detection**: Registry + vswhere.exe + filesystem fallback (see `environment-scanner.iss`)
- **Post-install**: Clears MEF caches, writes config.json (only if absent)
- **Silent mode**: `/VERYSILENT /ACCEPTEULA /TARGETS=20,22,2022 /NOUPDATE`

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Out-of-process engine | .NET Framework ↔ .NET 10 isolation; crash safety; trimming/AOT |
| Shared `.projitems` | One source compiled against 6 VS SDK versions without duplication |
| MessagePack for IPC | ~3× faster + smaller than JSON; strongly typed; binary-safe |
| `ConcurrentDictionary` for schema cache | Lock-free reads; multiple background writers safe |
| Phase A / Phase B loading | Phase A < 500ms for first completion; columns/FKs in background |
| `CHECKSUM_AGG(BINARY_CHECKSUM(...))` | Single scalar query for change detection; `modify_date` passed directly (not CAST to INT which truncates to day granularity) |
| Atomic config writes | `File.Replace` (netstandard2.0) / `File.Move(overwrite:true)` (.NET 10) prevents partial-write corruption |
| Named pipe + SID ACL | Local-only IPC; no network exposure; per-shell-process pipe |
| `FK index` in `DatabaseCache` | `RebuildFkIndex()` builds `"schema.table"` → list dict for O(1) vs O(N) scan |
| `LoggerFactory` reads config JSON directly | Avoids circular dependency — ConfigManager calls `Log.Error` before logger exists |
| `_cachedSettings` in `PipeRpcServer` | Avoids per-request `ConfigManager.Load()` disk read; invalidated on `AnalysisSettingsChanged` |
| `SemanticValidator` accepts pre-parsed AST | Avoids re-parsing original SQL in stage 6 (only formatted SQL is parsed) |
| SSMS 20 uses Schema 2010 vsixmanifest | SSMS 20 is a VS 2017 IsolatedShell — must use `<Vsix>` root, not `<PackageManifest>` |
| `EnableIdempotencyCheck` in `ProfileMetadata` | Lets bulk operations skip the expensive second parse pass |

## Code Conventions

### Async patterns

- All IPC handlers are `async Task<RpcMessage?>` — never use `.GetAwaiter().GetResult()` (deadlock risk in VS thread model)
- Background work uses `Task.Run(() => ...)` from `EngineProcessManager` or `SchemaCacheManager`
- `CancellationToken` is threaded through all async SQL operations

### Schema queries

- Remove `ORDER BY` from catalog queries — results are sorted in-memory after population
- Use direct JOINs to `sys.objects`/`sys.schemas` rather than scalar functions like `OBJECT_NAME()` / `OBJECT_SCHEMA_NAME()` (those cause per-row sub-lookups)
- Combine multiple `SERVERPROPERTY` or `HAS_PERMS_BY_NAME` calls into a single query returning multiple columns
- Always filter `is_ms_shipped = 0` when querying user objects

### Security

- Path validation: use `Path.GetFullPath()` canonical check, not `.Contains("..")`
- Document size limit: 10 MB per session (`MaxDocumentSizeChars` in `SessionManager`)
- Snippet JSON limit: 1 MB (`MaxSnippetJsonChars` in `SnippetRequestHandler`)
- All file paths accepted from IPC must be absolute (`Path.IsPathRooted`)

### Configuration

- `AppSettings` is the POCO for `config.json` — all sections are nested objects
- `ConfigManager.Load()` is idempotent and safe to call multiple times, but prefer caching the result
- Default log level is `"Debug"` — change via `logMinimumLevel` in `config.json`

### WPF UI conventions

The shell has three established WPF surfaces: tool-window controls, modal dialogs (`Window` subclasses), and editor margins/adornments (`IWpfTextViewMargin` / adornment layers). **New WPF UI must match these rules** or it will look out of place — especially in dark/blue theme.

- **Theme colors come from `ThemeManager.Instance`** — never hardcode hex for chrome (background, foreground, border, muted text, card background, accent). The singleton exposes `Background`, `Foreground`, `Border`, `AccentColor`, `HighlightBackground`, `HighlightForeground`, `EditorPanelBackground`, `PreviewBackground`, `PlaceholderText`, `SplitterColor`, plus History-specific properties. Semantic colors (amber for "confirm", red for "destructive", green for "success") are the only acceptable hardcoded hex — they should read the same in every theme.
- **Freeze brushes**. Use `private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }` and wrap every `new SolidColorBrush(...)` in it. Frozen brushes are thread-safe, skip change notifications, and share cheaply.
- **Hoist `FontFamily` to `static readonly`**. `new FontFamily("Segoe UI")` / `new FontFamily("Consolas")` per call is a per-iteration allocation; make them class-level statics.
- **Set `Owner` via DTE HWND** before `ShowDialog()` so the dialog parents to the VS/SSMS main window and `WindowStartupLocation = CenterOwner` actually works. Reference pattern in `src/AkmlSql.Shell.Shared/History/HistoryDiffWindow.cs` — reads `EnvDTE.DTE.MainWindow.HWnd` inside a try/catch and assigns via `WindowInteropHelper.Owner`. Silent no-op when DTE is unreachable.
- **FR-005 safety dialogs**: Cancel must be `IsCancel = true` AND the `Loaded`-focused control; the "Execute/Drop/Proceed" button is *not* set as `AcceptButton`/default — it must be clicked deliberately. See `src/AkmlSql.Shell.Shared/Safety/SafetyWarningDialog.cs` for the canonical pattern.
- **Reference implementations** (study before writing new WPF code):
  - `src/AkmlSql.Shell.Shared/History/HistoryDiffWindow.cs` — minimal theme-aware `Window` with DTE owner
  - `src/AkmlSql.Shell.Shared/Safety/SafetyWarningDialog.cs` — multi-section modal dialog with cards, inline type-to-confirm, frozen brushes, extracted helper methods
  - `src/AkmlSql.Shell.Shared/Editor/SchemaProgress/SchemaProgressMargin.cs` — `IWpfTextViewMargin` with arc spinner (`Ellipse` + `StrokeDashArray { 10, 30 }` + rotating transform), fade-in/out animations, polling state machine

### Editor margin spinner pattern

For any editor-margin or adornment that needs a spinner: do **not** rotate a `Border` with a partial `BorderThickness` + `CornerRadius` (the old `SchemaProgressMargin` did this and it looked broken). Use an `Ellipse`:

```csharp
var spinner = new Ellipse {
    Width = 12, Height = 12,
    Stroke = accentBrush,
    StrokeThickness = 1.6,
    StrokeDashArray = new DoubleCollection { 10, 30 }, // ~90° arc, ~270° gap
    StrokeStartLineCap = PenLineCap.Round,
    StrokeEndLineCap = PenLineCap.Round,
    RenderTransformOrigin = new Point(0.5, 0.5),
    RenderTransform = new RotateTransform(0),
};
((RotateTransform)spinner.RenderTransform).BeginAnimation(
    RotateTransform.AngleProperty,
    new DoubleAnimation { From = 0, To = 360,
        Duration = TimeSpan.FromMilliseconds(1100),
        RepeatBehavior = RepeatBehavior.Forever });
```

The `StrokeDashArray` sum (`10 + 30 = 40`) must be ≥ the ellipse perimeter (`2πr ≈ 37.7` for a 12×12 ellipse) so only one arc segment is visible.

## Documentation

| File | Content |
|------|---------|
| [docs/architecture.md](docs/architecture.md) | Component map, startup sequence, data flows, design decisions |
| [docs/ipc-api.md](docs/ipc-api.md) | All IPC message types, request/response schemas, frame format |
| [docs/configuration.md](docs/configuration.md) | Full `config.json` schema, `.casettings`, logging |
| [docs/deployment.md](docs/deployment.md) | Build commands, install paths, MEF cache clearing, troubleshooting |
| [docs/analysis-rules.md](docs/analysis-rules.md) | All 120+ analysis rules with descriptions and severities |
| [docs/formatting.md](docs/formatting.md) | Formatting pipeline stages, profile schema, all options |
| [doc/progress.md](doc/progress.md) | Development log: issues, root causes, fixes, cache clearing procedures, Spec 014 Phase 3 status |

## Progress and Troubleshooting

See [doc/progress.md](doc/progress.md) for the full development progress log including all issues encountered, root causes, fixes applied, cache clearing procedures, and deployment instructions.

**Active branch**: `014-sql-prompt-parity`. Spec 014 Phase 1+2 and Phase 3/US1 are committed (`fba63d6`, `f337729`); Phase 3b (UI polish for `SafetyWarningDialog`, `SchemaProgressMargin`, and `ExecutionInterceptor` — detailed in the "Spec 014 Phase 3b" section of progress.md) is currently **uncommitted** in the working tree. Remaining user stories on this branch: US10 (AI shortcut bindings), US14 (Invalid Objects tool window), US19 (completion polish — `Ctrl+Shift+D` refresh, `Ctrl+Shift+P` toggle, custom commit keys, encrypted-object decryption), US20 (remaining gaps). See `specs/014-sql-prompt-parity/tasks.md` for the full task list.

## Git Rules (MANDATORY)

- **NEVER commit, push, or create PRs automatically** — only when the user explicitly says "commit", "push", or "create PR"
- **NEVER run `git add`, `git commit`, `git push`, or `gh pr create`** unless directly instructed
- **NEVER amend existing commits** unless the user explicitly asks
- When asked to make code changes, ONLY make the code changes — do not stage, commit, or push them
- If you think a commit is needed, tell the user and wait for their explicit instruction
