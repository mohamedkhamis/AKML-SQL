# Contract: AppSettings Extensions

This contract catalogs every new `AppSettings` property added by Phase 10, with its JSON key, .NET type, default value, owning nested settings class, owning user story, and the spec FR or PRD section that justifies it. Properties added by earlier specs (010-016) are not listed.

## Persistence

All settings persist to `%AppData%\AKML SQL\config.json` via `ConfigManager.Load()` / `SaveAsync()`. Writes are atomic (temp-file + rename pattern). Defaults are applied at load time by `EnsureDefaults()` so a fresh install or an upgrade from a pre-Phase-10 `config.json` does not require user action.

## File Split (US14 FR-081)

Phase 10 splits `src/AkmlSql.Core/Config/AppSettings.cs` (currently 961 lines, 19 nested classes) into per-domain sibling files under `src/AkmlSql.Core/Config/`. The root `AppSettings.cs` ends up < 200 lines containing only the top-level properties and `EnsureDefaults()`. JSON shape on disk does NOT change — only physical file organisation.

## Property Additions

### `IntelliSenseSettings` (`config.json` → `intelliSense`)

| JSON key | .NET type | Default | Owning story | Spec ref |
|---|---|---|---|---|
| `columnPickerEnabled` | `bool` | `true` | US2 | FR-007 |
| `columnPickerSortMode` | `"TableOrder" \| "Alphabetical"` | `"TableOrder"` | US2 | FR-008 |
| `wildcardTabExpansionEnabled` | `bool` | `true` | US2 | FR-011 |

### `CompletionPolishSettings` (`config.json` → `completionPolish`)

CompletionPolish already exists as a section (shipped by spec 014 Phase 2 T006). Phase 10 extends it with the following.

| JSON key | .NET type | Default | Owning story | Spec ref |
|---|---|---|---|---|
| `toggleSuggestionsShortcut` | `string` | `"Ctrl+Shift+P"` | US11 | FR-047 |
| `commitKeys` | `string[]` | `["Tab", "Enter"]` | US11 | FR-048 |
| `categoryCycleEnabled` | `bool` | `true` | US11 | FR-049 |
| `showMsDescriptionInTooltip` | `bool` | `true` | US11 | FR-050 |
| `highlightNextParameterInSignature` | `bool` | `true` | US11 | FR-051 |
| `decryptEncryptedObjectsWithDac` | `bool` | `true` | US11 | FR-052 |
| `tempTableIntelliSenseEnabled` | `bool` | `true` | US11 | FR-053 |
| `objectDefinitionBoxSize` | `{ "width": double, "height": double }` | `{ "width": 360, "height": 220 }` | US11 | FR-054 |

### `CodeAnalysisSettings` (`config.json` → `codeAnalysis`)

| JSON key | .NET type | Default | Owning story | Spec ref |
|---|---|---|---|---|
| `issuesWindowEnabled` | `bool` | `true` | US3 | FR-012 |
| `lightbulbDetailsPopupEnabled` | `bool` | `true` | US3 | FR-014 |
| `applyFixOnAllOccurrencesShortcut` | `string` | `"Shift+Enter"` | US3 | spec.md Edge Cases (Apply Fix on multiple identical violations) |

### `TabSettings` (`config.json` → `tabs`)

| JSON key | .NET type | Default | Owning story | Spec ref |
|---|---|---|---|---|
| `rightClickAssignEnabled` | `bool` | `true` | US4 | FR-017 |
| `highContrastWcagClampEnabled` | `bool` | `true` | US4 | FR-019 |

### `NavigationSettings` (`config.json` → `navigation`)

| JSON key | .NET type | Default | Owning story | Spec ref |
|---|---|---|---|---|
| `summarizeScriptEnabled` | `bool` | `true` | US7 | FR-027 |
| `scriptAsAlterOnF12Enabled` | `bool` | `true` | US7 | FR-028 |
| `selectInObjectExplorerEnabled` | `bool` | `true` | US7 | FR-029 |
| `findUnusedVariablesEnabled` | `bool` | `true` | US7 | FR-030 |
| `browseOpenTabsEnabled` | `bool` | `true` | US7 | FR-031 |
| `browseOpenTabsShortcut` | `string` | `"Ctrl+Q"` | US7 | FR-031 |

### `CommandPaletteSettings` (`config.json` → `commandPalette`)

| JSON key | .NET type | Default | Owning story | Spec ref |
|---|---|---|---|---|
| `includeAkmlCommands` | `bool` | `true` | US6 | FR-023 |
| `includeAkmlOptions` | `bool` | `true` | US6 | FR-023 |
| `includeHostCommands` | `bool` | `true` | US6 | FR-023 |
| `includeDatabaseObjects` | `bool` | `true` | US6 | FR-023 |
| `maxRecentItemsPerHost` | `int` | `10` | US6 | FR-024 |
| `recentItems` | `Dictionary<string, List<string>>` | `{}` | US6 | FR-024 |

### `RefactoringSettings` (`config.json` → `refactoring`)

| JSON key | .NET type | Default | Owning story | Spec ref |
|---|---|---|---|---|
| `bracketsToggleShortcut` | `string` | `"Ctrl+B,Ctrl+B"` | US10 | FR-041 |
| `inlineStoredProcedureShortcut` | `string` | `"Ctrl+B,Ctrl+I"` | US10 | FR-041 |
| `encapsulateAsStoredProcedureShortcut` | `string` | `"Ctrl+B,Ctrl+E"` | US10 | FR-041 |
| `smartRenameEnabled` | `bool` | `true` | US10 | FR-042 |
| `smartRenamePreserveExtendedProperties` | `bool` | `true` | US10 | FR-072 |

### `ExecutionProductivitySettings` (`config.json` → `executionProductivity`)

| JSON key | .NET type | Default | Owning story | Spec ref |
|---|---|---|---|---|
| `executeCurrentBatchEnabled` | `bool` | `true` | US10 | FR-044 |
| `executeCurrentBatchShortcut` | `string` | `"Alt+Shift+F5"` | US10 | FR-044 |
| `executeToCursorEnabled` | `bool` | `true` | US10 | FR-045 |
| `executeToCursorShortcut` | `string` | `"Ctrl+Shift+F5"` | US10 | FR-045 |

### `FormatterSettings` (`config.json` → `formatter`)

| JSON key | .NET type | Default | Owning story | Spec ref |
|---|---|---|---|---|
| `disableFormattingForSelectionEnabled` | `bool` | `true` | US11 | FR-056 |

### `AiSettings` (`config.json` → `ai`)

| JSON key | .NET type | Default | Owning story | Spec ref |
|---|---|---|---|---|
| `openPanelShortcut` | `string` | `"Alt+Z"` | US13 | FR-063 |
| `fixSelectionShortcut` | `string` | `"Shift+Alt+R"` | US13 | FR-063 |
| `optimizeSelectionShortcut` | `string` | `"Ctrl+Alt+Z"` | US13 | FR-063 |
| `manualGhostTextShortcut` | `string` | `"Ctrl+Alt+Up"` | US13 | FR-063 |
| `explainSqlEnabled` | `bool` | `true` | US13 | FR-065 |
| `queryIndexAnalysisEnabled` | `bool` | `true` | US13 | FR-066 |
| `autoFixOnErrorEnabled` | `bool` | `true` | US13 | FR-067 |
| `commentToSqlEnabled` | `bool` | `true` | US13 | FR-068 |
| `panelHistoryEnabled` | `bool` | `true` | US13 | FR-069 |
| `selectionIconEnabled` | `bool` | `true` | US13 | FR-070 |
| `followUpSuggestionsEnabled` | `bool` | `true` | US13 | FR-071 |
| `panelHistoryRetentionDays` | `int` | `7` | US13 | inherited from spec 015 retention |

### `GridSettings` (`config.json` → `grid`)

| JSON key | .NET type | Default | Owning story | Spec ref |
|---|---|---|---|---|
| `copyAsInClauseReportNullCount` | `bool` | `true` | US9 | FR-038 |
| `scriptAsInsertPromptIdentityToggle` | `bool` | `true` | US9 | FR-039 |
| `openInExcelWidePrecisionAsText` | `bool` | `true` | US9 | FR-040 |
| `openInExcelWidePrecisionThreshold` | `int` | `15` | US9 | FR-040 |

## Invariants

1. **Backwards-compatible JSON shape**: all property additions are at the leaf level of existing nested sections. No section is renamed or moved. A pre-Phase-10 `config.json` continues to deserialise cleanly, with `EnsureDefaults()` filling in the new keys with the defaults above.
2. **No new top-level sections**: every new property lands inside an existing nested section (e.g., `intelliSense`, `codeAnalysis`, `tabs`). The root `AppSettings` gains no new fields.
3. **Settings search coverage**: every new property MUST be discoverable via the Options dialog search box (spec 014 US12 FR-059). This is satisfied by the `[CommandPaletteEntry(Label, Path)]` attribute being applied to each property's getter (the attribute is consumed by both the Options search index and the Command Palette `AkmlOptionsSource`).
4. **Live re-render**: every new boolean toggle MUST take effect within 1 second of `ConfigManager.SettingsChanged` firing, without restart (spec 014 US12 FR-060). Each setting consumer is responsible for subscribing to `SettingsChanged` and re-rendering or re-routing as appropriate.
5. **No new persistence layer**: API keys remain DPAPI-encrypted with the `dpapi:` prefix in the existing `ai.providers[*].apiKey` slot; Phase 10 does not introduce new credential storage.
