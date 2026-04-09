# Settings Schema Contract

**Branch**: `014-sql-prompt-parity` | **Date**: 2026-04-09

This document defines every new section added to `AppSettings` (which serializes to `%AppData%\AKML SQL\config.json`). Per A12, this is the single source of truth for all spec 014 toggles. No new persistence layer is introduced.

The existing `AppSettings` is a POCO in `src/AkmlSql.Core/Config/AppSettings.cs`. All new sections are nested objects under the root, following the existing convention.

---

## ExecutionWarnings (US1)

```text
ExecutionWarnings: {
  Enabled: bool                   // Master switch (default: true)
  Rules: [
    {
      Id: string                   // E.g. "DELETE_NO_WHERE"
      Severity: enum { Warning, Critical }
      Enabled: bool
      MessageTemplate: string      // Templated text shown in dialog
      EnvironmentOverride: { string => bool }?  // Optional per-environment overrides
    }
  ]
  ShowEnvironmentColorInHeader: bool  // Default: true
  DefaultButton: enum { Cancel, Execute }  // Default: Cancel (FR-005)
}
```

**Defaults**: Five rules pre-populated:
- `DELETE_NO_WHERE` (Critical, Enabled)
- `UPDATE_NO_WHERE` (Critical, Enabled)
- `MERGE_NO_FILTER` (Critical, Enabled)
- `INSIDE_JOIN` (Critical, Enabled)
- `INSIDE_PROC_OR_TRIGGER` (Warning, Enabled)

---

## TabColoring (US5)

```text
TabColoring: {
  Enabled: bool                    // Master switch (default: false)
  GradientEnabled: bool            // Global gradient toggle (default: true)
  Environments: [
    {
      Name: string                 // Unique
      ColorHex: string             // ^#[0-9A-Fa-f]{6}$
      GradientEnabled: bool        // Per-environment override
      Label: string?               // Optional tooltip
    }
  ]
  Assignments: [
    {
      Scope: enum { Server, Database, ServerGroup }
      ScopeValue: string
      EnvironmentName: string      // FK to Environments[].Name
      Priority: int                // Higher wins (FR-045)
    }
  ]
}
```

**Defaults**: Three environments pre-populated:
- `Production` (red `#D73A49`)
- `Staging` (orange `#F39C12`)
- `Development` (green `#2ECC71`)

---

## CommandPalette (US4)

```text
CommandPalette: {
  Enabled: bool                    // Default: true
  IncludeAkmlCommands: bool        // Default: true
  IncludeAkmlOptions: bool         // Default: true
  IncludeHostCommands: bool        // Default: true
  IncludeDbObjects: bool           // Default: true (SSMS only)
  MaxRecentItems: int              // Default: 10
  RecentItems: [string]            // Per-host history (FR-052)
}
```

---

## Ai (US10, US18)

```text
Ai: {
  Enabled: bool                    // Master switch (default: false)
  OpenChatShortcut: string         // Default: "Alt+Z"
  FixShortcut: string              // Default: "Shift+Alt+R"
  OptimizeShortcut: string         // Default: "Ctrl+Alt+Z"
  GhostTextShortcut: string        // Default: "Ctrl+Alt+Up"
  EnableExplainSql: bool           // Default: true
  EnableQueryIndexAnalysis: bool   // Default: true
  EnableCommentToSql: bool         // Default: true
  EnableFixOnError: bool           // Default: true
  ShowEditorIcon: bool             // Default: true (orange selection icon)
  ShowFollowupSuggestions: bool    // Default: true
  GhostTextDelayMs: int            // Default: 500
  CommentTriggerPrefix: string     // Default: "-- generate:"
}
```

---

## CompletionPolish (US2, US8, US19)

```text
CompletionPolish: {
  // US19 — toggle and refresh
  // SuggestionsSuppressed is runtime-only (not persisted) — see Key Entities

  // US19 — commit keys
  CommitKeys: [enum { Tab, Enter, Space, Dot, Comma, OpenParen }]
                                   // Default: [Tab, Enter]

  // US19 — category filter
  EnableCategoryFilter: bool       // Default: true

  // US19 — tooltips
  EnableMsDescription: bool        // Default: true
  EnableParameterHighlight: bool   // Default: true

  // US19 — encrypted decryption
  EnableEncryptedDecryption: bool  // Default: true (DAC required)

  // US19 — temp tables
  EnableTempTableIntellisense: bool // Default: true

  // US19 — customizable templates
  AlterTableTemplate: string?      // Null = use built-in default
  InsertIntoTemplate: string?      // Null = use built-in default

  // US2 / US8 — column picker + object definition box
  ObjectDefinitionBoxSize: { Width: double, Height: double }  // Default 400x300
  ColumnPickerDefaultSort: enum { TableOrder, Alphabetical }  // Default: TableOrder
}
```

---

## ResultGrid (US16)

```text
ResultGrid: {
  EnableCopyAsInClause: bool       // Default: true
  EnableScriptAsInsert: bool       // Default: true
  EnableOpenInExcel: bool          // Default: true
  OpenInExcelPreservePrecision: bool  // Default: true (FR-077)
  ScriptAsInsertIncludesIdentity: bool // Default: false (opt-in per edge case)
}
```

---

## Lightbulbs (US17)

```text
Lightbulbs: {
  Enabled: bool                    // Master switch (default: true)
  ShowAdvisoryHints: bool          // Default: true (blue lightbulbs)
  EnableApplyFixForRules: [string] // Default: all 27 known auto-fixable rule ids
                                    // Empty = enable all; explicit list = enable only those
  ApplyFixOnAllOccurrencesShortcut: string  // Default: "Shift+Click"
}
```

---

## Navigation (US13, US20)

```text
Navigation: {
  EnableF12ScriptAsAlter: bool     // Default: true (FR-062)
  EnableCtrlF12SelectInOe: bool    // Default: true (FR-063)
  EnableSummarizeScript: bool      // Default: true (FR-061)
  EnableFindUnused: bool           // Default: true (FR-064)
  EnableExecuteCurrentBatch: bool  // Default: true (FR-101)
  EnableExecuteToCursor: bool      // Default: true (FR-102)
  EnableBrowseOpenTabs: bool       // Default: true (FR-105)
  BrowseOpenTabsShortcut: string   // Default: "Ctrl+Q"
}
```

---

## Validation rules

Every new section MUST:
1. Have a `bool Enabled` toggle if the entire section can be disabled.
2. Use enum types for all multi-choice fields (no magic strings).
3. Round-trip cleanly through `ConfigManager.Save → ConfigManager.Load` without data loss.
4. Be tested by an entry in `tests/AkmlSql.Core.Tests/Config/AppSettingsTests.cs`.
5. Be searchable from the Options dialog by display label and description (FR-059) — every property MUST have a `[Description]` attribute.

## Migration

The new sections are additive. Older `config.json` files (without these sections) MUST load without error and the engine MUST populate the new sections from defaults on first save. The existing `ConfigManager.Load()` already does this for unknown nested sections; spec 014 changes nothing in `ConfigManager` itself.

## Search index (FR-059)

Every property in every new section is indexed for the Options search box by:
1. The property's `[Description]` attribute text.
2. The property name (camelCase split into words).
3. The section name.

Reflection over `AppSettings` produces this index at Options-window-open time. No persistence.
