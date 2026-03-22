# Research: Static Code Analysis Engine

**Branch**: `005-static-code-analysis` | **Date**: 2026-03-22

---

## R-001: Analysis Unit — What Is the Minimal Re-Analysis Unit?

**Question**: When the user edits a document, what is the smallest unit that needs to be re-analyzed to maintain correctness without analyzing the entire file?

**Decision**: T-SQL **batch** (statements separated by `GO`). Within a batch, if any statement changes, re-analyze all statements in that batch only.

**Rationale**:
- ScriptDom parses at the `TSqlBatch` level — a change to one statement can affect alias resolution, variable declarations, and CTE scope within the same batch
- Cross-batch references don't exist in T-SQL (each GO-separated batch is independently executed)
- Batches are already the unit of analysis in the existing `TsqlParserService`

**Alternatives Considered**:
- *Individual statement* — too granular; a CTE declared in a WITH clause affects all following statements in the same batch
- *Entire file* — correct but too slow for real-time use on 10,000-line files
- *Unchanged* — no incremental analysis — fails the 200ms target

**How to Implement**: Hash each batch's text. On document change, recompute hashes, re-analyze only batches whose hash changed. Cache `IEnumerable<AnalysisDiagnostic>` per batch hash.

---

## R-002: Rule Visitor Pattern — TSqlFragmentVisitor vs. Manual Token Walk

**Question**: Should rules use ScriptDom's `TSqlFragmentVisitor` (AST walk) or manually scan the token stream?

**Decision**: **TSqlFragmentVisitor for rules that need semantic context** (most rules); **token stream scan for rules that are purely lexical** (e.g., ST001 keyword casing, DEP004 old-style `*=` join syntax).

**Rationale**:
- The visitor gives type-safe access to specific AST node types (e.g., `SelectStatement`, `DeleteStatement`, `CreateProcedureStatement`) — far less code and far fewer false positives than regex on token streams
- Some rules (casing, whitespace, comment style) don't need AST — they can run on the flat token stream and are faster to implement and execute that way
- The existing `AliasResolver` and `CteResolver` already use `TSqlFragmentVisitor` — this is the established project pattern

**Alternatives Considered**:
- *Token-only for all rules* — simpler but produces high false positive rates (e.g., `= NULL` inside a comment would fire BP004)
- *Regex on raw text* — unreliable, context-unaware, difficult to maintain

**Rule Implementation Pattern**:
```csharp
// AST-based rule
public class PE001_AvoidSelectStar : IAnalysisRule
{
    public string RuleId => "PE001";
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var visitor = new SelectStarVisitor(ctx);
        ctx.Script.Accept(visitor);
        return visitor.Diagnostics;
    }
}
```

---

## R-003: Parallel Rule Execution Pattern

**Question**: How should rules be executed in parallel without shared state corruption?

**Decision**: `Task.WhenAll` over all enabled rules for a given batch, with each rule receiving an immutable `AnalysisContext` snapshot. Rules return `IEnumerable<AnalysisDiagnostic>` — no mutation of shared state.

**Rationale**:
- Rules are pure functions: `AnalysisContext → IEnumerable<AnalysisDiagnostic>`
- `TSqlScript` and `TSqlBatch` objects are constructed once per parse and are read-only during analysis
- `Task.WhenAll` with `CancellationToken` allows the analysis to be cancelled when a new keystroke arrives before the previous run completes

**Concurrency Cap**: 8 concurrent rules (from PRD) implemented via `SemaphoreSlim(8)` or a dedicated `TaskScheduler` with max concurrency.

**Cancellation Pattern**:
- `AnalysisController` (shell side) holds a `CancellationTokenSource`
- On each keystroke (after debounce delay), cancel previous token, issue new request
- Engine-side `AnalysisEngine.AnalyzeAsync(request, ct)` passes `ct` to each rule task

---

## R-004: VS Tagger / Squiggles API

**Question**: Which VS SDK API to use for underline squiggles in the SQL editor?

**Decision**: `ITagger<IErrorTag>` implemented in `DiagnosticTagger`, registered via `[Export(typeof(IViewTaggerProvider))]` MEF attribute.

**Rationale**:
- This is the standard VS extension pattern for editor decorations — already used by VS built-in analyzers and Roslyn
- Works in both SSMS and VS hosts because the SQL editor uses the same MEF text editor infrastructure
- `IErrorTag.ErrorType` controls squiggle color: `PredefinedErrorTypeNames.SyntaxError` (red), `PredefinedErrorTypeNames.Warning` (green), `PredefinedErrorTypeNames.OtherError` (blue) — map to Error/Warning/Information severity

**Tag Update Pattern**:
- `DiagnosticTagger` holds the latest `CodeIssueInfo[]` in a field
- When the `AnalysisController` receives new results, it invokes `TagsChanged` event on the tagger
- VS framework re-queries tags on the visible span

---

## R-005: VS Lightbulb / SuggestedActions API

**Question**: Which VS SDK API to use for the fix lightbulb?

**Decision**: `ISuggestedActionsSource` implemented in `LightbulbProvider`, registered via `[Export(typeof(ISuggestedActionsSourceProvider))]` MEF.

**Rationale**:
- Standard VS lightbulb API; works in both VS and SSMS (SSMS 21+ uses VS 17 shell)
- Each `ISuggestedAction` implementation (`FixAction`) receives the text edit to apply
- `ISuggestedAction.TryGetTelemetryId` can return the rule ID for future telemetry

**Fix Application Pattern**:
- Each `FixAction` holds a `Span` and replacement text
- `ISuggestedAction.Invoke(ct)` calls `ITextBuffer.Replace(span, newText)` on the UI thread
- Undo is automatic — VS wraps each `Replace` in an undo transaction if the edit is done through `ITextEdit`

---

## R-006: CAsettings Merge Precedence

**Question**: When both global settings and a project-level CAsettings file exist, how should they be merged?

**Decision**: **Layered override** — innermost wins:
1. Built-in defaults (rule default severity, enabled=true)
2. Global AKML SQL config (`%AppData%/AKML SQL/config.json` — the `CodeAnalysis` section)
3. Project-level CAsettings file (nearest ancestor directory of the open file containing a `.casettings` file)
4. Inline `-- noqa` suppression (overrides everything for that line/block)

**Rationale**:
- Matches the industry standard (ESLint, StyleCop, .editorconfig all use nearest-wins directory traversal)
- "Nearest ancestor" means a CAsettings in `C:\Projects\MyDB\` applies to all files in that folder and subfolders; a more specific file in `C:\Projects\MyDB\Migrations\` overrides it only for that subdirectory

**Discovery Algorithm**:
1. Starting from the directory of the currently open file, walk up to the drive root
2. First `.casettings` file found wins; stop searching after that
3. Result is cached per directory path; cache invalidated if the file changes (FileSystemWatcher)

---

## R-007: CLI Exit Codes and Report Format

**Question**: What exit code and report schema should the CLI tool use for CI/CD compatibility?

**Decision**:
- Exit 0 = no violations at or above the threshold severity
- Exit 1 = one or more violations at or above the threshold severity
- Exit 2 = analysis error (file not found, malformed SQL, etc.)

**Report format**: JSON matching the schema defined in the PRD (see `contracts/cli-interface.md`).

**Rationale**:
- Exit 0/1 is the universal CI tool contract (matches eslint `--max-warnings`, sqlfluff, etc.)
- Exit 2 for tool errors distinguishes "code has problems" from "tool couldn't run" — important for CI pipelines where a tool crash should not silently pass

---

## R-008: SQL Prompt CAsettings Import

**Question**: How should SQL Prompt CAsettings XML be mapped to AKML JSON?

**Decision**: One-way import only (SQL Prompt XML → AKML JSON). The importer reads the `<rule>` elements from SQL Prompt's XML format, maps known rule IDs 1:1 by ID string (BP001 → BP001, etc.), and writes a new AKML CAsettings JSON. Unknown SQL Prompt rule IDs are logged as skipped.

**Rationale**:
- AKML has more rules than SQL Prompt; round-trip export would lose AKML-specific rules
- 1:1 ID mapping is possible for the ~60 overlapping rules; no complex transformation needed
- Writing a new JSON file avoids mutating the user's original SQL Prompt settings

---

## R-009: ScriptDom Version Selection for Analysis

**Question**: Which ScriptDom parser version (TSql130–TSql170) should be used for analysis when no connection is present?

**Decision**: Use the same parser version selection as the existing `TsqlParserService` — derive from `SessionState.ServerVersion`. When no session is connected (e.g., standalone file), default to **TSql160** (SQL Server 2022).

**Rationale**:
- Matches the IntelliSense behavior already established in the project
- TSql160 is the most complete parser in ScriptDom and successfully parses all older syntax
- Using the wrong version risks false parse errors that suppress legitimate rule violations
