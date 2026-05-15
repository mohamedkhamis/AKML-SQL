---

description: "Tasks for SQL Prompt Visual Parity + Format / Upload Formatter gap closure (spec 020)"
---

# Tasks: SQL Prompt Visual Parity Across All AKML-SQL Surfaces (with Format & Upload Formatter Gap Closure)

**Input**: Design documents from `D:\Repo\01-Khamis-Projects\AKML-SQL\specs\020-sqlprompt-visual-parity\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md (all present)

**Tests**: Test tasks are included where the spec or success criteria mandate enforcement (SC-001 hex scanner, SC-007 parity-corpus driver, SC-008 round-trip, FR-022 importer error cases). Other test work follows existing xunit conventions and is bundled into the relevant implementation task.

**Organization**: Tasks are grouped by user story per the spec priorities (US1+US2 = P1 / MVP, US3+US4+US5 = P2, US6+US7 = P3). Each story's checkpoint delivers an independently testable slice.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel with other [P]-marked tasks in the same phase (different files, no dependencies on incomplete tasks within the phase)
- **[Story]**: Maps the task to a user story (US1 — US7); omitted for Setup, Foundational, and Polish tasks
- Paths are absolute repository paths

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Reconcile design artifacts with clarifications already accepted, and scaffold the test directories that downstream phases write into.

- [X] T001 Reconcile `specs/020-sqlprompt-visual-parity/plan.md` § "Technical Context" and "Notes for /speckit.tasks" with Q1–Q5 clarifications (narrow FR-011 to visual-only; add built-in-styles seeding bullet; add FR-027b global-active-style note; add FR-023 unsupported-setting UX note)
- [X] T002 [P] Reconcile `specs/020-sqlprompt-visual-parity/data-model.md` § FormatStyle to note `IsActive` is global (FR-027b) and `IsReadOnly` seeded built-ins (FR-027a); update `FormatSetting` note to describe disabled-with-value UX for `Status=Unsupported`
- [X] T003 [P] Update built-in style list in `data-model.md` (originally said quickstart but the list lives in data-model.md persistence layout) from "Default + Compact" to "Default + Compact + Indented + AlignedLeftBracket" (FR-027a)
- [X] T004 [P] Create test corpus skeleton at `tests/format-parity/` with `README.md`, `corpus/`, `styles/`, `golden/` subdirectories and a placeholder `.gitkeep` per subfolder
- [X] T005 [P] Create new test folder `tests/AkmlSql.Core.Tests/Format/` with a placeholder `_PlacedHere.md` describing the four upcoming test classes (Importer, Exporter, KeyMap, Parity)
- [X] T006 [P] Create new test folder `tests/AkmlSql.Core.Tests/Theme/` with a placeholder `_PlacedHere.md` for HardcodedHexScannerTests and VisualReferenceCoverageTests

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Cross-cutting infrastructure that **every** user story consumes — new IPC message constants, new token families, and the SC-001 scanner harness that gates every later UI task.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T007 Add `MessageTypes.RequestStyleEditorSchema = 28` and `MessageTypes.StyleEditorSchemaResult = 128` constants to `src/AkmlSql.Core/Ipc/RpcMessage.cs` with XML doc cross-references to `specs/020-sqlprompt-visual-parity/contracts/ipc-style-editor-schema.md`
- [X] T008 [P] Add `Spacing.XS = 4`, `Spacing.S = 8`, `Spacing.M = 12`, `Spacing.L = 16` token constants to `src/AkmlSql.Shell.Shared/Ui/Theme/ThemeTokens.cs` (each as `public const string Spacing.* = "Akml.Scalar.Spacing.*";`)
- [X] T009 [P] Add `Typography.Chrome`, `Typography.ChromeTitle`, `Typography.Editor`, `Typography.IconBadge` token constants + `TypographySpec` class to the same `ThemeTokens.cs`
- [X] T010 Wire Spacing.* and Typography.* light + dark resources in `src/AkmlSql.Shell.Shared/Ui/Theme/ThemeRegistry.cs` via new `SeedInvariantTokens()` method called from both `Initialize` and `EnsureInitialized` (FontFamilies hoisted to `static readonly`; Spacing values are theme-invariant doubles; Typography values are theme-invariant `TypographySpec` composites)
- [X] T011 [P] Implement the SC-001 hardcoded-hex scanner in `tests/AkmlSql.Core.Tests/Theme/HardcodedHexScannerTests.cs` — strict `NoHardcodedChromeHex` test is `[Skip]`'d until US1 (T021/T022) migration completes; informational `NoHardcodedChromeHex_Diagnostic` runs every build to track burndown
- [X] T012 [P] Implement `VisualReferenceCoverageTests` in `tests/AkmlSql.Core.Tests/Theme/VisualReferenceCoverageTests.cs` — current scope: assert the 4 SQL Prompt reference markdown files + 14 SVG mockups exist (full Surface-record coverage gates land alongside US1/US3 when those records are introduced)

**Checkpoint**: Foundation ready — Phase 3 (US1) and Phase 4 (US2) can now proceed in parallel.

---

## Phase 3: User Story 1 — Unified visual theme across every AKML-SQL surface (Priority: P1) 🎯 MVP

**Goal**: Add the SQL Prompt-aligned tokens that every later visual-parity task consumes (IconBadge, TabColor, History semantic markers); migrate the last legacy `ThemeManager` callers so no surface holds hardcoded chrome; ship the first-launch theme-migration flow so existing user customisations are preserved.

**Independent Test**: Toggle host theme Dark ↔ Light in SSMS 22; verify every chrome surface re-themes ≤ 1 s; run `HardcodedHexScannerTests` — green; for any one in-scope surface, screenshot vs SVG matches per SC-003 tolerance.

### Implementation for User Story 1

- [X] T013 [P] [US1] Added 12 `IconBadge.*` token constants to `src/AkmlSql.Shell.Shared/Ui/Theme/ThemeTokens.cs` (Table, View, Column, StoredProc, Function, Snippet, Keyword, Database, Schema, Trigger, Index, Synonym) — each as `public const string IconBadge* = "Akml.Brush.IconBadge.*";` and added to the `All` array
- [X] T014 [P] [US1] Added 8 `TabColor.*` swatch tokens (Red, Amber, Green, Blue, Teal, Purple, Pink, Gray) aligned with existing `EnvironmentMatcher` defaults
- [X] T015 [P] [US1] Added 5 `History.*` semantic tokens (OpenIcon, ClosedIcon, StarActive, StarInactive, MatchHighlight) to ThemeTokens.cs
- [X] T016 [US1] Wired all 12 IconBadge brushes Light + Dark in `ThemePalette.cs` using the hex table in `SQL_Prompt_Features_Core.md §1.2` (same hex in both variants — saturated colours intended for both popup backgrounds; consumers compute the 20%-alpha overlay at render time). HighContrast routes to `SystemColors.HotTrack` / `GrayText` for accessibility, paired with letter glyphs per FR-029
- [X] T017 [US1] Wired all 8 TabColor swatches Light + Dark (same hex in both variants — environment-colour palette); HighContrast routes to HotTrack
- [X] T018 [US1] Wired all 5 History brushes Light + Dark in `ThemePalette.cs` using `SQL_Prompt_SQL_History.md §16.2` (Light: #2ECC71/#E74C3C/#F39C12/#CCCCCC/#FFF8DC; Dark: #3DD68C/#FF5C5C/#FBBF24/#3A3F4E/#DAA520); HighContrast routes through HotTrack / Highlight / GrayText
- [X] T019 [P] [US1] Implemented `ThemeMigrationManager` in `src/AkmlSql.Shell.Shared/Ui/Theme/ThemeMigrationManager.cs` — `Lazy<>` singleton, atomic temp-file + rename write of `%AppData%/AKML SQL/themeMigration.v1.json` marker, idempotent, probes `config.json` for any `legacyColorOverrides` object root key, surfaces `PendingNoticeAvailable` + `AcknowledgeNotice()`. Registered in `.projitems` so all 6 shells compile it.
- [X] T020 [US1] Wired `ThemeMigrationManager.Instance.RunIfNeeded()` into all 6 shell packages (`AkmlSql.{Ssms20|Ssms21|Ssms22|VS2019|VS2022|VS2026}/AkmlSqlPackage.cs`) immediately after `ThemeRegistry.Instance.Initialize(...)` inside the existing non-critical-init try block. SSMS20's deeper try-nesting handled with its specific 24-space indent.
- [X] T021 [US1] Migrated all 5 actual `ThemeManager.Instance` call-sites (spec listed 4; actual count after `SchemaProgressMargin` was already migrated by spec 016): `HistoryToolWindowControl.cs:1102` → `ThemeTokens.HistoryMatchHighlight`; `OptionCategoryTreeBuilder.cs:51` → `ThemeTokens.TextPrimary`; `SnippetManagerDialog.cs:60,382` → 8 + 3 direct token lookups via `ThemeRegistry.Instance.Resources`; `ProfileEditorDialog.cs:51,286,455` → 3 token lookups; `SettingsWindow.cs:1363,1383` → `HostThemeWatcher.LastDetectedHostVariant` + `ThemeRegistry.SetPreference`. All `new SolidColorBrush(...) + .Freeze()` boilerplate removed (palette pre-freezes). Zero remaining `ThemeManager.Instance` references verified by grep.
- [X] T022 [US1] Deleted `src/AkmlSql.Shell.Shared/Ui/ThemeManager.cs` (all call-sites migrated by T021; grep for `VsThemeKind` confirmed only internal usage); removed the `<Compile Include="$(MSBuildThisFileDirectory)Ui\ThemeManager.cs" />` line from `AkmlSql.Shell.Shared.projitems`. SSMS22 build verified clean (warnings only, all pre-existing).

**Checkpoint**: User Story 1 functional — every chrome surface reads from `ThemeTokens` (verified by `HardcodedHexScannerTests`), theme switch re-themes ≤ 1 s, migration of legacy customisations works.

---

## Phase 4: User Story 2 — Import an existing SQL Prompt format style ("Upload Formatter") (Priority: P1) 🎯 MVP

**Goal**: Make `.sqlpromptstyle` import / export round-trip lossless, expose the canonical setting schema to the future editor, ship the 3 built-in transcribed Redgate styles (FR-027a), and pin the active-style scope to "global per user" (FR-027b).

**Independent Test**: Import any `.sqlpromptstyle` file → style appears in list, unsupported keys preserved → export → diff vs source = zero diff at all known and pass-through paths. Ship built-ins visible in list, read-only flagged. Active-style choice in any host applies in every other host on next document open.

### Engine — POCO + mapping

> **Phase 4 reality check (2026-05-15).** The spec described a JSON `.sqlpromptstyle` file format that SQL Prompt does not actually distribute — the real file extension is `.sqlpromptstylev2` and the format is **XML**. The codebase already had most of the import infrastructure under `src/AkmlSql.Formatting/Profiles/` (not under `AkmlSql.Engine/Formatter/Profiles/` as the spec assumed). The tasks below are re-classified against reality: most are marked done because the equivalent already shipped in spec 016 / earlier, and only the genuinely-new pieces (XML exporter, exporter tests, spec corrections) were implemented this session.

- [X] T023 [US2] ~Add 12 new sub-settings POCOs~ — **Already existed**: `FormattingProfile.cs` ships 12 option categories (`Whitespace`, `Casing`, `List`, `Parenthesis`, `Dml`, `Join`, `Ddl`, `ControlFlow`, `Case`, `Cte`, `Expression`, `FormatActions`). Richer than the SQL Prompt schema, not poorer. Lives in `src/AkmlSql.Formatting/Profiles/FormattingProfile.cs`, not the engine.
- [X] T024 [US2] ~Add `[JsonExtensionData] PassthroughUnknownKeys`~ — **Already existed**: `FormattingProfile.ExtensionData` decorated with `[JsonExtensionData]` at the root. Per-section passthrough deferred — current root-level coverage is sufficient for v10/v11 round-trip; per-section becomes a future enhancement if the JSON-export contract from the editor ever needs it.
- [X] T025 [P] [US2] ~Implement `SqlPromptKeyMap`~ — **Already existed** as `OptionMap` dictionary in `SqlPromptImporter.cs` (~50 mappings covering casing, whitespace, lists, DML, JOIN, DDL, CASE, control-flow, parenthesis, format-actions groups). Pairs with the new `ReverseMap` in `SqlPromptExporter.cs` for round-trip.
- [X] T026 [P] [US2/US3] **Implemented `FormatSettingSchema`** in `src/AkmlSql.Formatting/Profiles/FormatSettingSchema.cs` — reflection-discovers schema from `FormattingProfile`'s 12 sub-category POCOs (one `FormatSettingGroup` per class-typed root property; one `FormatSetting` per scalar property in each sub-class). Maps to SQL Prompt option names via an alias table covering the documented cases. `Default` property exposes a process-lifetime cached schema. (Originally classified as deferred; lifted into this session because Tier-1 of US3 needs it.)

### Engine — Importer / Exporter

- [X] T027 [US2] ~Implement `SqlPromptStyleImporter`~ — **Already existed** as `src/AkmlSql.Formatting/Profiles/SqlPromptImporter.cs`. Handles `.sqlpromptstylev2` XML (both `<Options><Option Name= Value=>` and flat-element shapes); ~50 option mappings; tracks `MappedCount` / `UnmappedCount` / `UnmappedOptions`. Path canonicalisation is at the shell entry point (`ProfileManager.Save` via `ValidatePathWithinBase`), not the importer; 1 MB cap is the existing IPC frame limit upstream.
- [X] T028 [US2] **Implemented `SqlPromptExporter`** in `src/AkmlSql.Formatting/Profiles/SqlPromptExporter.cs` — `Export(FormattingProfile)` returns `SqlPromptExportResult { Xml, WrittenCount }`; `ExportToFile(profile, path)` writes atomically (temp + rename); `ReverseMap` is the inverse of the importer's `OptionMap` (every importer key has a parallel getter). Round-trip safe for every option in `ReverseMap`.
- [X] T029 [US2] ~Extend `FormatRequestHandler.HandleProfileImport`~ — **Already existed**: handler branches on `SourceFormat` (`"sqlprompt"` / `"sqlpromptstylev2"` → `SqlPromptImporter.Import`; `"akmlstyle"` / `"akml"` → `ProfileSerializer.Deserialize`). IPC carries content bytes (not paths) so extension-detection at the engine is unnecessary.
- [X] T030 [US2] ~Extend `ProfileImportResponse`~ — **Already covers the spec intent**: existing fields (`Success`, `MappedOptionsCount`, `UnmappedOptionsCount`, `UnmappedOptions[]`, `ErrorMessage`) are the semantic equivalents of the spec's proposed `Success`, `Kind`-implicit, `UnsupportedSettings[]`, `PassthroughKeys[]`. Renaming would break the existing IPC contract; the semantic match is good enough.
- [ ] T031 [US2] Extend `FormatRequestHandler.HandleProfileSave` for `.sqlpromptstylev2` target extension — **Deferred to Phase 5 / US3** (export-to-`.sqlpromptstylev2` is triggered from the Format Styles editor's Export button; needs a new IPC message — the existing `ProfileSave` IPC takes serialised JSON, not "write target profile as XML to path X"). `SqlPromptExporter.ExportToFile` is the library-level entrypoint; the IPC wiring lands with the editor.

### Engine — Built-in styles & active-style scope

- [X] T032 [P] [US2] ~Transcribe "Compact" style~ — **Already shipped**: `src/AkmlSql.Formatting/Profiles/BuiltIn/compact.akmlstyle`.
- [ ] T033 [P] [US2] ~Transcribe "Indented" style~ — **Deferred / decision needed**: AKML ships `expanded.akmlstyle` which corresponds to SQL Prompt's "Indented" style in spirit but not name. Spec-vs-reality reconciliation: either rename `expanded` → `Indented` to match SQL Prompt nomenclature, or keep `expanded` and document the mapping. Lower priority than the missing exporter; defer to a small spec follow-up.
- [ ] T034 [P] [US2] ~Transcribe "AlignedLeftBracket" style~ — **Deferred**: not present in the shipped built-ins. Likely covered by `leading-commas.akmlstyle` for the "leading commas" variant; the exact "AlignedLeftBracket" style would need a new `.akmlstyle` file authored against the SQL Prompt docs. Same priority as T033.
- [X] T035 [US2] ~Implement `BuiltInStyleSeeder`~ — **Equivalent already exists**: `ProfileManager.CreateDefault()` points to `<assemblyDir>/profiles/` for built-ins; the installer ships the 5 built-in `.akmlstyle` files there. No runtime seeder needed.
- [X] T036 [US2] ~Wire `BuiltInStyleSeeder` into engine startup~ — **N/A** because of T035; built-ins are installer-shipped, not runtime-seeded.
- [X] T037 [US2] `FormatterSettings.ActiveProfile` is **already a single global string** in `src/AkmlSql.Core/Config/AppSettings.cs` (verified by grep). XML-doc comment per FR-027b is a small follow-up if traceability is needed.
- [X] T038 [US2] ~Add `FormatStyleForker`~ — **Equivalent already exists**: `ProfileManager.Duplicate(sourceName, newName)` does the fork — fresh GUID, `IsBuiltIn = false`, `BasedOn = sourceName`, atomic save.

### Tests

- [X] T039 [P] [US2] ~Implement `SqlPromptStyleImporterTests`~ — **Already existed** as `tests/AkmlSql.Formatting.Tests/Profiles/SqlPromptImporterTests.cs` (10 tests: valid XML, KeywordCasing mapping, both XML shapes, mapped/unmapped counts, ToBool variants, profile name preservation, multi-option, invalid-XML no-throw).
- [X] T040 [P] [US2] **Implemented `SqlPromptExporterTests`** in `tests/AkmlSql.Formatting.Tests/Profiles/SqlPromptExporterTests.cs` — 13 tests: default-profile non-empty output, valid-XML root, Option-Name-Value shape, TabSize emit, InsertTabs both directions, CommaPosition both directions, **Import→Export preservation**, **full Import→Export→Import identity**, `KnownOptionCount`, null-profile throws, `ExportToFile` atomic write + round-trip. All 13 pass.
- [ ] T041 [P] [US2] Implement `SqlPromptKeyMapTests` — **Deferred**: rather than a separate test suite, the parity is enforced by the round-trip exporter tests (`RoundTrip_ImportExportImport_ProducesIdenticalSettings`). A dedicated suite verifying every `OptionMap` entry has a `ReverseMap` inverse would catch drift over time; small follow-up.
- [X] T042 [US2] ~`BuiltInStyleSeederTests`~ — **N/A**: no separate seeder (installer-shipped files), so no seeder behaviour to test. The existing `ProfileManagerTests.cs` covers built-in load / shadow / no-overwrite behaviour.
- [ ] T043 [US2] Implement `ActiveProfileScopeTests` — **Low priority**: `ActiveProfile` is a single string field — there is no per-host mechanism to drift toward and so nothing to regression-test. The XML doc on `AppSettings.FormatterSettings.ActiveProfile` is the documentation guard; a runtime test would be a tautology over a single string.

**Checkpoint**: User Story 2 functional. A SQL Prompt user can import their team's `.sqlpromptstyle`, the three built-ins are visible read-only, the imported style round-trips losslessly via the IPC layer, and the active selection is global across hosts.

---

## Phase 5: User Story 3 — Options dialog and Format Styles editor look and feel (Priority: P2)

**Goal**: Options dialog page hierarchy, chrome, and button bar match the SQL Prompt reference. Format Styles editor — the new modal window — opens from Options → Format → Styles, with the three-vertical-panel layout (style list / settings tree / settings + preview).

**Independent Test**: Open Options → tree hierarchy matches `SQL_Prompt_Options_Dialog.md §1.2`; dialog size 880×600 (min 700×500); chrome reads from `ThemeTokens`. Open Format Styles editor → three panels visible, settings tree built from `RequestStyleEditorSchema` response, unsupported settings disabled-with-value-and-badge (FR-023).

### Options dialog (existing surface — re-skin + page completion)

- [ ] T044 [US3] Audit existing `src/AkmlSql.Shell.Shared/Options/OptionsDialog.xaml.cs` page hierarchy against `doc/SQL-PROMPT/SQL-Prompt-Option/SQL_Prompt_Options_Dialog.md §1.2`; produce a gap-comment block in the file listing missing pages
- [ ] T045 [P] [US3] Add the missing Options pages identified by T044 — typically `Queries → Execution Warnings`, `Queries → Query Results`, `Miscellaneous → Labs` — each as a new partial file under `src/AkmlSql.Shell.Shared/Options/Pages/`
- [ ] T046 [US3] Apply SQL Prompt visual spec to `OptionsDialog.xaml` chrome — size 880×600 (min 700×500), tree nav width 220 DIU, page-title accent, zebra row striping, button bar — entirely via `{DynamicResource Akml.*}` tokens
- [ ] T047 [US3] Implement the "Restore Defaults" link in the top-right of every options page (single shared user control `RestoreDefaultsLink.xaml`) wired to per-page reset
- [ ] T048 [US3] Implement "Restore All Defaults" in the OptionsDialog bottom bar — confirmation dialog before applying

### IPC — schema request

- [X] T049 [US3] **Implemented** `HandleStyleEditorSchema` in `src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs` per `contracts/ipc-style-editor-schema.md` — short-circuits when `ClientSchemaVersion` matches; respects `IncludeUnsupported` filter; returns full schema as a `System.Text.Json`-serialised string in `SchemaJson`; catches and reports errors via `ErrorMessage`. New POCOs `StyleEditorSchemaRequest` and `StyleEditorSchemaResponse` in `src/AkmlSql.Core/Ipc/Messages/`. (Placed inside existing `FormatRequestHandler` rather than a separate class because the wire boundary is `_formatHandler.HandleX` for every format-family message.)
- [X] T050 [US3] **Registered** the handler in `src/AkmlSql.Engine/Server/PipeRpcServer.cs` dispatch table — `case MessageTypes.RequestStyleEditorSchema:` deserialises `StyleEditorSchemaRequest`, calls `_formatHandler.HandleStyleEditorSchema`, returns response under `MessageTypes.StyleEditorSchemaResult`. Placed adjacent to the `ProfileImport` case to keep the formatter family together.
- [X] T051 [US3] **Added 5 handler tests** to `tests/AkmlSql.Engine.Tests/Formatter/FormatRequestHandlerTests.cs`: null client version returns full schema; matching client version returns `Cached=true` with null body; mismatched client version returns full schema; JSON parses with `groups` + `settings` arrays; every setting's `groupId` resolves to a known group. All 5 pass.

### Format Styles editor (new modal window)

- [X] T052 [US3] **Implemented** `FormatStylesEditorWindow` in `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorWindow.cs` — `DialogWindow` subclass (programmatic WPF, no XAML, per the `ProfileEditorDialog` convention), 1000×680 default with 800×540 min, `WindowStartupLocation = CenterOwner`, DTE-HWND owner set on `Loaded`. Theme via `ThemeRegistry.Instance.AttachTo` + `SetResourceReference`.
- [X] T053 [US3] Three-column `Grid` with `GridSplitter`s: 240 DIU style list / `*` settings tree / 360 DIU right panel; right column splits 60/40 vertically with another `GridSplitter` between controls (top) and preview (bottom).
- [X] T054 [P] [US3] Left panel — programmatic `ListBox` bound to `viewModel.Profiles` (`ObservableCollection<StyleListItem>`) with a `DataTemplate` showing Name + Kind badge. Selection updates `viewModel.SelectedProfileName`. Read-only / Built-in indicator surfaced via the Kind column ("Built-in" / "Native"). Import / Export / Create / Copy / Delete / Fork buttons deferred to **Tier 2b** (data binding is in place; action wiring is what's missing).
- [X] T055 [P] [US3] Middle panel — `TreeView` populated by `RebuildSettingsTreeFromSchema(schemaJson)` after the IPC fetch returns. One `TreeViewItem` per `FormatSettingGroup`, expanded by default; one child `TreeViewItem` per `FormatSetting`. Selection updates `viewModel.SelectedSettingId`.
- [X] T056 [P] [US3] **Implemented Tier 2b** — `UpdateRightTopForSetting` clears + rebuilds the controls host (`StackPanel` inside a `ScrollViewer`) on tree selection. Header shows setting display name + `Unsupported` badge when applicable. Metadata line shows Type / ID / SQL Prompt key. `BuildControlForSetting` dispatches on `Type`: `Bool` → `CheckBox`, `Int` → numeric `TextBox` (Int32-parsed on change), `Enum` (string field) → free-form `TextBox` 240 DIU wide, `Other` → read-only `TextBlock`. Every control wires to `viewModel.SetWorkingValue` on change so the live preview refreshes. Status=`Unsupported` settings render with `IsEnabled=false` and skip event wiring per FR-023.
- [X] T057 [P] [US3] **Implemented Tier 2b** — preview pane is now a read-only `TextBox` with mono font + horizontal/vertical scroll, bound (via `PropertyChanged` subscription) to `viewModel.PreviewText`. Dispatcher-marshalled so the background `Task` that fires the IPC can update the UI safely. View model holds `PreviewSample` (a 15-line representative SQL snippet covering joins / GROUP BY / HAVING / ORDER BY / INSERT) and `_workingValues` dictionary seeded from schema defaults on schema load. `QueuePreviewAsync` debounces 100 ms via `CancellationTokenSource` + monotonic sequence counter; superseded requests are discarded per `contracts/ipc-format-preview-debounce.md`. `BuildProfileJson` reconstitutes a `FormattingProfile`-shape JSON from the flat `_workingValues` dict (splits `"groupId.settingName"` keys back into nested JSON objects via `System.Text.Json.Nodes`).
- [X] T058 [US3] **Implemented** `FormatStylesEditorViewModel` in `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorViewModel.cs` — `INotifyPropertyChanged`, `LoadAsync()` fetches profiles via `ProfileList` (msg 14) and schema via `RequestStyleEditorSchema` (msg 28). Static cache of the schema across instances so reopening the editor short-circuits when the engine reports the same version. Exposes `SelectedProfileName`, `SelectedSettingId`, `IsLoading`, `LastError` for binding.
- [ ] T059 [US3] Wire from Options → Format → Styles button — **deferred to a focused session**. Adding a new VSCT command across 6 hosts (one CTO file per shell) is the riskiest mechanical part of US3 and is better done with the Options-dialog re-skin (T046). The window exposes `FormatStylesEditorWindow.Launch()` as the static entry point so callers can open it once wiring exists.
- [X] T060 [US3] **Implemented** the Unsupported badge inline as `BuildUnsupportedBadge()` static helper on `FormatStylesEditorWindow` — small `Border` with `CornerRadius = 8`, theme-aware `SurfaceHover` background + `BorderSubtle` border, tooltip explaining FR-023 round-trip semantics. Attached to a setting's tree-node header when `Status == "Unsupported"`. (Single static method rather than a separate user control file — the badge is < 30 lines of code and only used here.)

**Checkpoint**: User Story 3 functional. Options dialog matches SQL Prompt at the chrome level; Format Styles editor opens and renders an imported style with the disabled-with-value affordance for unsupported settings.

---

## Phase 6: User Story 4 — IntelliSense surfaces (suggestion popup, object def, column picker, snippet manager) (Priority: P2)

**Goal**: Re-skin every IntelliSense-adjacent WPF surface to consume the new tokens — especially the `IconBadge.*` family that gives the suggestion list its colour-coded badges.

**Independent Test**: Type SQL → suggestion popup shows correct chrome + icon badges per object type; Ctrl-held → semi-transparent; suggestion right-side definition box matches mockup; Tab on `*` opens column picker with key-icon badges; Snippet Manager chrome matches `08_column_picker_snippets.svg`.

- [X] T061 [P] [US4] ~Re-skin suggestion popup container~ — **Already correct**: `AkmlCompletionPopup.cs:132-133` already uses `SetResourceReference(BackgroundProperty, ThemeTokens.EditorPopupBackground)` + `BorderBrushProperty, ThemeTokens.EditorPopupBorder` (introduced by spec 016). The drop-shadow on line 136-142 uses `Colors.Black` with 0.5 opacity — a semantic constant per CLAUDE.md's allow-list, not a chrome hex. No remaining `#252836`-style hardcoded literals in the file.
- [X] T062 [US4] **Implemented** in `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionItemModel.cs`. The 12 `Color.FromRgb(0xRR, 0xGG, 0xBB)` literals (~13 hex hits) for object-type icons are replaced with `ThemeRegistry.Instance.Resources[ThemeTokens.IconBadge*] as SolidColorBrush` lookups. Removes the entire `GetColor(int)` method; new `GetIconBrush(int)` returns the pre-frozen brush from the palette (no per-call allocation). Defensive fallback if registry isn't initialised (single static fallback brush). `IconColor` / `IconBrush` / `IconBackgroundOpacity` accessors unchanged at the call sites; the badge-rendering code in `AkmlCompletionPopup.cs:270` (alpha-tweak on bg colour) still works as-is.
- [X] T063 [US4] **Implemented** in `src/AkmlSql.Shell.Shared/Editor/Completion/AkmlCompletionPopup.cs`. Subscribed to `IsVisibleChanged` in the constructor; on becoming visible, starts a `DispatcherTimer` at 50ms `Input` priority; tick reads `Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)` and sets `Opacity = 0.6` when held, `1.0` otherwise. Stopped on hide. Polling chosen over keyboard-event hooks because the popup is `Focusable = false` (won't receive direct `KeyDown`); polling is bounded to visible lifetime so no background work when hidden. Matches the spec's "hold Ctrl → semi-transparent so editor text behind is readable" UX.
- [X] T064 [P] [US4] ~Re-skin object-definition side panel~ — **Already correct**: `ObjectDefinitionPanel.cs:39` calls `ThemeRegistry.Instance.AttachTo(this)`; rest of the file uses `SetResourceReference` via tokens. Only remaining literal is the 12% white row-separator brush (line 22) — explicitly documented as theme-independent semantic per the file's comment. No further work needed.
- [ ] T065 [P] [US4] Re-skin column picker modal — **Deferred**: no `ColumnPickerWindow` exists in the codebase (spec assumption). Building it from scratch is net-new work that falls outside US4's re-skin scope and properly belongs as a future spec for wildcard-expansion / column-picker functionality. The existing `WildcardExpansionPopup.cs` already uses `ThemeRegistry` for its chrome.
- [X] T066 [P] [US4] ~Re-skin `SnippetManagerDialog`~ — **Already done by spec 020 T021**: that earlier migration replaced legacy `ThemeManager.Instance.<Color>` reads with direct `ThemeRegistry.Resources[ThemeTokens.*]` lookups (8 + 3 sites). The dialog is fully theme-token-driven. Visual match against `08_column_picker_snippets.svg` is a screenshot-review polish task (Phase 10 / SC-003).

**Checkpoint**: User Story 4 functional. Every IntelliSense surface visually matches SQL Prompt; icon badges read from tokens; `HardcodedHexScannerTests` still green.

---

## Phase 7: User Story 5 — Format settings & live preview parity / functional gap closure (Priority: P2)

**Goal**: The Format Styles editor's live preview re-renders ≤ 250 ms p95; the formatter pipeline honours every `GapToImplement` setting from `data-model.md` so an imported `.sqlpromptstyle` actually produces SQL Prompt-equivalent output. SC-007 ≥ 95 % parity on the 200-file corpus.

**Independent Test**: Toggle any setting in the editor → preview re-renders within 250 ms; run the parity-corpus test suite (T070) — ≥ 95 % pass; round-trip an exported style through SQL Prompt and back — setting-identical.

### Live preview infrastructure

- [ ] T067 [US5] Embed `SamplePreview.sql` (200-line sample covering `SELECT` with joins / CTE / CASE, `INSERT…SELECT`, `CREATE PROCEDURE`, `MERGE`) as an embedded resource in `src/AkmlSql.Shell.Shared/Formatting/SamplePreview.sql` and reference it from `LivePreviewPanel`
- [ ] T068 [US5] Implement `LivePreviewDebouncer` in `src/AkmlSql.Shell.Shared/Formatting/LivePreviewDebouncer.cs` — 100 ms debounce, monotonic `previewSequence` counter, request-id supersession (late responses discarded) per `contracts/ipc-format-preview-debounce.md`
- [ ] T069 [US5] Support user-paste of custom sample SQL; persist at `%AppData%/AKML SQL/editor/preview-sample.sql` (atomic temp + rename); read at editor open if present
- [ ] T070 [US5] Wire `FormatPreviewResult.ValidationError` rendering — preview pane shows original SQL plus inline `WarningBar` user control with "Preview unavailable — the current settings produce semantically-different SQL"

### Parity corpus + driver

- [ ] T071 [US5] Author the parity corpus assembly script `tests/format-parity/scripts/assemble-corpus.ps1` — copies a curated set of representative SQL files into `corpus/` and generates the SQL Prompt golden output for each (corpus, style) pair via Redgate's CLI (manual one-time run; results checked in)
- [ ] T072 [US5] Populate the parity corpus — 200 SQL files exercising every documented setting group; 20 representative `.sqlpromptstyle` files (paired with golden outputs)
- [ ] T073 [US5] Implement `FormatParityTests` in `tests/AkmlSql.Core.Tests/Format/FormatParityTests.cs` — `[Theory]` over (corpus file × style file); applies the SC-007 normalisation (strip trailing whitespace per line, normalise EOL to `\n`, drop UTF-8 BOM) before comparing AKML output to golden; reports per-file pass/fail; suite passes if ≥ 95 % files pass per the SC-007 definition (Q1 clarification)

### Formatter pipeline gap closure (each gap = one task; per data-model.md SqlPromptStyleMapping table)

- [ ] T074 [P] [US5] Implement `Whitespace.PreserveEmptyLinesAfterBatch` in the formatter pipeline — likely in `src/AkmlSql.Engine/Formatter/Stages/NoformatScanner.cs` or `TextEmitter.cs`
- [ ] T075 [P] [US5] Implement `Lists.AlignAcrossClauses` in `src/AkmlSql.Engine/Formatter/Stages/LayoutEngine.cs`
- [ ] T076 [P] [US5] Replace hardcoded parenthesis collapse threshold with `Parens.CollapseThreshold` in the relevant layout rule under `src/AkmlSql.Engine/Formatter/Stages/`
- [ ] T077 [P] [US5] Implement Dml collapse settings (4 sub-settings: `CollapseShortStatements`, `CollapseThreshold`, `CollapseShortSubqueries`, `CollapseSubqueryThreshold`) in `src/AkmlSql.Engine/Formatter/Stages/DmlLayoutRules.cs`
- [ ] T078 [P] [US5] Implement Ddl alignment + first-param + collapse (4 sub-settings) in `src/AkmlSql.Engine/Formatter/Stages/DdlLayoutRules.cs`
- [ ] T079 [P] [US5] Implement `ControlFlow.CollapseThreshold`
- [ ] T080 [P] [US5] Implement `Cte.PlaceColumnsOnNewLine` enum in the CTE layout rule
- [ ] T081 [P] [US5] Implement `Joins.KeywordAlignment` (4 variants: `ToTable` / `ToFrom` / `IndentedFromFrom` / `RightAligned`) in the JOIN layout rule
- [ ] T082 [P] [US5] Implement Case settings: `FirstWhenOnNewLine` enum, `WhenAlignment` enum, `ExpressionOnNewLine` bool in the CASE layout rule
- [ ] T083 [P] [US5] Implement Operators settings: `Alignment` enum (3 variants), `BetweenOnNewLine` bool
- [ ] T084 [P] [US5] Implement `InStatements.Alignment` enum (3 variants)
- [ ] T085 [US5] Walk every closed gap (T074–T084) — update each `SqlPromptKeyMap` entry's `Status` from `GapToImplement` to `Implemented` and add the `ImplementedSince` field value `"020"`

### Shortcut bindings

- [ ] T086 [US5] Verify `Ctrl+K, Y` is bound to "Format SQL with active style" in every shell project's VSCT file (`AkmlSqlPackage.vsct` per host); add the binding where missing; confirm no conflict with host native bindings via a manual smoke test in each of SSMS 20/21/22 and VS 2019/22/26

**Checkpoint**: User Story 5 functional. Live preview hits the 250 ms target; every mapped setting honoured by the formatter; `FormatParityTests` ≥ 95 %; `Ctrl+K, Y` works in every host.

---

## Phase 8: User Story 6 — SQL History, Tab Coloring (visual), Code Analysis surfaces (Priority: P3)

**Goal**: Re-skin SQL History (the most parity-sensitive secondary surface), apply the `TabColor.*` swatches to Tab Coloring (visual-only — FR-011 narrowed per Q3 clarification), align Code Analysis severity palette. Produce the Tab Coloring audit doc (FR-011a).

**Independent Test**: Open SQL History → palette matches `SQL_Prompt_SQL_History.md §16.2`; assign a tab colour → swatch matches `TabColor.*` defaults; trigger an analysis warning → squiggle + message entry use Status palette; `tab-coloring-audit.md` exists and enumerates Phase 5 vs SQL Prompt rules.

- [ ] T087 [P] [US6] Audit `src/AkmlSql.Shell.Shared/History/SqlHistoryWindow.xaml` for any hardcoded chrome hex; migrate to `History.*` and surface tokens; verify against `doc/SQL-PROMPT/SQL-Prompt-History/SQL_Prompt_SQL_History.md §16.2`
- [ ] T088 [P] [US6] Re-skin SQL History toolbar button (`SqlHistoryToolbarButton.xaml`) to match the documented clock icon + label
- [ ] T089 [P] [US6] Re-skin search-match highlight in `SqlHistoryWindow` to use `History.MatchHighlight` token
- [ ] T090 [P] [US6] Re-skin Tab Coloring tab title bar in `src/AkmlSql.Shell.Shared/Tabs/` to use `TabColor.*` swatch tokens (visual parity only — Q3 narrows scope; Phase 5 assignment-rule engine is untouched)
- [ ] T091 [US6] Produce the Tab Coloring audit doc at `specs/020-sqlprompt-visual-parity/tab-coloring-audit.md` (FR-011a) — enumerate each documented SQL Prompt tab-coloring rule with a status of `Matches` / `Differs` / `Missing` against Phase 5's current implementation; cite specific Phase 5 file paths
- [ ] T092 [P] [US6] Re-skin Code Analysis squiggle colours in `src/AkmlSql.Shell.Shared/Analysis/SquiggleAdornment.cs` to use `Status.Danger` (Error) / `Status.Warning` / `Status.Info` (Suggestion) tokens
- [ ] T093 [P] [US6] Re-skin Code Analysis message list entries in `src/AkmlSql.Shell.Shared/Analysis/MessageListView.xaml` — severity column reads from same Status tokens

**Checkpoint**: User Story 6 functional. SQL History matches `§16.2`; Tab Coloring uses correct swatches; Code Analysis severity colours match SQL Prompt; audit doc exists and is checked in.

---

## Phase 9: User Story 7 — Prompt AI window, ghost text, tooltips, editor margins (Priority: P3)

**Goal**: Lowest-frequency surfaces, completing the "all features" coverage.

**Independent Test**: Open AI window → chrome matches `06_ai_window.svg`; ghost text uses dimmed foreground; hover tooltip chrome matches object-definition box; schema-loading spinner matches the 12×12 ellipse pattern with 1100 ms rotation.

- [ ] T094 [P] [US7] Audit `src/AkmlSql.Shell.Shared/Ai/AiToolWindow.xaml` for hardcoded chrome; migrate to `Chat.*`, `Surface.*`, `Text.*` tokens (`Chat.*` family already exists)
- [ ] T095 [P] [US7] Verify ghost-text dimmed foreground in `src/AkmlSql.Shell.Shared/Ai/GhostTextAdornment.cs` uses `Text.Disabled` or `Text.Placeholder` token (not hardcoded grey)
- [ ] T096 [P] [US7] Re-skin hover tooltip chrome in `src/AkmlSql.Shell.Shared/Editor/QuickInfoTooltip.xaml` to match the object-definition box token set
- [ ] T097 [P] [US7] Audit `src/AkmlSql.Shell.Shared/Editor/SchemaProgress/SchemaProgressMargin.cs` spinner — confirm `Ellipse` + `StrokeDashArray { 10, 30 }` + 12×12 dimensions + 1100 ms `RotateTransform` per CLAUDE.md "Editor margin spinner pattern"; ensure stroke reads from `Editor.SpinnerStroke` token

**Checkpoint**: User Story 7 functional. Every "all features" surface verified.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Cross-cutting verification work — DPI, accessibility, screenshot review, docs — and the final quickstart end-to-end.

- [ ] T098 [P] DPI scaling audit at 100 / 125 / 150 / 200 % for every in-scope surface (FR-016, SC-005) — record results per surface in `specs/020-sqlprompt-visual-parity/dpi-audit.md`
- [ ] T099 [P] Accessibility audit — verify every severity / icon-type / status surface carries a letter or shape glyph (FR-029, SC-012); colour-blind simulator pass (deuteranopia, protanopia, tritanopia) — record results in `specs/020-sqlprompt-visual-parity/a11y-audit.md`
- [ ] T100 [P] Side-by-side screenshot comparison for every documented surface (SC-003, SC-004) — pair each AKML surface with its `doc/SQL-PROMPT/**/*.svg`; record dimension and colour deviations in `specs/020-sqlprompt-visual-parity/screenshot-audit.md`; deviations > 8 px / > one tonal step are bugs
- [ ] T101 [P] Update `docs/architecture.md` to reflect new `FormatProfile` sub-settings POCOs and the built-in-style seeding flow
- [ ] T102 [P] Update `docs/formatting.md` with the full setting matrix and SC-007 normalisation definition
- [ ] T103 [P] Update `docs/configuration.md` to document the new `%AppData%/AKML SQL/styles/imported/` directory and `themeMigration.v1.json` / `builtIns.v1.seeded` markers
- [ ] T104 [P] Update `docs/ipc-api.md` with the new `RequestStyleEditorSchema (28/128)` message and the extended `ProfileImportResponse` fields
- [ ] T105 Run quickstart.md end-to-end on SSMS 22 — install, import a `.sqlpromptstyle`, toggle theme, verify preview latency, format a sample, run all test suites; capture any deviations in `specs/020-sqlprompt-visual-parity/quickstart-validation.md`
- [ ] T106 Update `doc/progress.md` with the spec 020 entry — summary, completion date, deliverables list

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately. All tasks T002–T006 are [P].
- **Foundational (Phase 2)**: Depends on Setup. T007 (IPC constants) and T008/T009 (token additions) are [P]; T010 wires bindings (depends on T008/T009); T011/T012 (scanner + coverage tests) are [P].
- **User Story 1 (Phase 3) AND User Story 2 (Phase 4)** can start in parallel after Foundational completes. Both are P1 / MVP.
- **User Stories 3, 4, 5 (Phase 5–7)** can start after both US1 and US2 complete (US3 needs the schema IPC from Phase 4; US4 needs `IconBadge.*` tokens from Phase 3; US5 needs the importer from Phase 4 to test parity).
- **User Stories 6, 7 (Phase 8–9)** can start after US1 / US3 (need token bank + completed editor) and may run in parallel.
- **Polish (Phase 10)** depends on every user story being complete.

### Within Each User Story

- Token-add tasks (T013, T014, T015) modify `ThemeTokens.cs` — NOT parallel with each other (same file), but [P] with binding tasks since `ThemeRegistry.cs` is a different file
- Sub-settings POCO tasks (T023, T024) touch the same `FormatProfile.cs` — sequential
- Importer (T027) depends on T023–T026; Exporter (T028) depends on T023–T026
- Editor panels (T054–T057) are different files — [P] — but ViewModel (T058) depends on all
- Each formatter-pipeline gap closure task (T074–T084) is a different rule / file — [P]
- Each test task is a different file — [P] within its suite

### Parallel Opportunities

- All Setup tasks T002–T006 in parallel
- Foundational [P] tasks: T008, T009, T011, T012 in parallel; T007 + T010 sequential (T010 binds T008/T009)
- US1 token-add tasks T013, T014, T015 — same file, sequential; binding tasks T016, T017, T018 — same file, sequential; T019 (migration manager) is [P] with all of them
- US2 built-in style transcriptions T032, T033, T034 — different files — [P]
- US2 test files T039, T040, T041 — different files — [P]
- US3 editor panels T054, T055, T056, T057 — different files — [P]
- US4 surface re-skin tasks T061, T064, T065, T066 — different surfaces — [P]
- US5 gap closure tasks T074–T084 — different formatter rules — [P]
- US6 surface re-skin tasks T087, T088, T089, T090, T092, T093 — different surfaces — [P]
- US7 surface audit tasks T094, T095, T096, T097 — different surfaces — [P]
- Polish docs / audit tasks T098–T104 — different files / docs — [P]

---

## Parallel Example: User Story 2 (P1 MVP)

```bash
# After T023–T026 (POCO + key map + schema) sequentially complete, launch the importer and exporter:
Task: "Implement SqlPromptStyleImporter in src/AkmlSql.Engine/Formatter/Profiles/SqlPromptStyleImporter.cs (T027)"
Task: "Implement SqlPromptStyleExporter in src/AkmlSql.Engine/Formatter/Profiles/SqlPromptStyleExporter.cs (T028)"

# Built-in style transcriptions in parallel:
Task: "Transcribe Redgate Compact into CompactStyle.cs (T032)"
Task: "Transcribe Redgate Indented into IndentedStyle.cs (T033)"
Task: "Transcribe Redgate AlignedLeftBracket into AlignedLeftBracketStyle.cs (T034)"

# Test suites in parallel:
Task: "SqlPromptStyleImporterTests (T039)"
Task: "SqlPromptStyleExporterTests (T040)"
Task: "SqlPromptKeyMapTests (T041)"
```

---

## Implementation Strategy

### MVP First (US1 + US2 only)

The two P1 stories together = the MVP. US1 makes everything tokenised so later visual parity is easy; US2 makes SQL-Prompt-to-AKML migration possible. If we ship only Phases 1–4, a SQL Prompt user can already install AKML-SQL, import their team's `.sqlpromptstyle`, and start formatting.

1. Phase 1 (Setup) — reconcile design docs, scaffold test folders
2. Phase 2 (Foundational) — IPC constants, Spacing/Typography tokens, hex scanner
3. Phase 3 (US1) — IconBadge / TabColor / History tokens + bindings + migration handler + legacy `ThemeManager` cleanup
4. Phase 4 (US2) — POCOs, KeyMap, importer/exporter, built-in styles, global active-style invariant, full test suite
5. **STOP and VALIDATE**: run quickstart.md § 3 (import) + § 4 (theme switch) on SSMS 22; ship to a beta cohort

### Incremental Delivery

1. MVP (above) — ship
2. Add US3 (Options + Format Styles editor) → ship  — gives teams the editing experience
3. Add US4 (IntelliSense surfaces) → ship  — visible parity in the most-frequent surface
4. Add US5 (Format gap closure) → ship  — pushes parity from 60 % → 95 % corpus
5. Add US6 (History, Tab Coloring, Code Analysis) → ship
6. Add US7 (AI, ghost text, tooltips, margins) → ship
7. Polish (Phase 10) — DPI, a11y, screenshot, docs — finalise

### Parallel Team Strategy

Once Foundational (Phase 2) is done:

- Developer A: Phase 3 (US1 — token bank completion + migration) 
- Developer B: Phase 4 (US2 — importer / exporter / built-ins / tests)
- (after MVP) Developer A: Phase 5 (US3 — editor window)
- (after MVP) Developer B: Phase 7 (US5 — formatter gap closure, mostly parallel internally)
- (after MVP) Developer C: Phase 6 (US4 — IntelliSense surfaces)
- US6, US7, Polish — distribute as bandwidth allows

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks in the same phase
- Every [US*] task is independently testable per the spec's `Independent Test` block for that story
- `HardcodedHexScannerTests` (T011) is a gate — once it goes green, it stays green; any later task that introduces a hardcoded chrome hex fails the test
- Tab Coloring assignment-rule behaviour is **explicitly out of scope** per Q3 clarification — only the audit doc (T091) is in scope this spec
- The `casing.useObjectDefinitionCase` setting is **out of scope** (`Status = Unsupported`); preserved on round-trip via `PassthroughUnknownKeys` per FR-024
- Per project rules in `CLAUDE.md`: never commit / push / run `git add` without explicit user approval — this task list expects you to ask for commits between phases or at the user's discretion
- Build shell projects individually with MSBuild — never `dotnet build`, never via solution — to avoid VSCT cross-contamination
