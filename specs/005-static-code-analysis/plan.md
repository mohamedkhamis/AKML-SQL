# Implementation Plan: Static Code Analysis Engine

**Branch**: `005-static-code-analysis` | **Date**: 2026-03-22 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/005-static-code-analysis/spec.md`

## Summary

Implement a real-time SQL static code analysis engine that scans T-SQL as the user types, surfaces violations as colored squiggles in the SSMS/VS editor, and offers one-click auto-fix actions. The engine runs out-of-process (inside the existing `AkmlSql.Engine` executable), receives analysis requests via the existing named-pipe RPC protocol, and returns structured diagnostics to the shell extension. Rules are stateless visitors over the ScriptDom AST, run in parallel, and operate incrementally (only re-analyze changed statements). A separate CLI tool (`AkmlSql.Analyzer.exe`) enables bulk analysis and CI/CD integration.

---

## Technical Context

**Language/Version**: C# / .NET 10 (Engine + CLI), .NET Standard 2.0 (Core models), .NET Framework 4.7.2 (Shell)
**Primary Dependencies**: Microsoft.SqlServer.TransactSql.ScriptDom (AST + visitors), MessagePack (IPC), Serilog (logging), xUnit 2.x (tests)
**Storage**: JSON files for CAsettings; no database; analysis results are ephemeral (per-request)
**Testing**: xUnit 2.x via `dotnet test`; Arrange-Act-Assert; `[Fact]` / `[Theory]` patterns matching existing Engine.Tests style
**Target Platform**: Windows; out-of-process Engine (win-x64 .NET 10); shell extension (.NET 472); SSMS 20/21/22 + VS 2019/2022/2026
**Project Type**: Background service (Engine) + VS shell extension consumer + standalone CLI tool
**Performance Goals**: Single-statement analysis < 20ms; 1,000-line file < 200ms; 10,000-line file < 1,000ms; auto-fix application < 50ms; bulk 100-file analysis < 30s
**Constraints**: Rules must be stateless (no shared mutable state); analysis must not block the UI thread; incremental (only changed batch/statement re-analyzed); up to 8 rules run concurrently per statement; cancellation on next keystroke
**Scale/Scope**: 200+ rules across 8 categories; 600+ new tests; all 6 host targets share the same analysis code via `AkmlSql.Shell.Shared`

---

## Constitution Check

*No constitution.md exists for this project yet. Gates inferred from CLAUDE.md conventions and existing codebase patterns.*

| Gate | Status | Notes |
|------|--------|-------|
| No new standalone projects beyond what is architecturally necessary | PASS | Two new projects: `AkmlSql.Engine/Analysis/` (folder, not project) added to existing Engine; `AkmlSql.Analyzer` is a new project but architecturally required for CLI independence |
| New code follows existing patterns (stateless services, MessagePack IPC, xUnit tests) | PASS | All new code mirrors existing provider/handler patterns |
| Shell extensions use Shared project pattern — no duplication | PASS | All analysis UI code goes into `AkmlSql.Shell.Shared` |
| No blocking UI-thread calls | PASS | Analysis runs in Engine (out-of-proc); shell fires async RPC |
| Tests for all new Engine logic | PASS | Tests planned in `AkmlSql.Engine.Tests/Analysis/` |

---

## Project Structure

### Documentation (this feature)

```text
specs/005-static-code-analysis/
├── plan.md              # This file
├── research.md          # Phase 0 — patterns, decisions, alternatives
├── data-model.md        # Phase 1 — entities, state, transitions
├── quickstart.md        # Phase 1 — developer onboarding for this feature
├── contracts/
│   ├── rpc-messages.md        # IPC request/response contracts (new message types 25-35 / 125-135)
│   ├── casettings-schema.md   # CAsettings JSON file format
│   └── cli-interface.md       # CLI argument contract and exit codes
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── AkmlSql.Core/
│   ├── Config/
│   │   └── AppSettings.cs            # + CodeAnalysisSettings class
│   ├── Ipc/
│   │   ├── MessageTypes.cs           # + RequestAnalyze=25, AnalysisResult=125, etc.
│   │   └── Messages/
│   │       ├── CodeAnalysisRequest.cs
│   │       ├── CodeAnalysisResponse.cs
│   │       ├── CodeIssueInfo.cs       # MessagePack-serializable diagnostic
│   │       └── FixActionInfo.cs       # MessagePack-serializable fix action
│   └── Models/
│       └── Analysis/
│           ├── CaSettings.cs          # Rule configuration document
│           ├── RuleConfig.cs          # Per-rule enable/severity override
│           └── GlobalSuppression.cs   # Project-wide suppression entry
│
├── AkmlSql.Engine/
│   └── Analysis/
│       ├── AnalysisEngine.cs          # Orchestrator: parse → run rules → return diagnostics
│       ├── IAnalysisRule.cs           # Rule interface: Analyze(AnalysisContext) → IEnumerable<AnalysisDiagnostic>
│       ├── AnalysisDiagnostic.cs      # In-proc diagnostic model (ruleId, severity, span, message, fixes)
│       ├── AnalysisContext.cs         # Per-analysis input: AST, tokens, schema cache, session, settings
│       ├── AnalysisFixAction.cs       # Fix type, label, text replacement
│       ├── SuppressionParser.cs       # Reads -- noqa: / -- noqa-begin/end from token stream
│       ├── CaSettingsLoader.cs        # Loads/merges CAsettings JSON; project-dir override logic
│       ├── RuleRegistry.cs            # Discovers and instantiates all IAnalysisRule implementations
│       └── Rules/
│           ├── Performance/            # PE001–PE035 (IAnalysisRule implementations)
│           ├── BestPractices/          # BP001–BP030
│           ├── Security/               # SE001–SE020
│           ├── Style/                  # ST001–ST025
│           ├── Deprecated/             # DEP001–DEP020
│           ├── Design/                 # DE001–DE025
│           ├── Execution/              # EX001–EX020
│           └── Naming/                 # NM001–NM025
│
├── AkmlSql.Analyzer/                  # NEW standalone CLI project (net10.0, win-x64, self-contained)
│   ├── AkmlSql.Analyzer.csproj
│   ├── Program.cs                     # CLI entry point
│   ├── AnalyzerOptions.cs             # --file, --directory, --recursive, --check, --severity, --settings, --report
│   └── ReportWriter.cs                # JSON report serialization
│
└── AkmlSql.Shell.Shared/
    └── Analysis/
        ├── AnalysisController.cs       # Debouncer: keystroke → delayed RPC → apply squiggles
        ├── DiagnosticTagger.cs         # IErrorTag tagger: maps CodeIssueInfo → editor error markers
        ├── LightbulbProvider.cs        # ISuggestedActionsSource: CodeIssueInfo → fix menu
        ├── FixAction.cs                # ISuggestedAction implementation (applies text edits)
        ├── ErrorListReporter.cs        # Pushes Error/Warning issues to VS Error List
        └── BulkAnalysisCommand.cs      # "Run Code Analysis" menu command + result dialog

tests/
└── AkmlSql.Engine.Tests/
    └── Analysis/
        ├── AnalysisEngineTests.cs       # Integration: engine returns correct diagnostics
        ├── SuppressionParserTests.cs    # noqa parsing edge cases
        ├── CaSettingsLoaderTests.cs     # Settings load/merge/override logic
        └── Rules/
            ├── Performance/             # One test class per rule (PE001Tests.cs, etc.)
            ├── BestPractices/
            ├── Security/
            ├── Style/
            ├── Deprecated/
            ├── Design/
            ├── Execution/
            └── Naming/
```

**Structure Decision**: The analysis engine lives as a folder namespace inside the existing `AkmlSql.Engine` project — no new .NET project needed for the core analysis. A single new project (`AkmlSql.Analyzer`) is added only for the standalone CLI tool, which must run independently of the VS/SSMS shell. All shell-side UI (squiggles, lightbulbs, error list, bulk analysis command) goes into `AkmlSql.Shell.Shared` following the existing shared-project pattern.

---

## Complexity Tracking

*No constitution violations requiring justification.*

---

## Implementation Phases

### Phase 0 — Research & Decisions *(complete — see research.md)*

- Incremental analysis: which ScriptDom unit to use as the analysis unit
- Rule visitor pattern: TSqlFragmentVisitor vs. manual token scan
- Parallel rule execution: Task.WhenAll + CancellationToken pattern
- VS tagger / squiggle API for SSMS and VS
- VS lightbulb / ISuggestedActionsSource pattern
- CAsettings merge precedence (global → project-dir → inline suppression)

### Phase 1 — Core Models & Engine (no UI)

1. Add `CodeAnalysisSettings` to `AppSettings`
2. Add new IPC message types (25–35 / 125–135) and serializable models
3. Create `IAnalysisRule`, `AnalysisDiagnostic`, `AnalysisContext`, `AnalysisFixAction`
4. Create `SuppressionParser` (noqa token reader)
5. Create `CaSettingsLoader` (JSON load + project-dir override)
6. Create `RuleRegistry` (reflection-based rule discovery)
7. Create `AnalysisEngine` (orchestrator: parse → suppress → parallel rules → filter)
8. Wire `RequestAnalyze` into `PipeRpcServer.DispatchAsync`
9. Implement PE001–PE010 (first 10 performance rules + fixes) as reference implementations
10. Tests for items 3–9

### Phase 2 — Rules Batch 1: Performance + Best Practices

- Implement PE011–PE035 (remaining performance rules)
- Implement BP001–BP030 (best practice rules)
- Tests for all rules (both trigger and false-positive non-trigger)

### Phase 3 — Rules Batch 2: Security + Deprecated + Design

- Implement SE001–SE020, DEP001–DEP020, DE001–DE025
- Tests for all rules

### Phase 4 — Rules Batch 3: Style + Execution + Naming

- Implement ST001–ST025, EX001–EX020, NM001–NM025
- Tests for all rules

### Phase 5 — Shell Extension UI

1. `DiagnosticTagger` — squiggles
2. `ErrorListReporter` — Error List integration
3. `LightbulbProvider` + `FixAction` — fix menu
4. `AnalysisController` — debounce + RPC + apply tagger results
5. Analysis tab in `SettingsDialog` (enable/disable rules, severity overrides)
6. `BulkAnalysisCommand` + result dialog

### Phase 6 — CLI Tool + CAsettings Import

1. `AkmlSql.Analyzer.csproj` CLI project
2. `--file`, `--directory`, `--recursive`, `--check`, `--severity`, `--settings`, `--report`
3. SQL Prompt CAsettings XML → AKML JSON importer
4. JSON report writer

### Phase 7 — QA & Performance

- Performance benchmarks (1k-line, 10k-line scripts)
- False positive sweep across test corpus
- Full test matrix run (600+ tests)
- Integration testing on SSMS 22 and VS 2022
