# Tasks: Snippet Manager

**Input**: Design documents from `/specs/004-snippet-manager/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Create directory structure and shared infrastructure for the snippet module

- [x] T001 Create directory structure for engine snippet module: src/AkmlSql.Engine/Snippets/, src/AkmlSql.Engine/Snippets/Import/
- [x] T002 Create directory structure for shell snippet integration: src/AkmlSql.Shell.Shared/Snippets/
- [x] T003 Create test directory: tests/AkmlSql.Engine.Tests/Snippets/
- [x] T004 [P] Create directory for built-in snippets: src/AkmlSql.Engine/Snippets/BuiltIn/ (or embedded resource folder)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

### IPC Message Types

- [x] T005 [P] Create SnippetExpandRequest and SnippetExpandResponse MessagePack message types in src/AkmlSql.Core/Ipc/Messages/SnippetExpandRequest.cs and SnippetExpandResponse.cs per snippet-protocol-extension.md
- [x] T006 [P] Create SnippetListRequest and SnippetListResponse with SnippetInfo in src/AkmlSql.Core/Ipc/Messages/SnippetListRequest.cs and SnippetListResponse.cs
- [x] T007 [P] Create SnippetSaveRequest, SnippetSaveResponse, SnippetDeleteRequest, SnippetDeleteResponse in src/AkmlSql.Core/Ipc/Messages/SnippetSaveRequest.cs (and siblings)
- [x] T008 [P] Create SnippetImportRequest and SnippetImportResponse in src/AkmlSql.Core/Ipc/Messages/SnippetImportRequest.cs and SnippetImportResponse.cs
- [x] T009 [P] Create PlaceholderInfo shared model with VariableName, Offset, Length, DefaultText, SchemaAwareType, GroupIndex in src/AkmlSql.Core/Ipc/Messages/PlaceholderInfo.cs
- [x] T010 Extend CompletionRequest in src/AkmlSql.Core/Ipc/Messages/CompletionRequest.cs to add HasSelection boolean field for surround-with filtering

### Snippet Data Model

- [x] T011 [P] Create Snippet model with Metadata, Variables array, and Body array in src/AkmlSql.Engine/Snippets/Models/Snippet.cs per snippet-file-format.md
- [x] T012 [P] Create SnippetMetadata model with Id, Shortcode, Name, Description, Author, Version, Created, Modified, Category, Tags, Context, SurroundsWith in src/AkmlSql.Engine/Snippets/Models/SnippetMetadata.cs
- [x] T013 [P] Create SnippetVariable model with Name, Default, Tooltip, SchemaAware in src/AkmlSql.Engine/Snippets/Models/SnippetVariable.cs
- [x] T014 [P] Create SnippetSource model with Type enum (Personal/Team/BuiltIn), Priority, Path, IsWriteable, IsAvailable in src/AkmlSql.Engine/Snippets/Models/SnippetSource.cs

### Core Engine Components

- [x] T015 Create SnippetLoader that loads .akmlsnippet JSON files from all configured source directories using System.Text.Json in src/AkmlSql.Engine/Snippets/SnippetLoader.cs
- [x] T016 Create SnippetIndex with in-memory Dictionary keyed by ID, ShortcodeMap (lowercase shortcode → priority-ordered snippets), CategoryMap, Search(query) full-text search, and GetByContext(clauseType, hasSelection) filtering in src/AkmlSql.Engine/Snippets/SnippetIndex.cs
- [x] T017 Create PlaceholderParser that scans snippet body lines for $VarName$ markers and returns ordered list of placeholder positions with variable references in src/AkmlSql.Engine/Snippets/PlaceholderParser.cs
- [x] T018 Create BuiltInVariableResolver that resolves 14 built-in variables ($DATE$, $USER$, $DATABASE$, $SERVER$, $SCHEMA$, $GUID$, etc.) to current values using session context in src/AkmlSql.Engine/Snippets/BuiltInVariableResolver.cs

### Engine Integration

- [x] T019 Extend PipeRpcServer in src/AkmlSql.Engine/Server/PipeRpcServer.cs to route MessageTypes 20-24 to snippet handlers
- [x] T020 Extend AppSettings in src/AkmlSql.Core/Config/AppSettings.cs with SnippetSettings class containing Enabled, ShowInCompletion, TriggerKey, FormatOnExpand, PersonalFolder, TeamFolder, ContextFilter, SurroundShortcut, TrackUsage

**Checkpoint**: Foundation ready — snippet models, IPC messages, loader, index, and engine routing in place

---

## Phase 3: User Story 1 — Snippet Expansion via Shortcode (Priority: P1) MVP

**Goal**: User types a shortcode, presses Tab, and the snippet expands with tab-stop placeholder navigation

**Independent Test**: Type `ssf` + Tab, verify expansion to `SELECT * FROM $TABLE$` with cursor at placeholder

### Engine-Side Expansion

- [ ] T021 [US1] Create SnippetExpander that takes a shortcode + session context, resolves built-in variables, applies format-on-expand via FormatterPipeline (if enabled), and returns SnippetExpandResponse with expanded text and placeholder positions in src/AkmlSql.Engine/Snippets/SnippetExpander.cs
- [ ] T022 [US1] Wire SnippetExpandRequest handler in engine to call SnippetExpander and return SnippetExpandResponse in src/AkmlSql.Engine/Snippets/SnippetRequestHandler.cs

### Shell-Side Tab-Stop Navigation

- [ ] T023 [US1] Create SnippetExpansionSession that holds tab-stop state: ordered TabStopGroup list (each with variable name + ITrackingSpan[]), current group index, cursor position ITrackingPoint, ITextUndoTransaction for Escape revert in src/AkmlSql.Shell.Shared/Snippets/SnippetExpansionSession.cs
- [ ] T024 [US1] Create SnippetExpansionManager that manages one active SnippetExpansionSession per ITextView, handles session creation on expansion and cleanup on commit/revert in src/AkmlSql.Shell.Shared/Snippets/SnippetExpansionManager.cs
- [ ] T025 [US1] Create SnippetTriggerHandler that detects shortcode + Tab in CompletionCommandHandler.Exec, sends SnippetExpandRequest to engine, receives response, replaces shortcode with expanded text via ITextEdit, and creates SnippetExpansionSession with ITrackingSpan placeholders in src/AkmlSql.Shell.Shared/Snippets/SnippetTriggerHandler.cs
- [ ] T026 [US1] Implement Tab/Shift+Tab navigation in SnippetExpansionSession: Tab advances to next TabStopGroup (selects primary span text), Shift+Tab moves to previous, past-last-group moves to $CURSOR$ and ends session in src/AkmlSql.Shell.Shared/Snippets/SnippetExpansionSession.cs
- [ ] T027 [US1] Implement linked placeholder synchronization: subscribe to ITextBuffer.Changed, when text changes within one span of a linked group propagate to all other spans via ITextBuffer.CreateEdit with reentrancy guard in src/AkmlSql.Shell.Shared/Snippets/SnippetExpansionSession.cs
- [ ] T028 [US1] Implement Escape revert: on Escape keypress during active session, roll back ITextUndoTransaction to restore original shortcode text and end session in src/AkmlSql.Shell.Shared/Snippets/SnippetExpansionSession.cs
- [ ] T029 [US1] Extend CompletionCommandHandler in src/AkmlSql.Shell.Shared/Editor/CompletionCommandHandler.cs to integrate SnippetTriggerHandler: on Tab, check for active snippet session (navigate) or shortcode match (trigger expansion) before default Tab behavior

### Minimal Built-in Snippets for MVP

- [ ] T030 [P] [US1] Create 6 essential built-in snippet files: ssf.akmlsnippet (SELECT * FROM), sel.akmlsnippet (SELECT...FROM...WHERE), ins.akmlsnippet (INSERT INTO), upd.akmlsnippet (UPDATE...SET...WHERE), del.akmlsnippet (DELETE FROM...WHERE), cte.akmlsnippet (WITH...AS) in src/AkmlSql.Engine/Snippets/BuiltIn/

### End-to-End Validation

- [ ] T031 [US1] Wire end-to-end: type shortcode → Tab → engine expand → shell replace + create session → Tab through placeholders → commit at $CURSOR$ — verify with ssf snippet

**Checkpoint**: Shortcode + Tab expands snippets with tab-stop navigation and linked placeholders

---

## Phase 4: User Story 2 — Built-in Snippet Library (Priority: P1)

**Goal**: 75+ built-in snippets across 5 categories available out of the box

**Independent Test**: Open snippet list, verify 75+ snippets across DML, DDL, DBA, ControlFlow, SurroundWith

### Built-in Snippet Files

- [ ] T032 [P] [US2] Create 14 remaining DML snippets: selc, selt, seld, inss, mer, rcte, piv, unpiv, ex, nex, j, lj, cj, ca in src/AkmlSql.Engine/Snippets/BuiltIn/
- [ ] T033 [P] [US2] Create 15 DDL snippets: ct, ci, cci, cui, cp, cf, ctf, cv, ctr, cs, ac, dc, afk, adf, ack in src/AkmlSql.Engine/Snippets/BuiltIn/
- [ ] T034 [P] [US2] Create 20 DBA/Metadata snippets: sp, sh, sw, dbsize, tsize, idx, midx, locks, blocks, waits, cpu, io, plan, frag, deps, cols, fks, perms, bak, rest in src/AkmlSql.Engine/Snippets/BuiltIn/
- [ ] T035 [P] [US2] Create 10 Error Handling/Control Flow snippets: tc, tct, ife, ifex, wh, cur, tran, raiserr, throw, print in src/AkmlSql.Engine/Snippets/BuiltIn/
- [ ] T036 [P] [US2] Create 10 Surround-With snippets: stc, stran, sife, sbe, stiming, snocount, scomment, sregion, snoformat, stemp in src/AkmlSql.Engine/Snippets/BuiltIn/
- [ ] T037 [US2] Update SnippetLoader to load built-in snippets from the BuiltIn/ directory as read-only (IsBuiltIn=true, prevent modification) in src/AkmlSql.Engine/Snippets/SnippetLoader.cs

**Checkpoint**: 75+ built-in snippets available across all 5 categories

---

## Phase 5: User Story 3 — Snippets in IntelliSense Popup (Priority: P1)

**Goal**: Snippets appear in the completion popup with a distinct icon, ranked by usage and filtered by context

**Independent Test**: Type partial shortcode, verify matching snippets appear in completion popup with snippet icon

- [ ] T038 [US3] Extend SnippetProvider in src/AkmlSql.Engine/Completion/Providers/SnippetProvider.cs to load snippets from SnippetIndex instead of hardcoded list, return CompletionItems with ObjectType=Snippet and snippet icon
- [ ] T039 [US3] Implement context filtering in SnippetProvider.GetCompletions: map current CursorContext.ClauseType to snippet context strings, filter snippets whose context list contains the current clause type in src/AkmlSql.Engine/Completion/Providers/SnippetProvider.cs
- [ ] T040 [US3] Implement surround-with filtering: only show surround-with snippets when CompletionRequest.HasSelection is true in src/AkmlSql.Engine/Completion/Providers/SnippetProvider.cs
- [ ] T041 [US3] Implement usage-based ranking in SnippetProvider: rank snippets by usage count from SnippetUsageTracker within their relevance group in src/AkmlSql.Engine/Completion/Providers/SnippetProvider.cs
- [ ] T042 [US3] Update CompletionSource in src/AkmlSql.Shell.Shared/Editor/CompletionSource.cs to pass HasSelection (from ITextView.Selection) in CompletionRequest
- [ ] T043 [US3] When user selects a snippet from the completion popup and presses Tab/Enter, trigger snippet expansion (send SnippetExpandRequest) instead of simple text insertion in src/AkmlSql.Shell.Shared/Editor/CompletionCommandHandler.cs

**Checkpoint**: Snippets appear in IntelliSense, filtered by context, ranked by usage, expandable from popup

---

## Phase 6: User Story 4 — Custom Snippet Creation (Priority: P2)

**Goal**: Users can create, save, and use custom snippets with a visual editor

**Independent Test**: Create a custom snippet in the manager, save it, type the shortcode, verify expansion

- [ ] T044 [US4] Implement snippet CRUD in SnippetRequestHandler: handle SnippetSaveRequest (create/update .akmlsnippet file in personal folder), SnippetDeleteRequest (delete file), validate shortcode uniqueness in src/AkmlSql.Engine/Snippets/SnippetRequestHandler.cs
- [ ] T045 [US4] Create SnippetEditorPanel with programmatic WPF: fields for shortcode, name, description, category dropdown, tags input, variable list editor (name, default, tooltip, schemaAware dropdown), and code body text editor in src/AkmlSql.Shell.Shared/Ui/SnippetEditorPanel.cs
- [ ] T046 [US4] Create SnippetPreviewRenderer that shows live expanded snippet with default variable values and syntax coloring (reuse SqlPreviewRenderer from Phase 3) in src/AkmlSql.Shell.Shared/Ui/SnippetPreviewRenderer.cs
- [ ] T047 [US4] Implement duplicate detection: when saving a snippet with a shortcode that conflicts with another source, show warning with source priority information in src/AkmlSql.Engine/Snippets/SnippetRequestHandler.cs
- [ ] T048 [US4] Implement built-in snippet protection: when user tries to edit a built-in snippet, offer to create a personal copy (duplicate with new GUID, source=Personal) in SnippetRequestHandler

**Checkpoint**: Custom snippets can be created, edited, saved, and used via shortcodes

---

## Phase 7: User Story 5 — Surround-With Snippets (Priority: P2)

**Goal**: Select code, press Ctrl+K, Ctrl+S, choose a template, selected code is wrapped

**Independent Test**: Select SQL, trigger surround-with, choose TRY/CATCH, verify wrapping

- [ ] T049 [US5] Create SurroundWithCommand that intercepts Ctrl+K, Ctrl+S, gets selected text from ITextView.Selection, sends SnippetListRequest with HasSelection=true to get surround-with snippets, shows a quick-pick list in src/AkmlSql.Shell.Shared/Snippets/SurroundWithCommand.cs
- [ ] T050 [US5] Implement surround-with expansion in SnippetExpander: replace $SELECTEDTEXT$ with the actual selected text before returning expansion result in src/AkmlSql.Engine/Snippets/SnippetExpander.cs
- [ ] T051 [US5] Register SurroundWithCommand with Ctrl+K, Ctrl+S keyboard shortcut in VSPackage command table
- [ ] T052 [US5] Handle edge case: if no text is selected when surround-with is invoked, disable the command or show message in src/AkmlSql.Shell.Shared/Snippets/SurroundWithCommand.cs

**Checkpoint**: Surround-with wraps selected code with chosen template

---

## Phase 8: User Story 6 — Schema-Aware Placeholders (Priority: P2)

**Goal**: Placeholders with schemaAware type show IntelliSense suggestions from schema cache during tab-stop navigation

**Independent Test**: Expand a snippet with schemaAware="tables" placeholder, verify table list appears at that tab-stop

- [ ] T053 [US6] Extend SnippetExpansionSession to detect when user navigates to a placeholder with SchemaAwareType, programmatically trigger ICompletionBroker.TriggerCompletion() with a filtered completion set in src/AkmlSql.Shell.Shared/Snippets/SnippetExpansionSession.cs
- [ ] T054 [US6] Create SnippetPlaceholderCompletionSource that provides schema-filtered completions based on the active placeholder's SchemaAwareType using the Phase 2 schema cache via engine request in src/AkmlSql.Shell.Shared/Snippets/SnippetPlaceholderCompletionSource.cs
- [ ] T055 [US6] Handle column context: when SchemaAwareType is "columns" and a preceding placeholder resolved to a table name, filter columns to that specific table in src/AkmlSql.Shell.Shared/Snippets/SnippetPlaceholderCompletionSource.cs
- [ ] T056 [US6] Handle no-connection fallback: when no database connection is active, schema-aware placeholders behave as regular text input (no IntelliSense triggered) in src/AkmlSql.Shell.Shared/Snippets/SnippetExpansionSession.cs

**Checkpoint**: Schema-aware placeholders show relevant IntelliSense suggestions during tab-stop navigation

---

## Phase 9: User Story 7 — Snippet Manager UI (Priority: P2)

**Goal**: Visual dialog to browse, search, create, edit, and delete snippets

**Independent Test**: Open Snippet Manager, browse tree, search, edit a snippet, verify changes persist

- [ ] T057 [US7] Create SnippetManagerDialog extending DialogWindow with programmatic WPF Grid layout: left pane (search bar + TreeView by source/category), right pane (SnippetEditorPanel + preview), bottom buttons (New, Import, Export, Delete, Close) in src/AkmlSql.Shell.Shared/Ui/SnippetManagerDialog.cs
- [ ] T058 [US7] Create SnippetManagerViewModel with snippet tree state, search filtering, selected snippet binding, CRUD command handlers in src/AkmlSql.Shell.Shared/Ui/SnippetManagerViewModel.cs
- [ ] T059 [US7] Implement full-text search: filter TreeView items matching query across name, shortcode, description, tags, and body content in src/AkmlSql.Shell.Shared/Ui/SnippetManagerViewModel.cs
- [ ] T060 [US7] Implement snippet tree: organize by source (Personal/Team/Built-in) → category (DML/DDL/DBA/ControlFlow/SurroundWith/Custom) → individual snippets with usage count badges in src/AkmlSql.Shell.Shared/Ui/SnippetManagerDialog.cs
- [ ] T061 [US7] Apply VS theme colors via EnvironmentColors + SetResourceReference for Snippet Manager dialog in src/AkmlSql.Shell.Shared/Ui/SnippetManagerDialog.cs
- [ ] T062 [US7] Wire Snippet Manager launch command (AKML SQL menu → Snippet Manager) in src/AkmlSql.Shell.Shared/Commands/

**Checkpoint**: Snippet Manager opens with full browse, search, CRUD, and preview

---

## Phase 10: User Story 8 — Built-in Variables (Priority: P2)

**Goal**: 14 built-in variables ($DATE$, $USER$, $DATABASE$, etc.) resolve automatically on expansion

**Independent Test**: Create snippet with $DATE$ and $DATABASE$, expand, verify current values appear

- [ ] T063 [US8] Complete BuiltInVariableResolver with all 14 variables: $CURSOR$, $SELECTEDTEXT$, $CLIPBOARD$, $DATE$, $DATETIME$, $TIME$, $USER$, $MACHINE$, $DATABASE$, $SERVER$, $SCHEMA$, $GUID$, $YEAR$, $FILENAME$ — using session context for connection-dependent variables in src/AkmlSql.Engine/Snippets/BuiltInVariableResolver.cs
- [ ] T064 [US8] Handle connection-dependent variable fallback: $DATABASE$, $SERVER$, $SCHEMA$ resolve to empty string when no connection is active in src/AkmlSql.Engine/Snippets/BuiltInVariableResolver.cs
- [ ] T065 [US8] Handle $CLIPBOARD$ resolution: add clipboard text to SnippetExpandRequest so the engine can resolve it (clipboard access requires UI thread, so shell sends it) in src/AkmlSql.Core/Ipc/Messages/SnippetExpandRequest.cs and src/AkmlSql.Shell.Shared/Snippets/SnippetTriggerHandler.cs

**Checkpoint**: All 14 built-in variables resolve correctly, including connection-dependent fallbacks

---

## Phase 11: User Story 9 — Multi-Source Snippet Library (Priority: P3)

**Goal**: Personal, Team, and Built-in snippet sources with priority-based conflict resolution and hot-reload

**Independent Test**: Configure team folder, add snippet, verify it appears; create personal snippet with same shortcode, verify personal wins

- [ ] T066 [US9] Create SnippetFileWatcher with one FileSystemWatcher per source folder, 200ms debounce timer, and fallback 30-second polling for team (network) folders in src/AkmlSql.Engine/Snippets/SnippetFileWatcher.cs
- [ ] T067 [US9] Implement graceful degradation for team folder: catch constructor exception if path unreachable, log warning, skip source, retry every 60 seconds in src/AkmlSql.Engine/Snippets/SnippetFileWatcher.cs
- [ ] T068 [US9] Wire SnippetFileWatcher to SnippetIndex: on file change (debounced), reload affected snippets and update index in src/AkmlSql.Engine/Snippets/SnippetFileWatcher.cs
- [ ] T069 [US9] Implement source priority in SnippetIndex.GetByShortcode: when multiple sources have same shortcode, return Personal (priority 1) over Team (priority 2) over Built-in (priority 3) in src/AkmlSql.Engine/Snippets/SnippetIndex.cs

**Checkpoint**: Three-source library with priority resolution and hot-reload working

---

## Phase 12: User Story 10 — Import from SQL Prompt and SSMS (Priority: P3)

**Goal**: Import snippets from SQL Prompt (.sqlpromptsnippet XML and JSON) and SSMS native (.snippet) formats

**Independent Test**: Import a SQL Prompt snippet, verify it appears in snippet library and expands correctly

- [ ] T070 [P] [US10] Create ImportVariableMapper with static mapping tables for SQL Prompt variables ($DBNAME$→$DATABASE$, $PASTE$→$CLIPBOARD$) and SSMS variables ($end$→$CURSOR$, $selected$→$SELECTEDTEXT$) in src/AkmlSql.Engine/Snippets/Import/ImportVariableMapper.cs
- [ ] T071 [P] [US10] Create SqlPromptXmlImporter that parses .sqlpromptsnippet XML (CodeSnippet schema), extracts Header/Declarations/Code, maps variables via ImportVariableMapper, outputs Snippet model in src/AkmlSql.Engine/Snippets/Import/SqlPromptXmlImporter.cs
- [ ] T072 [P] [US10] Create SqlPromptJsonImporter that parses SQL Prompt v10.5+ JSON format (body as \n string, placeholders array), maps variables, outputs Snippet model in src/AkmlSql.Engine/Snippets/Import/SqlPromptJsonImporter.cs
- [ ] T073 [P] [US10] Create SsmsSnippetImporter that parses .snippet VS CodeSnippet XML, maps $end$→$CURSOR$ and $selected$→$SELECTEDTEXT$, handles missing shortcode (derive from filename), outputs Snippet model in src/AkmlSql.Engine/Snippets/Import/SsmsSnippetImporter.cs
- [ ] T074 [US10] Implement auto-detection of import format: content-sniff for XML vs JSON, file extension for .sqlpromptsnippet vs .snippet vs .json in src/AkmlSql.Engine/Snippets/Import/ (factory method)
- [ ] T075 [US10] Implement bulk import: scan directory for all snippet files, detect formats, import each, generate summary report (imported/failed/reasons) in src/AkmlSql.Engine/Snippets/SnippetRequestHandler.cs
- [ ] T076 [US10] Implement SQL Prompt folder auto-detection: check %LocalAppData%\Red Gate\SQL Prompt *\Snippets\ paths, offer one-click migration in src/AkmlSql.Shell.Shared/Ui/SnippetManagerDialog.cs (Import button)
- [ ] T077 [US10] Wire SnippetImportRequest/SnippetImportResponse IPC flow in src/AkmlSql.Engine/Snippets/SnippetRequestHandler.cs

**Checkpoint**: SQL Prompt and SSMS snippets import successfully with variable mapping

---

## Phase 13: User Story 11 — Snippet Usage Statistics (Priority: P3)

**Goal**: Track usage frequency per snippet, display badges, use for ranking

**Independent Test**: Expand snippets, verify usage counts increment, verify most-used appear first in IntelliSense

- [ ] T078 [US11] Create SnippetUsageTracker that persists usage counts (snippet ID → count + lastUsed) to %AppData%/AKML SQL/cache/snippet-usage.json, loads on startup, saves on change (debounced) in src/AkmlSql.Engine/Snippets/SnippetUsageTracker.cs
- [ ] T079 [US11] Wire SnippetUsageTracker into SnippetExpander: after successful expansion, increment usage count for the expanded snippet in src/AkmlSql.Engine/Snippets/SnippetExpander.cs
- [ ] T080 [US11] Include usage counts in SnippetListResponse.SnippetInfo.UsageCount so Snippet Manager can display badges in src/AkmlSql.Engine/Snippets/SnippetRequestHandler.cs
- [ ] T081 [US11] Honor SnippetSettings.TrackUsage toggle: when disabled, do not record usage and fall back to alphabetical ranking in SnippetUsageTracker

**Checkpoint**: Usage stats tracked, displayed as badges, and used for ranking

---

## Phase 14: User Story 12 — Create Snippet from Selection (Priority: P3)

**Goal**: Right-click selected code → "Create Snippet from Selection" opens manager with pre-filled body

**Independent Test**: Select SQL, right-click, create snippet, verify code is pre-filled and snippet works

- [ ] T082 [US12] Create CreateSnippetFromSelectionCommand that gets selected text from ITextView.Selection, opens SnippetManagerDialog in "create new" mode with body pre-filled with the selected code in src/AkmlSql.Shell.Shared/Commands/CreateSnippetFromSelectionCommand.cs
- [ ] T083 [US12] Register CreateSnippetFromSelectionCommand in the editor right-click context menu for T-SQL content types in VSPackage command table
- [ ] T084 [US12] Implement variable marking in SnippetEditorPanel: user can highlight portions of pre-filled body and click "Make Variable" to create a placeholder with auto-generated variable definition in src/AkmlSql.Shell.Shared/Ui/SnippetEditorPanel.cs

**Checkpoint**: Create from selection flow works end-to-end

---

## Phase 15: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [ ] T085 Create PlaceholderAdornment that draws a subtle background highlight or box around the active placeholder during tab-stop navigation using IAdornmentLayer in src/AkmlSql.Shell.Shared/Snippets/PlaceholderAdornment.cs
- [ ] T086 [P] Extend src/AkmlSql.Installer/AkmlSqlSetup.iss to deploy built-in snippets (.akmlsnippet files) to <install>/snippets/ and create %AppData%/AKML SQL/snippets/ directory
- [ ] T087 [P] Register all snippet commands (Surround-With, Snippet Manager, Create from Selection) in .vsct command table for all 6 shell targets
- [ ] T088 Verify snippet expansion works across all 6 IDE targets (SSMS 20/21/22, VS 2019/2022/2026)
- [ ] T089 Run quickstart.md validation: verify all build commands and development workflow steps
- [ ] T090 Performance validation: measure expansion latency (<20ms), search latency (<50ms), tab-stop navigation (<10ms) per spec targets

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories
- **US1 Expansion (Phase 3)**: Depends on Foundational — core expansion MVP
- **US2 Built-in Library (Phase 4)**: Depends on US1 (uses SnippetLoader)
- **US3 IntelliSense Integration (Phase 5)**: Depends on US1 (SnippetIndex) + US2 (snippets to show)
- **US4 Custom Creation (Phase 6)**: Depends on US1 (expansion) + Foundational (CRUD)
- **US5 Surround-With (Phase 7)**: Depends on US1 (expansion) + US2 (surround-with snippets)
- **US6 Schema-Aware (Phase 8)**: Depends on US1 (tab-stop session)
- **US7 Manager UI (Phase 9)**: Depends on US4 (CRUD operations)
- **US8 Built-in Variables (Phase 10)**: Depends on US1 (BuiltInVariableResolver)
- **US9 Multi-Source (Phase 11)**: Depends on US1 (SnippetLoader + SnippetIndex)
- **US10 Import (Phase 12)**: Depends on US4 (save to personal folder)
- **US11 Usage Stats (Phase 13)**: Depends on US1 (expansion) + US3 (ranking)
- **US12 Create from Selection (Phase 14)**: Depends on US7 (manager dialog)
- **Polish (Phase 15)**: Depends on all desired user stories

### User Story Dependencies

```
Phase 1 (Setup) → Phase 2 (Foundational) → Phase 3 (US1: Expansion) ──┐
                                                                        │
Phase 3 (US1) → Phase 4 (US2: Built-in Library) → Phase 5 (US3: IntelliSense)
             → Phase 6 (US4: Custom Creation) → Phase 9 (US7: Manager UI)
             →                                    → Phase 14 (US12: Create from Selection)
             → Phase 7 (US5: Surround-With)
             → Phase 8 (US6: Schema-Aware)
             → Phase 10 (US8: Built-in Variables)
             → Phase 11 (US9: Multi-Source)
             → Phase 12 (US10: Import)
             → Phase 13 (US11: Usage Stats)
```

### Parallel Opportunities

- Phase 1: T001-T004 (directory creation) can run in parallel
- Phase 2: T005-T009 (IPC messages) all parallel; T011-T014 (models) all parallel
- Phase 4: T032-T036 (built-in snippet files) all parallel
- Phase 12: T070-T073 (importers) all parallel
- US2, US4, US5, US6, US8, US9, US10, US11 can start in parallel after US1

---

## Parallel Example: Phase 4 (US2 — Built-in Library)

```
# All snippet file creation tasks are independent:
Task: T032 "Create 14 DML snippets in src/AkmlSql.Engine/Snippets/BuiltIn/"
Task: T033 "Create 15 DDL snippets in src/AkmlSql.Engine/Snippets/BuiltIn/"
Task: T034 "Create 20 DBA snippets in src/AkmlSql.Engine/Snippets/BuiltIn/"
Task: T035 "Create 10 Control Flow snippets in src/AkmlSql.Engine/Snippets/BuiltIn/"
Task: T036 "Create 10 Surround-With snippets in src/AkmlSql.Engine/Snippets/BuiltIn/"
```

---

## Implementation Strategy

### MVP First (Phase 1 + Phase 2 + Phase 3)

1. Complete Phase 1: Setup (4 tasks)
2. Complete Phase 2: Foundational — IPC, models, loader, index, engine routing (16 tasks)
3. Complete Phase 3: US1 — Snippet expansion with 6 basic snippets (11 tasks)
4. **STOP and VALIDATE**: Type `ssf` + Tab → expansion → Tab through placeholders → commit
5. Deploy/demo if ready — core snippet system works

### Incremental Delivery

1. Setup + Foundational → Infrastructure ready
2. US1 (Expansion) → Core expansion works → **MVP**
3. US2 (Built-in Library) + US3 (IntelliSense) → 75+ snippets in popup → **Beta 1**
4. US4 (Custom) + US5 (Surround-With) + US8 (Variables) → User-created snippets → **Beta 2**
5. US6 (Schema-Aware) + US7 (Manager UI) → Power features → **Beta 3**
6. US9 (Multi-Source) + US10 (Import) + US11 (Stats) + US12 (Create from Selection) → Complete → **Release**

### Parallel Team Strategy

With multiple developers after Foundational:
- Developer A: US1 → US5 → US6 → US8
- Developer B: US2 → US3 → US11
- Developer C: US4 → US7 → US12
- Developer D: US9 → US10 (independent paths)

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story
- No new projects needed — snippets are a module within existing engine + shell
- MVP = 31 tasks (Phase 1 + Phase 2 + Phase 3)
- Total = 90 tasks across 15 phases
- 24 tasks marked [P] for parallel execution
