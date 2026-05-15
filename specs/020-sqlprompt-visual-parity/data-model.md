# Phase 1 — Data Model: SQL Prompt Visual Parity + Format Gap Closure

**Feature**: `020-sqlprompt-visual-parity`
**Date**: 2026-05-13

This document captures every entity introduced or modified by the feature, with fields, relationships, validation rules, and state transitions. Entity definitions in this doc are the canonical contract — `data-model.md` is source-of-truth for the type shapes that flow through code generation, contracts, and tests.

---

## Entity map (overview)

```text
ThemeToken ──belongs-to──> TokenCategory
   │
   └─consumed-by─> Surface ──validated-against──> VisualReference

ThemeVariant (enum)
   │
   └─selected-by─> HostThemeWatcher ──notifies──> ThemeRegistry ──reads──> ThemeToken

FormatStyle ──has-many──> SettingValue
   │
   ├─is-a──> Native | SqlPromptImported (kind)
   ├─persisted-as─> StyleFile (.akmlstyle | .sqlpromptstyle)
   └─round-trips-via──> SqlPromptKeyMap

FormatSettingSchema ──has-many──> FormatSettingGroup ──has-many──> FormatSetting
   │
   └─consumed-by─> FormatStylesEditorWindow (via RequestStyleEditorSchema IPC)
```

---

## ThemeToken

A named design value with one variant per theme. All chrome surfaces resolve their visual properties by token name through `ThemeRegistry`.

| Field | Type | Notes |
|---|---|---|
| `Name` | `string` | E.g. `"Akml.Brush.IconBadge.Table"`. Matches `ThemeTokens` const value. |
| `Category` | `TokenCategory` enum | `Surface`, `Text`, `Border`, `Accent`, `Status`, `Editor`, `Chat`, `IconBadge`, `TabColor`, `History`, `Spacing`, `Typography` |
| `Type` | `TokenType` enum | `Brush`, `Color`, `Font`, `Spacing` |
| `LightValue` | `string` | Hex (`#RRGGBB[AA]`) for `Brush`/`Color`, font descriptor for `Font`, DIU number for `Spacing` |
| `DarkValue` | `string` | As above, dark variant |
| `Description` | `string` | Optional; surfaces in tooling but not in UI |

**Validation**:
- `Name` must start with `"Akml."`.
- For `Type == Brush|Color`: `LightValue` and `DarkValue` must match `^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$`.
- For `Type == Spacing`: values must be positive integers, multiples of 2.

**State**: immutable per build. Tokens are declared as `const string` keys (already in `ThemeTokens.cs`) and bound in `ThemeRegistry`'s light + dark resource dictionaries.

---

## TokenCategory (enum)

```text
Surface, Text, Border, Accent, Status, Editor, Chat, IconBadge, TabColor, History, Spacing, Typography
```

Categories are how the SC-001 scanner partitions its allow-list: any hex literal in source code outside `Status` (semantic colours) fails the gate.

---

## ThemeVariant (enum)

```text
Light, Dark
```

Selected per host by `HostThemeWatcher`. `ThemeRegistry` swaps its active resource dictionary based on this value; all surfaces that bind via `{DynamicResource Akml.Brush.…}` repaint automatically.

---

## Surface

A visible AKML-SQL UI element with declared token dependencies. Used by the SC-001 scanner and the screenshot review process.

| Field | Type | Notes |
|---|---|---|
| `Name` | `string` | E.g. `"SuggestionPopup"`, `"OptionsDialog"`, `"FormatStylesEditor"` |
| `RequiredTokens` | `string[]` | List of `ThemeToken.Name` the surface consumes |
| `VisualReferencePath` | `string` | Relative path to the design reference: `doc/SQL-PROMPT/…/<file>.md#section` |
| `ExpectedDimensions` | `{ width: int?, height: int?, minWidth: int?, minHeight: int? }` | DIU |
| `DpiAuditedAt` | `int[]` | Which DPI percentages have been verified (100, 125, 150, 200) |

**Validation**:
- Every `Surface` listed in the FR-005..FR-014 enumeration must have a `VisualReferencePath` that resolves to an existing document and section.

---

## FormatStyle

A named bundle of formatting settings. Two flavours: Native (created in AKML's editor) and SqlPromptImported (loaded from `.sqlpromptstyle`).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Stable identifier; survives renames |
| `Name` | `string` | Display name; from `metadata.name` on import |
| `Kind` | `FormatStyleKind` enum | `Native`, `SqlPromptImported` |
| `IsReadOnly` | `bool` | Built-in Redgate-style defaults are read-only. Per FR-027a, AKML ships **3 seeded built-ins** (Compact, Indented, AlignedLeftBracket — transcribed from SQL Prompt's documented defaults, never the Redgate-authored binaries). Seeded by `BuiltInStyleSeeder` at engine startup. |
| `IsActive` | `bool` | Exactly one style is active **globally per user** (FR-027b) — shared across SSMS 20/21/22 + VS 2019/22/26. Backed by single string `AppSettings.FormatterSettings.ActiveProfile`. Never split per-host or per-connection. |
| `SourceFilePath` | `string?` | For SqlPromptImported: the absolute path the file was imported from (used in "Re-import from source" action). Null for Native styles created in-app. |
| `SettingValues` | `Dictionary<string, object>` | Keyed by `FormatSetting.Id`. Values are typed per the setting (`bool`, `int`, `string` (enum)) |
| `PassthroughUnknownKeys` | `Dictionary<string, JsonElement>` | Round-trip preservation — keys not mapped by `SqlPromptKeyMap`, kept at their original JSON paths |
| `CreatedAt` | `DateTimeOffset` | |
| `LastModifiedAt` | `DateTimeOffset` | |

**State transitions**:

- `Kind` is fixed at creation. Importing a `.sqlpromptstyle` creates a `SqlPromptImported` style; "Create" / "Copy" in the editor creates a `Native` style. Re-importing a `.sqlpromptstyle` over an existing style replaces all `SettingValues` and `PassthroughUnknownKeys`.
- `IsReadOnly == true` blocks any mutation. The editor must call "Fork to Native copy" first, which copies the entire state into a new `Native` style with `IsReadOnly = false`.
- `IsActive` toggles on selection in the style list; setting one active clears the previous.
- A `SqlPromptImported` style can be exported back to `.sqlpromptstyle` losslessly because all knowns map and all unknowns are in `PassthroughUnknownKeys`.

**Validation**:
- `Name` is unique within the user's style list (case-insensitive).
- `SettingValues[FormatSetting.Id]` must satisfy `FormatSetting.Type` and (for numeric / enum) `FormatSetting.AllowedRange`.

---

## FormatSetting

A single configurable option exposed in the editor and the `.sqlpromptstyle` schema.

| Field | Type | Notes |
|---|---|---|
| `Id` | `string` | E.g. `"casing.reservedKeywords"` — same form as the SQL Prompt JSON path |
| `Group` | `string` | E.g. `"Global › Casing"` — drives the editor tree |
| `Name` | `string` | Display name, e.g. `"Reserved keywords"` |
| `Type` | `SettingType` enum | `Bool`, `Enum`, `Int`, `IntRange` |
| `Default` | `object` | Typed per `Type` |
| `AllowedRange` | `{ min: int?, max: int? }` or `string[]` (enum values) | Bounds |
| `SqlPromptKey` | `string?` | JSON path in `.sqlpromptstyle`, e.g. `"casing.reservedKeywords"`. Null if AKML-only |
| `AkmlField` | `string` | Dotted path inside `FormatProfile` |
| `Transform` | `TransformKind` enum | `Identity`, `EnumNameNormalise`, `BoolToEnum` (e.g. `placeCommasBeforeItems` bool ↔ `CommaPlacement` enum), `Custom` (named function) |
| `Status` | `SupportStatus` enum | `Implemented`, `GapToImplement`, `Unsupported` |
| `ImplementedSince` | `string?` | Phase / spec where this setting was added to the pipeline (for traceability) |

**Validation**:
- `Id` is unique.
- For `Type == Enum`: `Default` must be in `AllowedRange` (string list).
- For `Type == Int|IntRange`: `Default` must satisfy `AllowedRange.min ≤ Default ≤ AllowedRange.max`.
- For `Status == Unsupported`: import populates `PassthroughUnknownKeys`; the editor renders the setting **at its natural tree-group location** with `IsEnabled = false`, the imported value shown, and an "Unsupported" badge adjacent to the disabled control (FR-023, Q4 clarification). No separate "Settings not yet supported" bottom panel.

---

## FormatSettingSchema (root)

The canonical descriptor consumed by `FormatStylesEditorWindow` via the `RequestStyleEditorSchema` IPC. The schema is built once in the engine from the `SqlPromptKeyMap` plus AKML-native settings, and returned to any shell that requests it.

| Field | Type | Notes |
|---|---|---|
| `SchemaVersion` | `int` | Bumps when groupings change |
| `Groups` | `FormatSettingGroup[]` | Ordered for tree display |

```text
FormatSettingGroup
├── Id            (e.g. "global.casing")
├── DisplayName   (e.g. "Casing")
├── Parent        (e.g. "global"  — null for top-level)
└── Settings      FormatSetting[]
```

The editor renders the tree from this schema; the importer / exporter walks the same schema for round-trip safety.

---

## SqlPromptStyleMapping (full table)

Generated from `SqlPromptKeyMap.cs`. Truncated preview shown here — the full table is the authoritative declaration in code and is enforced by `SqlPromptKeyMapTests`.

| SqlPromptKey | AkmlField | Type | Transform | Status |
|---|---|---|---|---|
| `metadata.id` | `FormatProfile.Id` | Guid | Identity | Implemented |
| `metadata.name` | `FormatProfile.Name` | string | Identity | Implemented |
| `whitespace.newLines.preserveExistingEmptyLinesAfterBatchSeparator` | `Whitespace.PreserveEmptyLinesAfterBatch` | bool | Identity | GapToImplement |
| `lists.alignItemsAcrossClauses` | `Lists.AlignAcrossClauses` | bool | Identity | GapToImplement |
| `lists.alignAliases` | `Lists.AlignAliases` | bool | Identity | Implemented |
| `lists.placeCommasBeforeItems` | `Lists.CommaPlacement` | enum (`Leading`/`Trailing`) | BoolToEnum | Implemented |
| `lists.addSpaceAfterComma` | `Lists.AddSpaceAfterComma` | bool | Identity | Implemented |
| `parentheses.collapseShortParenthesisContents` | `Parens.CollapseShort` | bool | Identity | Implemented |
| `parentheses.collapseParenthesesShorterThan` | `Parens.CollapseThreshold` | int (20–120) | Identity | GapToImplement |
| `casing.reservedKeywords` | `Casing.ReservedKeywords` | enum | EnumNameNormalise | Implemented |
| `casing.builtInFunctions` | `Casing.BuiltInFunctions` | enum | EnumNameNormalise | Implemented |
| `casing.builtInDataTypes` | `Casing.BuiltInDataTypes` | enum | EnumNameNormalise | Implemented |
| `casing.globalVariables` | `Casing.GlobalVariables` | enum | EnumNameNormalise | Implemented |
| `casing.useObjectDefinitionCase` | — | bool | — | **Unsupported** |
| `dml.collapseShortStatements` | `Dml.CollapseShortStatements` | bool | Identity | GapToImplement |
| `dml.collapseStatementsShorterThan` | `Dml.CollapseThreshold` | int (20–120) | Identity | GapToImplement |
| `dml.collapseShortSubqueries` | `Dml.CollapseShortSubqueries` | bool | Identity | GapToImplement |
| `dml.collapseSubqueriesShorterThan` | `Dml.CollapseSubqueryThreshold` | int (20–200) | Identity | GapToImplement |
| `ddl.alignDataTypesAndConstraints` | `Ddl.AlignDataTypes` | bool | Identity | GapToImplement |
| `ddl.placeFirstProcedureParameterOnNewLine` | `Ddl.FirstParamOnNewLine` | enum (`Always`/`Never`/`IfLongerThanWrap`) | EnumNameNormalise | GapToImplement |
| `ddl.collapseShortStatements` | `Ddl.CollapseShortStatements` | bool | Identity | GapToImplement |
| `ddl.collapseStatementsShorterThan` | `Ddl.CollapseThreshold` | int (20–120) | Identity | GapToImplement |
| `controlFlow.collapseStatementsShorterThan` | `ControlFlow.CollapseThreshold` | int (20–200) | Identity | GapToImplement |
| `cte.placeColumnsOnNewLine` | `Cte.PlaceColumnsOnNewLine` | enum | EnumNameNormalise | GapToImplement |
| `joins.joinKeywordAlignment` | `Joins.KeywordAlignment` | enum (`ToTable`/`ToFrom`/`IndentedFromFrom`/`RightAligned`) | EnumNameNormalise | GapToImplement |
| `joins.placeOnConditionOnNewLine` | `Joins.OnOnNewLine` | bool | Identity | Implemented |
| `caseExpressions.placeFirstWhenOnNewLine` | `Case.FirstWhenOnNewLine` | enum | EnumNameNormalise | GapToImplement |
| `caseExpressions.whenAlignment` | `Case.WhenAlignment` | enum (`ToCase`/`ToFirstItem`/`IndentedFromCase`) | EnumNameNormalise | GapToImplement |
| `caseExpressions.placeThenOnNewLine` | `Case.ThenOnNewLine` | bool | Identity | Implemented |
| `caseExpressions.placeExpressionOnNewLine` | `Case.ExpressionOnNewLine` | bool | Identity | GapToImplement |
| `operators.alignment` | `Operators.Alignment` | enum (`InlineWithStatement`/`IndentedFromStatement`/`RightAligned`) | EnumNameNormalise | GapToImplement |
| `operators.placeBetweenKeywordOnNewLine` | `Operators.BetweenOnNewLine` | bool | Identity | GapToImplement |
| `inStatements.alignment` | `InStatements.Alignment` | enum | EnumNameNormalise | GapToImplement |

Total: 33 mapped + 1 unsupported (other JSON keys may exist in future versions — captured by `PassthroughUnknownKeys`).

---

## StyleFile

| Field | Type | Notes |
|---|---|---|
| `Path` | `string` | Absolute, validated via `Path.GetFullPath` |
| `Extension` | `string` | `.akmlstyle` (native) or `.sqlpromptstyle` (imported / exported) |
| `SchemaVersion` | `int` | Read from `metadata.schemaVersion` if present; defaults to 1 |
| `RawJson` | `JsonElement` | Cached parse for round-trip |

**Validation**:
- Max file size: 1 MB (FR-022 / security envelope).
- JSON must parse; root must be object.
- For `.sqlpromptstyle`: `metadata.name` must be a non-empty string.

---

## VisualReference

Per-surface design contract. Used by the SC-003 reviewer process.

| Field | Type | Notes |
|---|---|---|
| `SurfaceName` | `string` | Matches a `Surface.Name` |
| `DocPath` | `string` | `doc/SQL-PROMPT/…/file.md#section` |
| `SvgMockupPath` | `string?` | If a `.svg` mockup exists in the same folder |
| `ColorTable` | `ColorBinding[]` | Each entry: element name, light hex, dark hex, target token |
| `DimensionTable` | `DimensionBinding[]` | Each entry: element name, width, height, min, max |

**Relationships**:
- Every `Surface` listed in FR-005..FR-014 has exactly one `VisualReference`.

---

## Persistence layout

```text
%AppData%\AKML SQL\
├── config.json                            # AppSettings (FormatterSettings, theme prefs, etc.)
├── themeMigration.v1.json                 # Migration marker (R6)
├── styles\
│   ├── Default.akmlstyle                  # Built-in Native (AKML-authored default)
│   ├── Compact.akmlstyle                  # Built-in Native — read-only, transcribed from SQL Prompt's "Compact" (FR-027a)
│   ├── Indented.akmlstyle                 # Built-in Native — read-only, transcribed from SQL Prompt's "Indented" (FR-027a)
│   ├── AlignedLeftBracket.akmlstyle       # Built-in Native — read-only, transcribed from SQL Prompt's "AlignedLeftBracket" (FR-027a)
│   ├── *.akmlstyle                        # User-created Native styles
│   └── imported\
│       └── *.sqlpromptstyle               # SqlPromptImported styles, kept in original schema
└── logs\
    └── akmlsql-*.log
```

Atomic writes everywhere: temp file + rename.

---

## Type cross-references

The `FormatProfile` POCO (engine-side) gains the following new sub-objects to host the gap fields:

```text
FormatProfile {
  Id : Guid
  Name : string
  Whitespace : WhitespaceSettings
  Lists : ListSettings
  Parens : ParensSettings
  Casing : CasingSettings
  Dml : DmlSettings
  Ddl : DdlSettings
  ControlFlow : ControlFlowSettings
  Cte : CteSettings
  Joins : JoinSettings
  Case : CaseSettings
  Operators : OperatorSettings
  InStatements : InStatementSettings
  PassthroughUnknownKeys : Dictionary<string, JsonElement>   // [JsonExtensionData] per-section
}
```

Each sub-settings POCO is decorated with `[JsonPropertyName]` matching the `.sqlpromptstyle` JSON shape so the same POCO can be serialised in both schemas (AKML's own `.akmlstyle` adds an `_akml: { … }` envelope; `.sqlpromptstyle` emits the bare structure).

---

## Validation summary (cross-cutting)

| Rule | Source | Where enforced |
|---|---|---|
| Token names start with `Akml.` | R1 / SC-001 | `HardcodedHexScannerTests` |
| All FR-005..FR-014 surfaces have a `VisualReference` | Spec | `VisualReferenceCoverageTests` |
| Every mapped `SqlPromptKey` has a defined `Default` in AKML | R2 | `SqlPromptKeyMapTests` |
| Round-trip preserves unknown keys | R8 / FR-024 | `SqlPromptStyleExporterTests` |
| Style file ≤ 1 MB | Security | `SqlPromptStyleImporter` boundary check |
| Path canonical | Security / CLAUDE.md | `Path.GetFullPath` before `File.Open` |
| Only one style is active at a time | FormatStyle invariant | `FormatStylesEditorViewModel` |
| Read-only styles cannot be mutated | FormatStyle invariant | `FormatStylesEditorViewModel` |
