# Research: SQL Formatter & Code Beautifier

**Branch**: `003-sql-formatter` | **Date**: 2026-03-20

## R1: ScriptDom for SQL Formatting

### Decision: Use ScriptDom's token stream + AST hybrid approach for formatting

**Rationale**: ScriptDom 170.191.0 provides a complete T-SQL parser with full token stream preservation. Every `TSqlFragment` node has `FirstTokenIndex`/`LastTokenIndex` into a shared `ScriptTokenStream` that includes all tokens (keywords, identifiers, comments, whitespace). The AST provides structural context for formatting decisions; the token stream preserves the exact original text including comments.

**Key findings**:

- **Token stream is lossless**: Concatenating all `TSqlParserToken.Text` values reproduces the exact original input.
- **ScriptDom's built-in `SqlScriptGenerator`**: Has ~35 formatting options (`KeywordCasing`, `IndentationSize`, `NewLineBeforeFromClause`, etc.) and a `PreserveComments` flag. However, its comment placement is approximate — comments get attached to adjacent tokens by proximity, not semantic intent. Inline comments may shift position. This is insufficient for a production formatter that promises byte-for-byte comment preservation.
- **Custom hybrid approach preferred**: Walk the AST for structural decisions (indentation, line breaks, casing) but emit using the original token stream, only replacing whitespace tokens. This preserves comments exactly where they are relative to surrounding tokens.
- **Error-tolerant parsing**: `TSqlParser.Parse()` always returns a `TSqlScript`, even with errors. Valid statements are parsed; malformed regions are skipped. `ParseError` provides offset, line, column for each error.
- **Selection formatting**: Every AST node has `StartOffset`/`FragmentLength`. Given a selection range, walk the tree to find the smallest enclosing `TSqlFragment`, format it with `GenerateScript()`, and splice back.
- **Semantic validation**: Re-parse formatted output and compare normalized script generation from both parse trees. If `SqlScriptGenerator` produces identical output for both, they are semantically equivalent.

**SQL Server version support**:

| SqlVersion Enum | SQL Server | Supported |
|---|---|---|
| Sql130 | 2016 | Yes |
| Sql140 | 2017 | Yes |
| Sql150 | 2019 | Yes |
| Sql160 | 2022 | Yes |
| Sql170 | 2025 | Yes (Vector, DiskANN, AI functions) |
| SqlFabricDW | Fabric | Yes (dedicated parser/generator) |

**Alternatives considered**:
- Pure `SqlScriptGenerator` output: Rejected — drops comment positions, limited to ~35 options vs. our 250+ requirement.
- Pure token stream walk (no AST): Rejected — cannot make structural decisions (e.g., "this comma is in a SELECT list" vs. "in a function call").
- Custom parser: Rejected — ScriptDom is the canonical T-SQL parser, no reason to rebuild.

---

## R2: Noformat Regions and Comment Preservation

### Decision: Pre-scan token stream for noformat ranges; hybrid AST + token stream emit for comments

**Noformat strategy**:
1. **Pre-scan phase**: Linear scan of token stream before AST walking. For each `SingleLineComment` or `MultilineComment` token, check if text matches `--noformat` / `/* noformat */` (case-insensitive). Build sorted list of `NoformatRegion { StartOffset, EndOffset }` ranges.
2. **Offset-based exclusion**: During formatting, check if any token range overlaps a noformat region. If so, emit original source text byte-for-byte.
3. **Unmatched open tag**: Extends to EOF.
4. **Nested tags**: First open to last close = single region.
5. **GO batch boundaries**: Noformat scanner runs on full document text before batch splitting. Batch splitter must not split inside a noformat region.

**Comment preservation strategy**:
1. **Build comment attachment map**: After parsing, scan token stream. Classify each comment as:
   - **Trailing**: Same line as preceding semantic token (e.g., `SELECT col1, -- primary key`)
   - **Leading**: Own line(s) before next semantic token
   - **Standalone**: No adjacent semantic tokens
2. **During emit**: Process tokens from `FirstTokenIndex` to `LastTokenIndex` per AST node. Semantic tokens get formatted; comment tokens emit verbatim; whitespace tokens get replaced with formatter-computed whitespace.
3. **Inter-node comments**: Comments between AST siblings (in the token gap between `node1.LastTokenIndex` and `node2.FirstTokenIndex`) are explicitly scanned and emitted.

**SQLCMD directive handling**:
1. **Pre-process** (after noformat scan, before parsing): Replace line directives (`:setvar`, `:connect`, etc.) with sentinel comments `--__SQLCMD_LINE_N__`. Replace inline `$(Variable)` with placeholder identifiers `__SQLCMD_VAR_N__`.
2. **Post-process** (after formatting): Restore sentinels with original text.
3. **Order**: Noformat scan → SQLCMD preprocessing (only outside noformat regions) → parse → format → SQLCMD restore.

**Pipeline order**:
```
Raw SQL → [1] Noformat scan → [2] SQLCMD preprocess → [3] Batch split → [4] Parse →
[5] Comment map → [6] AST walk + token emit → [7] SQLCMD restore → [8] Semantic validate
```

**Alternatives considered**:
- Handle noformat during AST visit: Rejected — noformat regions can span arbitrary AST boundaries.
- Pure AST emit + comment re-insertion: Rejected — fragile comment positioning, especially for inline comments.
- ScriptDom SQLCMD parsing mode: Does not exist.

---

## R3: VS SDK Profile Editor UI

### Decision: Modal WPF `DialogWindow` with programmatic construction in Shell.Shared

**Window type**: `Microsoft.VisualStudio.PlatformUI.DialogWindow` (modal). Available across VS SDK 12.0+, so compatible with all targets (15.x through 17.x). Save/Cancel semantics are natural for a profile editor.

**Cross-SDK strategy**: Build the entire WPF visual tree programmatically in C# (no XAML files). The existing codebase already follows this pattern — there are zero `.xaml` files in Shell.Shared. XAML compilation in shared projects is fragile across SDK versions.

**Theme integration**: Use `EnvironmentColors` resource keys with `SetResourceReference()` for automatic theme updates:
- Dialog background: `EnvironmentColors.ToolWindowBackgroundBrushKey`
- Text: `EnvironmentColors.ToolWindowTextBrushKey`
- Borders: `EnvironmentColors.ToolWindowBorderBrushKey`
- Buttons: `EnvironmentColors.ButtonFaceBrushKey` / `ButtonTextBrushKey`

**Layout**: `Grid` + `GridSplitter` for split-pane. Left side: `TreeView` for option categories. Right side: upper `ScrollViewer` for option controls, lower pane for SQL preview. Another `GridSplitter` between options and preview.

**Preview control**: Read-only `RichTextBox` with `FlowDocument` and syntax-colored `Run` elements. NOT `IWpfTextViewHost` — embedding the VS editor inside a dialog is fragile across SDK versions, heavyweight, and unnecessary for read-only preview.

**Alternatives considered**:
- `ToolWindowPane` (dockable): Rejected — wrong UX for edit-then-commit workflow.
- WinForms `Form`: Rejected — cannot produce split-pane layout with rich text preview.
- XAML files in shared project: Rejected — cross-SDK compilation issues.
- `IWpfTextViewHost` for preview: Rejected — requires MEF service resolution, fragile in SSMS IsolatedShell.
- AvalonEdit: Rejected — unnecessary third-party dependency for read-only preview.

---

## R4: CLI Formatter Architecture

### Decision: Separate `AkmlSql.Formatting` library + `AkmlSql.Formatter` CLI project

**Code sharing**: Extract formatting logic into `AkmlSql.Formatting` (.NET 10 class library). Both `AkmlSql.Engine` and `AkmlSql.Formatter` reference it. This avoids the CLI carrying the entire engine (schema cache, completion providers, pipe server).

**CLI project**: `AkmlSql.Formatter`, same pattern as `AkmlSql.Updater` — .NET 10, self-contained, `PublishTrimmed`, win-x64.

**DiffPlex**: `InlineDiffBuilder.Diff()` for diff computation. DiffPlex does NOT produce standard unified diff format natively — requires a custom `UnifiedDiffFormatter` adapter to emit `--- a/file` / `+++ b/file` / `@@ -X,Y +A,B @@` headers.

**Parallel bulk formatting**: `Parallel.ForEachAsync` with per-file read-format-write pipeline. Configurable `--parallel N` (default: `Environment.ProcessorCount`). No batched read phase — per-file pipeline keeps memory bounded.

**Profile serialization**: `System.Text.Json` source generators for AOT/trim compatibility. `[JsonExtensionData]` on profile model to preserve unknown fields from newer versions (forward compatibility). `schemaVersion` integer in metadata (separate from user-facing `version` string).

**Exit codes** (from PRD, validated against industry conventions):

| Code | Meaning | Convention Match |
|---|---|---|
| 0 | Success | Standard POSIX |
| 1 | Formatting violations (check mode) | Matches `prettier --check`, `black --check` |
| 2 | Parse error | Standard for parse/usage errors |
| 3 | File not found / permission denied | Distinct from parse error |
| 4 | Invalid profile | Useful for CI debugging |
| 5 | Internal error | Catch-all |

Aggregate: highest-severity code wins across all files. Per-file details in JSON report.

**Alternatives considered**:
- CLI as second entry point in `AkmlSql.Engine`: Rejected — engine carries schema cache, completion, pipe server overhead.
- No separate formatting library (everything in engine): Rejected — CLI would depend on full engine binary.
- JSON.NET for profiles: Rejected — System.Text.Json is already used, source generators enable trimming.
