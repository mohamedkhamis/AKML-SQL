# Tasks: Format Styles Window Promotion — the dedicated SQL Prompt-grade style editor

**Input**: Design documents from `/specs/033-format-styles-window/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: INCLUDED — the repo's TDD gate applies to engine/Core logic (Constitution Check), and SC-009 mandates the new coverage. Write each story's tests first and watch them fail before implementing.

**Organization**: Grouped by user story (US1–US5 from spec.md). US1+US2 are both P1; US1 alone is the MVP increment.

**Standing rules for every task** (from plan.md/quickstart.md):

- Engine.Tests runs are ALWAYS filtered: `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "FullyQualifiedName!~PerformanceBaseline"` (untagged ~13-min perf gate in-project).
- Shell projects build per-project with full MSBuild (`-t:Restore` then `-t:Build`), never `dotnet build`, never solution build.
- New Shell.Shared source files must be added to `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems`.
- Setting/group **ids are byte-frozen** (`"{groupId}.{jsonName}"`) — SqlPromptKey resolution keys on them.
- NEVER commit — the user commits explicitly.

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

**Purpose**: Confirm a green baseline so spec-033 regressions are attributable.

- [X] T001 Baseline run of the four affected suites (all must pass before any change): `dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj`, `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "FullyQualifiedName!~PerformanceBaseline"`, `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj`, `dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj`; record counts in the PR notes

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The VM IPC seam every ViewModel test in US1/US2/US3 depends on (research R7: no fake-IPC is possible today — six call sites read the static `EngineLifecycle.Manager?.Client`).

**⚠️ CRITICAL**: No user-story VM work can be tested until this phase is complete.

- [X] T002 Introduce an injectable IPC seam in `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorViewModel.cs`: define `internal interface IRpcClientAccessor` (new file `src/AkmlSql.Shell.Shared/Ipc/RpcClientAccessor.cs` + projitems entry) exposing `IsConnected` and `Task<T> SendRequestAsync<T, TPayload>(int messageType, TPayload payload, int timeoutMs, CancellationToken ct)`; default implementation delegates to `EngineLifecycle.Manager?.Client`; VM gains an internal constructor accepting the accessor and routes all six call sites (lines 357, 436, 490, 527, 579, 624) through it — behavior identical, all 83 existing Shell.Shared tests stay green
- [X] T003 Fake RPC client test double in `tests/AkmlSql.Shell.Shared.Tests/FakeRpcClientAccessor.cs`: canned response per message type (dictionary of `int -> Func<object, object>`), records every request (type + payload) for assertions, settable `IsConnected`; no ThemeRegistry/WPF dependency so it runs in plain facts

**Checkpoint**: Seam in place, suites green — user stories can begin.

---

## Phase 3: User Story 1 — Edit a style and have it stick (Priority: P1) 🎯 MVP

**Goal**: Selecting a style loads its real stored values (new `ProfileGet` 34/134 returning RAW file text); edits are dirty-tracked and Saved by merging into the loaded JSON (preserving `metadata` + `ExtensionData`) via existing `ProfileSave`; built-ins are visibly read-only.

**Independent Test**: quickstart.md steps 1, 2, 4, 9 — select "Khamis Style" (values differ from "Default"), copy it, edit, Save, Format SQL reflects the edit without restart; built-in shows disabled controls + "Copy this style to edit"; switching styles with unsaved edits prompts.

### Tests for User Story 1 (write first, must fail)

- [X] T004 [P] [US1] MessagePack round-trip tests for `ProfileGetRequest`/`ProfileGetResponse` (key layout 0..4 per contracts/ipc-profile-messages.md) in `tests/AkmlSql.Core.Tests/Ipc/ProfileGetMessageTests.cs`
- [X] T005 [P] [US1] `ProfileManager.TryReadRaw` tests in `tests/AkmlSql.Formatting.Tests/Profiles/ProfileManagerTests.cs` (temp builtin/custom dirs pattern): returns file text VERBATIM (assert byte-equality including an unknown key nested inside `whitespace` — must survive); custom-first resolution; `isBuiltIn` true only for built-in-dir-resolved with no custom shadow; a custom file whose JSON lies `"isBuiltIn": true` still reports `isBuiltIn == false` (directory-derived, research R1 trap); unknown name returns false
- [X] T006 [P] [US1] Engine handler tests in `tests/AkmlSql.Engine.Tests/Handlers/FormattingHandlersTests.cs`: `ProfileGet` unknown name → `Success=false`, nothing created; known name → raw text + `IsBuiltIn`; extend the message-type-pair sanity test with 34/134; PLUS `ProfileSave` regression: `ProfileJson` > 1 MB → `Success=false`, nothing written
- [X] T007 [P] [US1] `ProfileJsonMerger` pure-function tests in `tests/AkmlSql.Shell.Shared.Tests/ProfileJsonMergerTests.cs`: overlays only changed working values; creates missing group objects; multi-segment nesting (`insertStatements.columns.parenthesisStyle` → `{"insertStatements":{"columns":{"parenthesisStyle":...}}}`); preserves `metadata`, root `ExtensionData`-style unknown keys, and unknown keys nested in untouched groups; output re-parses as valid JSON
- [X] T008 [US1] VM behavior tests via `FakeRpcClientAccessor` in `tests/AkmlSql.Shell.Shared.Tests/FormatStylesEditorViewModelTests.cs`: load-on-select issues ProfileGet and seeds working values = schema defaults overlaid with profile values (`IsDirty == false`, `IsSelectedReadOnly` from response); `SetWorkingValue` flips `IsDirty`; Save sends the MERGED json (fake asserts `metadata` present in the ProfileSave payload) and clears dirty; ProfileGet failure clears selection and never shows defaults masquerading as the style; disconnected accessor → `LastError`, no crash (depends on T003)

### Implementation for User Story 1

- [X] T009 [P] [US1] Add `ProfileGet = 34` / `ProfileGetResult = 134` to `src/AkmlSql.Core/Ipc/RpcMessage.cs` and create `src/AkmlSql.Core/Ipc/Messages/ProfileGetRequest.cs` + `ProfileGetResponse.cs` (`[MessagePackObject]`, keys per data-model.md §3, one class per file matching `DuplicateProfileRequest` style)
- [X] T010 [P] [US1] `ProfileManager.TryReadRaw(string name, out string json, out bool isBuiltIn)` in `src/AkmlSql.Formatting/Profiles/ProfileManager.cs`: custom-first probe identical to `Load()`, `File.ReadAllText` verbatim, `isBuiltIn` derived from resolving directory AND no-custom-shadow (never from JSON)
- [X] T011 [US1] `HandleProfileGet` in `src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs` (+ typed `ProfileGetHandler` adapter in `src/AkmlSql.Engine/Handlers/Formatting/FormattingHandlers.cs`, + registration in `src/AkmlSql.Engine/EngineHandlerRegistry.cs`): unknown name → `Success=false` "Profile '<name>' was not found."; catch-all → `Success=false` + message (depends on T009, T010)
- [X] T012 [US1] `HandleProfileSave` hardening in `src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs`: reject `ProfileJson` over 1 MB (mirror the import cap at :504-512) before deserializing
- [X] T013 [P] [US1] `ProfileJsonMerger` in new `src/AkmlSql.Shell.Shared/Formatting/ProfileJsonMerger.cs` (+ projitems entry): pure `internal static string Merge(string baseJson, IReadOnlyDictionary<string, object?> workingValues, IReadOnlyDictionary<string, object?> schemaDefaults)` using `System.Text.Json.Nodes` — writes a working value by full dotted path only when it differs from the base's effective value; multi-segment nesting; never touches `metadata` or unrelated keys
- [X] T014 [US1] VM load-on-select in `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorViewModel.cs`: new state per data-model.md §5 (`_loadedProfileJson`, `_loadedProfileName`, `IsDirty`, `IsSelectedReadOnly` notify properties); guarded selection transition `SelectProfileAsync(name)` (dirty check hook → window-provided prompt callback returning Save/Discard/Cancel; Cancel restores previous selection); ProfileGet fetch (5000 ms action timeout); seed working values = schema defaults overlaid with flattened profile values (multi-segment paths); failure path per T008; `SwitchToMainThreadAsync` before touching bound state; `QueuePreviewAsync()` after load (depends on T002, T009, T013)
- [X] T015 [US1] VM `SaveAsync` in `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorViewModel.cs`: `ProfileJsonMerger.Merge(_loadedProfileJson, ...)` → `ProfileSave` (msg 15, 5000 ms); on success update `_loadedProfileJson` to the merged text and clear `IsDirty`; on failure `LastError` + keep dirty; hard-refuse when `IsSelectedReadOnly`
- [X] T016 [US1] Window editing UX in `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorWindow.cs`: Save button in the footer (enabled binding: `IsDirty && !IsSelectedReadOnly`); wire ListBox `SelectionChanged` through the guarded `SelectProfileAsync` with a Save/Discard/Cancel `TaskDialog`-style prompt (themed MessageBox is acceptable); same prompt on window `Closing` when dirty; read-only state disables the settings-controls host and shows a "Copy this style to edit" hint bar; double-click on a built-in list item triggers the existing Copy action
- [X] T017 [US1] Engine-level edit round-trip test (SC-006) in `tests/AkmlSql.Engine.Tests/Formatter/ProfileEditRoundTripTests.cs`: import the Redgate fixture (reuse `ProfileImportHandlerTests` harness) → `HandleProfileGet` → merge one changed setting into the returned raw JSON (inline JsonNode edit mirroring the merger's output shape) → `HandleProfileSave` → reload the file: unknown root keys intact, `<name>.source.json` sidecar intact, `metadata.name` unchanged

**Checkpoint**: US1 fully functional — quickstart steps 1/2/4/9 pass end-to-end after engine publish + shell deploy.

---

## Phase 4: User Story 2 — Readable, SQL Prompt-grade organization (Priority: P1)

**Goal**: Schema v2 (5-category `ParentId` hierarchy, `AllowedEnumValues`/`Description`/`Min`/`Max` from a new `[SettingMeta]` attribute — there are NO C# enums to reflect over), rendered as a 2-level tree with themed enum ComboBoxes, clamped ints, per-setting descriptions; v1 graceful degrade; in-window sample editing (closes spec-020 T069).

**Independent Test**: quickstart.md step 3 — tree shows exactly Global/Statements/Clauses/Expressions/Other; every enum setting is a dropdown; every setting shows a description; `tabSize` rejects 99999. Plus step 10 degrade check.

### Tests for User Story 2 (write first, must fail)

- [X] T018 [P] [US2] Schema-v2 completeness tests in `tests/AkmlSql.Formatting.Tests/Profiles/FormatSettingSchemaV2Tests.cs` (aggregate-offenders-fail-once idiom from `RedgateSchemaCompletenessTests.cs:24-31`): `SchemaVersion == 2`; every group's `ParentId` ∈ {global, statements, clauses, expressions, other} per the data-model category map; every `"Enum"` setting has non-empty `AllowedEnumValues` containing its `Default`; every setting has non-empty `Description`; every ranged `"Int"` satisfies `Min <= Default <= Max`; the 6 flattened `insertStatements.columns/values.*` ids exist as typed rows and the two `"Other"` blob rows are gone; **id-freeze guard**: all v1 setting ids still present byte-identical and every `ExplicitKeyMap` key still resolves to a setting

### Implementation for User Story 2

- [X] T019 [US2] `SettingMetaAttribute` in new `src/AkmlSql.Formatting/Profiles/SettingMetaAttribute.cs`: `[AttributeUsage(AttributeTargets.Property)]`, fields `Description` (string), `AllowedValues` (string[]?), `Min`/`Max` (int, sentinel `int.MinValue` = unset) per data-model.md §1
- [X] T020 [US2] Annotate all ~178 settable properties across the 18 POCOs in `src/AkmlSql.Formatting/Profiles/FormattingProfile.cs` with `[SettingMeta]`: one-sentence user-facing `Description` everywhere; `AllowedValues` on the ~60 enum-like strings using the EXACT stored spellings already documented in each property's XML doc comment (mixed case preserved: `"UPPERCASE"`, `"AsIs"`, `"trailing"`, `"compactSimple"`, the 9-value `parenthesis.style` set, …), each containing the property default; `Min`/`Max` on the 14 ranged ints (`tabSize`, `maxLineWidth`, `blankLinesBeforeGoCount` 0–5, 6× `collapseThreshold`, `subqueryCollapseThreshold`, `inListThreshold`, `emptyLineBetweenStatements`, `maxConsecutiveEmptyLines`, `emptyLinesAfterBatchSeparator`); T018 is the completeness oracle — iterate until it passes
- [X] T021 [US2] Schema builder v2 in `src/AkmlSql.Formatting/Profiles/FormatSettingSchema.cs`: static group→category map emitting `ParentId` on all 18 group rows (unmapped future group → `"other"`); read `SettingMeta` in the inner loop (`GetCustomAttribute` beside the existing `JsonPropertyNameAttribute` read) → populate `AllowedEnumValues`/`Description`/`Min`/`Max`; flatten `InsertStatementsOptions.Columns/Values` into 6 multi-segment ids replacing the two `"Other"` blobs, with `ExplicitKeyMap` entries for their Redgate keys (importer's insertStatements section); bump the `SchemaVersion` literal at :56 to `2` — categories are NOT emitted as group rows (contracts/style-editor-schema-v2.md)
- [X] T022 [US2] Window schema consumption in `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorWindow.cs`: extend `FormatSettingNode` + `RebuildSettingsTreeFromSchema` to read `parentId`/`description`/`allowedEnumValues`/`min`/`max` (defensive `TryGetProperty` — all optional); build the 2-level tree (category nodes from the id→display map global→Global etc., expanded by default; unknown/missing parentId → Other; v1 schema → current flat rendering, no crash); delete the dead `SelectedItemChanged` handler at :637-644 and the duplicate `UpdateStatus` at :1190-1193
- [X] T023 [US2] Setting controls in `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorWindow.cs` (`BuildControlForSetting`/`UpdateRightTopForSetting`): `"Enum"` with `allowedEnumValues` → `ComboBox` of plain-string items via `Ui.Theme.ComboBoxTheming.Apply(combo)` (selection persists the exact spelling; without values → existing TextBox degrade); `"Int"` → numeric TextBox clamped to `min`/`max` with inline red-border/tooltip rejection BEFORE preview or save; render `description` as a wrapped secondary-text line under the setting header; keep the SqlPromptKey/status metadata line and badges unchanged
- [X] T024 [US2] Multi-segment working values in `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorViewModel.cs`: `SeedWorkingValuesFromSchema` + the T014 profile-overlay handle multi-segment ids (`insertStatements.columns.parenthesisStyle`), and `BuildProfileJson` (preview path) nests by ALL dot segments instead of first-dot-only (:298-299) so flattened settings preview correctly
- [X] T025 [US2] In-window preview-sample editing (FR-014, closes spec-020 T069) in `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorWindow.cs` + `FormatStylesEditorViewModel.cs`: "Edit sample" toggle switches the preview box (Sample source only) to an editable input whose text persists via the existing atomic write to `%AppData%/AKML SQL/editor/preview-sample.sql` and re-runs the debounced preview; existing 2 s timeout + size handling unchanged
- [X] T026 [US2] Degrade test in `tests/AkmlSql.Shell.Shared.Tests/FormatStylesTreeDegradeTests.cs` (single `[StaFact]` — ThemeRegistry one-window-class rule): feeding a v1-shaped schema JSON (no parentId/allowedEnumValues/description) to `RebuildSettingsTreeFromSchema` renders the flat tree and free-text enum boxes without throwing; v2-shaped JSON yields exactly 5 top-level category nodes

**Checkpoint**: US1+US2 together = the window is a real, readable editor. Quickstart steps 1–4, 9 pass.

---

## Phase 5: User Story 3 — Full style lifecycle in the list panel (Priority: P2)

**Goal**: `ProfileRename` (35/135, atomic engine transaction incl. `.source.json` sidecar); New-based-on / Rename / Delete (active-blocked) from toolbar + per-style context menu; sectioned list ("Your styles"/"Built-in styles") with ✔ active marker and a working lock glyph.

**Independent Test**: quickstart.md step 5 — create "Team Standard" based on "Khamis Style", rename it, set active (✔ moves, status bar + config update), delete blocked while active, delete succeeds after deactivating.

### Tests for User Story 3 (write first, must fail)

- [X] T027 [P] [US3] MessagePack round-trip tests for `ProfileRenameRequest`/`ProfileRenameResponse` in `tests/AkmlSql.Core.Tests/Ipc/ProfileRenameMessageTests.cs`
- [X] T028 [P] [US3] `ProfileManager.Rename` tests in `tests/AkmlSql.Formatting.Tests/Profiles/ProfileManagerTests.cs`: happy path renames file AND rewrites `metadata.name` (+`modified`); built-in source rejected; collision vs existing custom AND vs built-in name rejected (OrdinalIgnoreCase); case-only rename (`"my style"` → `"My Style"`) succeeds; `<old>.source.json` sidecar moves; old name unloadable + new name loadable afterward
- [X] T029 [P] [US3] Engine handler tests in `tests/AkmlSql.Engine.Tests/Handlers/FormattingHandlersTests.cs`: `ProfileRename` happy/reject paths + message-type-pair 35/135; PLUS `ProfileDelete` regression: deleting a nonexistent profile now returns `Success=false` (currently discards the bool — `FormatRequestHandler.cs:309-321`)
- [X] T030 [US3] VM lifecycle tests via fake accessor in `tests/AkmlSql.Shell.Shared.Tests/FormatStylesEditorViewModelTests.cs` (extend): renaming the ACTIVE style updates `AppSettings.Formatter.ActiveProfile` via ConfigManager (use the `AKML_APP_DATA_ROOT` + `[Collection]` isolation pattern from `DisableRuleFixActionTests`/`ConfigManagerTests`); deleting the active style is refused shell-side with a message before any IPC; New-based-on passes the chosen base name to `DuplicateProfile`; `StyleListItem.IsActive` recomputes after set-active and rename

### Implementation for User Story 3

- [X] T031 [P] [US3] Add `ProfileRename = 35` / `ProfileRenameResult = 135` to `src/AkmlSql.Core/Ipc/RpcMessage.cs` and create `src/AkmlSql.Core/Ipc/Messages/ProfileRenameRequest.cs` + `ProfileRenameResponse.cs` (keys per data-model.md §3)
- [X] T032 [US3] `ProfileManager.Rename(string oldName, string newName)` in `src/AkmlSql.Formatting/Profiles/ProfileManager.cs`: the contract transaction (read raw custom → rewrite `metadata.name`+`modified` → atomic temp+move write to new file → delete old → move sidecar); guards per T028; returns final sanitized name (depends on T010's raw-read plumbing)
- [X] T033 [US3] `HandleProfileRename` + `ProfileDelete` bool fix in `src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs` (+ adapter in `Handlers/Formatting/FormattingHandlers.cs`, + registration in `EngineHandlerRegistry.cs`) (depends on T031, T032)
- [X] T034 [US3] VM lifecycle operations in `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorViewModel.cs`: `RenameSelectedAsync(newName)` (on success: if renamed style was `Formatter.ActiveProfile`, update config via `ConfigManager` + status bar; reload list; reselect new name); `DeleteSelectedAsync` (shell-side refusal when target is the active style OR built-in, with status message; on success reload + clear selection); `CreateStyleAsync(name, basedOn)` calling `DuplicateProfile` with the chosen base then rename-to-chosen-name if needed; add `StyleListItem.IsActive` computed from `ConfigManager.Load().Formatter.ActiveProfile`, recomputed on list load / set-active / rename
- [X] T035 [US3] Name-prompt dialog in new `src/AkmlSql.Shell.Shared/Formatting/StyleNameDialog.cs` (+ projitems entry): small `ThemeAwareWindow` subclass (accepted-flag closure shape of `SettingsWindow.ShowRuleEditor`), `Owner = <styles window>` per the ImportSummaryDialog nested-modal rule; two modes — New Style (name TextBox + based-on themed ComboBox listing existing styles) and Rename (name TextBox pre-filled); OK `IsDefault`, Cancel `IsCancel`, inline validation (non-empty, no invalid filename chars)
- [X] T036 [US3] Style list overhaul in `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorWindow.cs`: replace the broken XAML-string DataTemplate (unregistered `BoolToVisibilityConverter`, :567) with a CODE-BUILT template rendering lock glyph (IsReadOnly), name, Kind badge, and ✔ (IsActive); section the list "Your styles" / "Built-in styles" via `CollectionViewSource` grouping with themed group headers; per-item `ContextMenu` (Set Active / Copy / Rename… / Delete / Export…) enabled per item state (no Rename/Delete on built-ins); wire New Style…/Rename… through `StyleNameDialog`; Delete gets a themed confirm; keep toolbar actions in sync

**Checkpoint**: Full lifecycle works; quickstart step 5 passes; profile files on disk verified.

---

## Phase 6: User Story 4 — Options page becomes the SQL Prompt-exact launcher (Priority: P2)

**Goal**: Options → Format → Styles = active-style dropdown + "Edit formatting styles…" button + "Behavior" group; post-close refresh fixes the ActiveProfile OK-clobber.

**Independent Test**: quickstart.md step 7 — open editor from the button, create+activate a style, close: dropdown refreshes and selects it; OK on Options does NOT revert the active style.

### Tests for User Story 4 (write first, must fail)

- [X] T037 [P] [US4] `FormattingPage` tests in `tests/AkmlSql.Shell.Shared.Tests/FormattingPageTests.cs` (headless — `FormattingControls` is internal + engine-disconnected `PopulateProfilesAsync` early-returns): the refresh method re-seeds the combo from `ConfigManager.Load().Formatter.ActiveProfile` (use the AppData isolation collection); after refresh, `Save` writes the refreshed name (no-clobber, US4 scenario 3); page Build contains the "Edit formatting styles…" button and a "Behavior" group header

### Implementation for User Story 4

- [X] T038 [US4] `FormattingPage` update in `src/AkmlSql.Shell.Shared/Dialogs/Pages/FormattingPage.cs`: add "Edit formatting styles…" via `RowFactory.AddButton` directly after the Active-style/status-bar rows (:26-34); click → `FormatStylesEditorWindow.Launch()` (modal) → on return re-read `ConfigManager.Load().Formatter.ActiveProfile`, re-seed the combo, re-run `PopulateProfilesAsync(active)` (extract the seed+populate sequence into an internal `RefreshActiveStyleFromDisk()` used by both Load and the button, giving T037 its seam); regroup the existing "Triggers" + "Safety & Validation" sections under one "Behavior" umbrella header (keep the two sub-headers as secondary rows); update the page help text to mention the button

**Checkpoint**: Options is the SQL Prompt-exact launcher; quickstart step 7 passes.

---

## Phase 7: User Story 5 — Discoverability and debris removal (Priority: P3)

**Goal**: Format Styles on every command surface; legacy editor stack deleted; stale docs corrected.

**Independent Test**: quickstart.md step 8 + repo greps — SSMS DTE menu and Command Palette show Format Styles; zero references to `ProfileEditorDialog` remain; both hosts build clean.

### Implementation for User Story 5

- [X] T039 [P] [US5] Add `(CommandIds.CmdFormatStyles, "Format Styles...")` to the `EnsureTopLevelMenu` cmds tuple array in `src/AkmlSql.Ssms22/AkmlSqlPackage.cs` (:436-452), positioned after Options to mirror the VSCT order
- [X] T040 [P] [US5] Command Palette swap: in `src/AkmlSql.Shell.Shared/Productivity/CommandPalette/CommandRegistry.cs` remove the dead "Edit Format Profile" entry (:110) and add `Cmd("akml.formatStyles", "Format Styles...", "Format", CommandIds.CmdFormatStyles)` (pattern `akml.options` at :95); in `CommandPaletteViewModel.cs` remove the `"akml.editProfile"` arm (:434) and add `"akml.formatStyles" => CommandIds.CmdFormatStyles` in `GetCommandIdValue`
- [X] T041 [US5] Delete the legacy editor stack: remove `src/AkmlSql.Shell.Shared/Ui/ProfileEditorDialog.cs`, `Ui/ProfileEditorViewModel.cs`, `Ui/OptionCategoryTreeBuilder.cs`, `Ui/SqlPreviewRenderer.cs`, `Commands/EditProfileCommand.cs`; remove projitems lines 114-118 from `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems`; remove `CmdEditProfile = 0x0220` from `src/AkmlSql.Shell.Shared/PackageGuids.cs:34`; fix the `<see cref="Ui.ProfileEditorDialog"/>` doc comment at `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorWindow.cs:32` and the `<c>EditProfileCommand</c>` mention at `src/AkmlSql.Shell.Shared/Productivity/BulkFormatCommand.cs:130`; verify with full MSBuild of BOTH hosts (Restore+Build) and a repo-wide grep for `ProfileEditor|EditProfile` returning only history/docs
- [X] T042 [P] [US5] Stale-docs corrections: `CLAUDE.md` — "Open follow-ups" bullet claiming T044-T048/T059 deferred (they shipped in 17e294c) and the "System.Text.Json 8.x" note (actually 9.*); `doc/architecture.md:290` same deferred claim; `specs/020-sqlprompt-visual-parity/T059-runbook.md:3` superseded note

**Checkpoint**: All five stories complete.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T043 [P] Document the new IPC surface: add ProfileGet 34/134 + ProfileRename 35/135 (+ the delete/save behavior fixes and schema-v2 note) to `docs/ipc-api.md`; add the spec-033 entry to `doc/progress.md`
- [X] T044 Full verification gate: all four suites green (`Formatting`, `Engine` with the PerformanceBaseline filter, `Core`, `Shell.Shared`); `dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64`; full MSBuild (Restore+Build) of `src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj` and `src/AkmlSql.VS2026/AkmlSql.VS2026.csproj`; confirm `BuiltInStyleGenerationTests` drift guard did NOT fire (built-in `.akmlstyle` content must be unchanged by annotation work)
- [ ] T045 Deploy to the dev machine (full engine publish copy + SSMS extension via the elevated robocopy script; SSMS closed; user approves UAC) and run quickstart.md manual verification steps 1–10 with the user (restart SSMS after deploy — both schema caches are process-lifetime)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (P1)** → **Foundational (P2)** → user stories.
- **US1 (Phase 3)**: only depends on Foundational. **MVP.**
- **US2 (Phase 4)**: engine side (T018–T021) is independent of US1 and can run in parallel with it; the shell side (T022–T026) touches the same window/VM files as US1's T014–T016 — run after US1 shell tasks (or coordinate carefully).
- **US3 (Phase 5)**: T032 reuses T010's raw-read plumbing; T034/T036 build on US1's VM/window state — run after US1. Engine/Core tasks (T027–T033) can overlap US2's shell work.
- **US4 (Phase 6)**: only needs `Launch()` (exists) — independent after Foundational; refresh test uses the AppData isolation pattern.
- **US5 (Phase 7)**: T041's cref fix touches the window file — do after US1–US3 window edits settle; T039/T040/T042 anytime.
- **Polish (Phase 8)**: last; T045 requires the user present.

### Story Dependency Summary

- US1: Foundational only 🎯
- US2: engine-independent / shell-after-US1 (same files)
- US3: after US1 (VM/window state); engine parts parallel with US2-engine
- US4: independent
- US5: mostly independent; T041 last among code tasks

### Parallel Opportunities

- Phase 3 tests T004–T007 all [P]; impl T009/T010/T013 all [P] (different projects).
- US2 engine track (T018→T021) ∥ US1 shell track (T014–T016).
- US3 engine/Core track (T027–T033) ∥ US2 shell track (T022–T026).
- T037 ∥ any; T039/T040/T042/T043 all [P].

## Parallel Example: User Story 1

```text
# Wave 1 (tests, all parallel):    T004  T005  T006  T007
# Wave 2 (impl, parallel):         T009 (Core)   T010 (Formatting)   T013 (Shell merger)
# Wave 3 (sequential engine):      T011 → T012
# Wave 4 (sequential shell):       T014 → T015 → T016, then T008 goes green
# Wave 5:                          T017 round-trip
```

## Implementation Strategy

**MVP first**: Phases 1–3 (T001–T017) deliver the core promise — the window actually edits and saves styles. Deploy + validate quickstart steps 1/2/4/9 before continuing.

**Increment 2**: US2 (readability) — the "like SQL Prompt" payoff. Deploy + validate step 3.

**Increment 3**: US3 lifecycle, then US4 Options, then US5 cleanup — each independently verifiable per its checkpoint. Finish with Phase 8 and the user-assisted quickstart run.

Total: **45 tasks** (US1: 14, US2: 9, US3: 10, US4: 2, US5: 4, Setup/Foundational: 3, Polish: 3).
