# Phase 1 Contracts — IPC Messages & Command Surface

The feature's external interfaces are (1) the shell↔engine **named-pipe IPC** (MessagePack `RpcMessage`, codes in `AkmlSql.Core/Ipc`) and (2) the host **command/menu surface** (`.vsct` per host + shell command classes). The IPC **wire format is unchanged**; existing codes keep their meaning. New codes are allocated from reserved free ranges and **must be confirmed against the current `RpcMessage.cs`** at task time (spec 029 already consumed 93/193; `EncryptedObjectDecryption` uses 92/192).

## 1. Reused IPC messages (no wire break — wiring or additive fields)

| Message (code → result) | Change | FR |
|---|---|---|
| `RequestSignatureHelp` (4 → 102) | Shell `SignatureHelpSource` now **sends/renders** it (was a log-only stub). No schema change. | FR-010 |
| `RequestQuickInfo` (5 → 103) | Shell `QuickInfoSource` now **sends/renders** it. **Additive** field on `QuickInfoResult`: object creation script for the definition Script tab. | FR-009, FR-017 |
| `FormatAction` (13 → 113) | Engine `HandleFormatAction` now **dispatches action types 0–5** to existing `IFormatAction` classes. No schema change. | FR-003 |
| `RequestAnalyze` (25 → 125) | **Additive** field `FilePath` on the request so the engine resolves the project `.casettings` directory for the live editor. | FR-024 |
| `RefactorPreview` (30 → 130) / `RefactorApply` (31 → 131) | **Additive** `RefactorKind` values: `SmartRenameDbWide`, `InlineProc`, `InlineExec`, `InsertToUpdate`, `ScriptAsAlter`. Preview returns the reviewable script; apply executes it. | FR-018, FR-020, FR-021, FR-022 |
| `SnippetExpand` (20 → 120) | Caller fixed to pass the **shortcode** (not the body); **additive** selection field so `$SELECTEDTEXT$`/surround works on desktop. | FR-030, FR-034, FR-035 |
| `SnippetImport` (24 → 124) | Implement the stubbed `.sqlpromptsnippet` (SqlPromptXml) import format with token mapping. No schema change. | FR-032 |
| `AnalysisSettingsChanged` (26) | Reused to invalidate cached rule settings when the Manage-Rules dialog writes overrides. | FR-026 |

## 2. New IPC messages (candidate allocations — confirm free slots in `RpcMessage.cs`)

| Message | Candidate code → result | Purpose | FR |
|---|---|---|---|
| `FindInvalidObjects` | 27 → 127 | Engine returns objects with broken/invalid definitions (replaces `FindInvalidObjectsHandlerStub`). | FR-019 |
| `ListAnalysisRules` | 29 → 129 | Engine returns the rule catalog (id, name, category, default severity, enabled) for the Manage-Rules dialog. | FR-026 |
| `ObjectSearch` | 32 → 132 | Command-Palette database-object search. **Reuse the existing `ObjectSearchWindow` IPC if one already exists**; only allocate if not. | FR-045 |

> Allocation rule (mirrors spec 029): pick the lowest free request code and its `+100` result code; document the choice in `RpcMessage.cs` with a `// Spec 030` comment.

## 3. Command / menu surface (`.vsct` per host + shell commands)

Existing placeholder command IDs to **wire** (audit found these defined but unbacked): `CmdInlineStoredProcedure`, `CmdDisableFormattingForSelection`, `CmdToggleSuggestions`. Existing format-action commands (casing/semicolons/qualify/wildcards/brackets) and `SafeRename` become functional via R2/R8.

| Command | Status | Surface | FR |
|---|---|---|---|
| `CmdBulkFormat` | **New** — opens the built `BulkFormatWizard` | AKML SQL menu + palette | FR-046 |
| `CmdManageRules` | **New** — Manage Rules dialog | AKML SQL menu | FR-026 |
| `CmdToggleAnalysis` | **New/wire** — analysis on/off (optional `Ctrl+Shift+A`) | AKML SQL menu | FR-029 |
| Smart Rename | **Wire** `SafeRename` command to DB-wide preview/apply | context menu / palette | FR-018 |
| `CmdFindInvalidObjects` | **New/wire** | AKML SQL menu | FR-019 |
| `CmdInlineStoredProcedure` | **Wire** existing placeholder | context menu | FR-020 |
| `CmdInlineExec`, `CmdInsertToUpdate`, `CmdScriptAsAlter` | **New** | context menu | FR-020/021/022 |
| `CmdDisableFormattingForSelection` | **Wire** existing placeholder (marker insert) | Actions/menu | FR-023 |
| Column picker | **New** — invoked within completion (`Ctrl+Left/Right`), not a top-level command | completion popup | FR-013 |
| Style create/copy/set-active | **Finish** the deferred Format-Styles-editor buttons | Format Styles editor | FR-007 |

> Per CLAUDE.md, every shell/`.vsct` change lands in `AkmlSql.Shell.Shared` (shared source) and is built **per host with full MSBuild** (SSMS 22 + VS 2026). Keyboard chords stay as AKML's existing bindings unless a behavior requires SQL Prompt's chord (spec Assumptions).

## 4. Configuration schema (`AppSettings`) deltas

Additive fields (atomic `ConfigManager` writes); existing fields become **honored** where the audit found them inert:

- **Honored (already exist)**: `IntelliSense.Enabled`, `IntelliSense.AutoTrigger`, `ColumnScope` (R6); `FormatActionConfig` (R2).
- **New fields** (for Options coverage, FR-042/FR-043): alias policy (include-AS, custom object→alias map, prefixes-to-ignore); special-characters (auto-close characters, add-parentheses); suggestion connection scope (databases/schemas, load-linked-servers); history `DisableAutoTrim`; tab-color database match target. Add a backing field only where one is missing; otherwise surface the existing field in Options.

## 5. Contract test obligations

- **IPC additive fields** (QuickInfoResult script, RequestAnalyze.FilePath, SnippetExpand selection, new RefactorKinds): round-trip MessagePack serialize/deserialize tests; old shell ↔ new engine stays compatible (unknown new fields tolerated).
- **New messages**: handler unit tests (engine) + a serialize/deserialize contract test for each request/result pair.
- **Format pipeline (R1/R2)**: idempotency + semantic-equivalence tests *through the pipeline* per rule group and per action.
- **Analysis live `.casettings` (R3)**: an editor-path request produces the same findings the CLI produces on the same file + settings (SC-005).
