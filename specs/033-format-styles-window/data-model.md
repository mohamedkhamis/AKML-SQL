# Data Model: Format Styles Window Promotion (spec 033)

## 1. `SettingMeta` attribute (new — `src/AkmlSql.Formatting/Profiles/SettingMetaAttribute.cs`)

Declarative metadata on `FormattingProfile` sub-category POCO properties; the single source the schema builder reads.

| Field | Type | Rules |
|---|---|---|
| `Description` | `string` | Required on every settable property (schema test enforces non-empty). One sentence, user-facing. |
| `AllowedValues` | `string[]?` | Required for enum-like string properties (~60). Entries are the **exact stored spellings** (`"UPPERCASE"`, `"trailing"`, …); must contain the property's default. Null for bool/int/free-string. |
| `Min` / `Max` | `int` | Meaningful for the 14 ranged ints (`tabSize`, `maxLineWidth`, 6× `collapseThreshold`, `subqueryCollapseThreshold`, `inListThreshold`, `emptyLineBetweenStatements`, `maxConsecutiveEmptyLines`, `emptyLinesAfterBatchSeparator`, `blankLinesBeforeGoCount` 0–5). Sentinel (e.g. `int.MinValue`) = unset. Invariant: `Min ≤ Default ≤ Max`. |

`AttributeUsage(Property)`. Read once per property in `FormatSettingSchema.BuildDefault()`'s inner loop via `GetCustomAttribute<SettingMetaAttribute>()`.

## 2. `FormatSettingSchema` v2 (population changes only — DTO shape already declared)

| Field | v1 | v2 |
|---|---|---|
| `SchemaVersion` | literal `1` (`FormatSettingSchema.cs:56`) | literal `2` — invalidates engine Lazy cache consumers + shell static cache automatically |
| `FormatSettingGroup.ParentId` | always null | one of `"global"`, `"statements"`, `"clauses"`, `"expressions"`, `"other"` on every one of the 18 group rows. **Categories are NOT emitted as group rows** (old shells would render empty nodes). |
| `FormatSetting.AllowedEnumValues` | always null | populated from `SettingMeta.AllowedValues` for enum-like strings |
| `FormatSetting.Description` | always null | populated for every setting |
| `FormatSetting.Min`/`Max` | always null | populated for ranged ints |
| `insertStatements` group | `columns`/`values` = opaque `"Other"` blobs | flattened to 6 multi-segment ids: `insertStatements.columns.{parenthesisStyle,indentContents,placeSubsequentItemsOnNewLines}` + same under `.values.` — with matching `ExplicitKeyMap` entries |

**Category map** (static, in `FormatSettingSchema`; unmapped future group → `"other"` + failing schema test):

| Category id | Display (shell map) | Groups |
|---|---|---|
| `global` | Global | whitespace, list, parenthesis, casing |
| `statements` | Statements | dml, ddl, cte, controlFlow, declare |
| `clauses` | Clauses | join, insertStatements |
| `expressions` | Expressions | case, operators, inStatements, functionCalls, expression |
| `other` | Other | comments, formatActions |

**Invariants**: group ids and the `"{groupId}.{jsonName}"` setting-id format stay byte-identical (SqlPromptKey `ExplicitKeyMap` keys on them); `Metadata`/`ExtensionData` remain excluded; wire transport unchanged (schema rides as JSON string in `StyleEditorSchemaResponse.SchemaJson`).

## 3. New IPC DTOs (`src/AkmlSql.Core/Ipc/Messages/`, MessagePack, one class per file)

### `ProfileGetRequest` (msg **34**) / `ProfileGetResponse` (result **134**)

| DTO | Key | Field | Notes |
|---|---|---|---|
| Request | 0 | `Name` string | profile display name (List semantics, OrdinalIgnoreCase) |
| Response | 0 | `Success` bool | false + error when name unknown — nothing created |
| | 1 | `ErrorMessage` string? | |
| | 2 | `Name` string? | resolved display name |
| | 3 | `ProfileJson` string? | **raw file text verbatim** (no re-serialization — preserves `metadata.modified`, nested unknown fields, formatting) |
| | 4 | `IsBuiltIn` bool | derived from resolving directory (built-in dir AND no custom shadow) — never from the JSON `isBuiltIn` field |

### `ProfileRenameRequest` (msg **35**) / `ProfileRenameResponse` (result **135**)

| DTO | Key | Field | Notes |
|---|---|---|---|
| Request | 0 | `OldName` string | must resolve to a **custom** profile |
| | 1 | `NewName` string | sanitized via `ProfileManager.SanitizeFileName`; collision-checked OrdinalIgnoreCase vs customs + built-ins; case-only rename allowed |
| Response | 0 | `Success` bool | |
| | 1 | `ErrorMessage` string? | |
| | 2 | `NewName` string? | final (sanitized) name |

**Engine rename transaction**: read raw → rewrite `metadata.name` (+`modified`) → atomic write new file → delete old → move `<old>.source.json` sidecar → respond. Active-profile config update is the **shell caller's** responsibility (config is shell-owned).

## 4. `ProfileManager` additions (`src/AkmlSql.Formatting/Profiles/ProfileManager.cs`)

- `bool TryReadRaw(string name, out string json, out bool isBuiltIn)` — custom-first probe identical to `Load()`, returns verbatim file text; `isBuiltIn` = resolved-from-built-in-dir AND no custom shadow.
- `string Rename(string oldName, string newName)` — the transaction above; throws on built-in source, collision, invalid name (mirrors `Save`/`Delete` error style).

## 5. ViewModel state machine (`FormatStylesEditorViewModel`)

New/changed state:

| Member | Type | Purpose |
|---|---|---|
| `_loadedProfileJson` | `string?` | raw ProfileGet text for the selected style — the merge base for Save |
| `_loadedProfileName` | `string?` | which style the working values belong to |
| `IsDirty` | `bool` (notify) | any `SetWorkingValue` since load; cleared on load/save; gates Save button + switch/close prompts |
| `IsSelectedReadOnly` | `bool` (notify) | from ProfileGet `IsBuiltIn`; disables controls + Save, shows "Copy this style to edit" |
| IPC seam | delegate/interface | default = `EngineLifecycle.Manager?.Client`; injectable fake for tests (R7) |
| `StyleListItem.IsActive` | `bool` | computed shell-side from `AppSettings.Formatter.ActiveProfile` (not on the wire); drives the ✔ marker; recomputed on Set Active / rename / list reload |

**Selection transition** (from `SelectedProfileName` setter or a guarded wrapper the window calls):

```
[dirty?] --yes--> prompt Save/Discard/Cancel
   Cancel  -> restore previous list selection, no state change
   Save    -> merge-save current; on failure stay (status bar), on success continue
   Discard -> continue
[continue] -> ProfileGet(newName)
   ok      -> seed working values = schema defaults overlaid with profile values;
              _loadedProfileJson/_loadedProfileName set; IsDirty=false;
              IsSelectedReadOnly from response; QueuePreviewAsync()
   fail    -> status bar error; refresh list; clear selection (never silently show defaults as if they were the style)
```

**Merge-save** (`ProfileJsonMerger.Merge(baseJson, workingValues)` — pure internal static):
parse base with `JsonNode` → for each working value whose effective value differs from the base, write by full dotted path (multi-segment nesting for `insertStatements.columns.*`) → return `ToJsonString()`. Preserves `metadata`, root `ExtensionData`, unknown nested keys untouched by edits. Sent via existing `ProfileSave` (identity = JSON `metadata.name`).

**Working-value seeding change**: v1 seeds schema defaults only; v2 seeds schema defaults **then overlays the loaded profile's values** (flattened by the same dotted-path convention), so `GetWorkingValue` reflects the style, and preview/BuildProfileJson keep functioning during the transition.

## 6. Options page (`FormattingPage`) — no schema change

Page keeps `formatter.*` bindings as-is. New behavior only: "Edit formatting styles…" button (modal `Launch()`; on return re-read `ConfigManager.Load().Formatter.ActiveProfile`, re-seed combo, `PopulateProfilesAsync(active)`) — this is also the fix for the OK-path ActiveProfile clobber. "Behavior" group header re-labels the existing toggle sections.

## 7. Deletions

`Ui/ProfileEditorDialog.cs`, `Ui/ProfileEditorViewModel.cs`, `Ui/OptionCategoryTreeBuilder.cs`, `Ui/SqlPreviewRenderer.cs`, `Commands/EditProfileCommand.cs` (+ projitems lines 114–118), `CmdEditProfile` const, palette entry + switch arm, `see cref` at `FormatStylesEditorWindow.cs:32`. No data migration — the legacy dialog persisted nothing of its own.
