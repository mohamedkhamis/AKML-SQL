# Tasks: SQL Formatter & Code Beautifier

**Input**: Design documents from `/specs/003-sql-formatter/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Create new projects, configure dependencies, establish project structure

- [ ] T001 Create AkmlSql.Formatting class library project in src/AkmlSql.Formatting/AkmlSql.Formatting.csproj targeting net10.0 with references to ScriptDom 170.191.0 and System.Text.Json 8.x
- [ ] T002 Create AkmlSql.Formatter CLI project in src/AkmlSql.Formatter/AkmlSql.Formatter.csproj targeting net10.0 with self-contained win-x64, PublishTrimmed, referencing AkmlSql.Formatting and DiffPlex 1.7.x
- [ ] T003 Create AkmlSql.Formatting.Tests test project in tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj targeting net10.0 with xunit references
- [ ] T004 Add AkmlSql.Formatting project reference to src/AkmlSql.Engine/AkmlSql.Engine.csproj
- [ ] T005 [P] Create directory structure for AkmlSql.Formatting: Pipeline/, Layout/, Rules/, Profiles/, Profiles/BuiltIn/, Actions/, Selection/, CamelCase/
- [ ] T006 [P] Create directory structure for AkmlSql.Formatter: Commands/, Output/
- [ ] T007 [P] Create directory structure for Shell.Shared extensions: src/AkmlSql.Shell.Shared/Formatting/, update AkmlSql.Shell.Shared.projitems
- [ ] T008 Update AKML-SQL.slnx solution file to include AkmlSql.Formatting, AkmlSql.Formatter, and AkmlSql.Formatting.Tests projects

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

### IPC Message Types

- [ ] T009 [P] Create FormatRequest and FormatResponse MessagePack message types in src/AkmlSql.Core/Ipc/Messages/FormatRequest.cs and FormatResponse.cs per format-protocol-extension.md contract
- [ ] T010 [P] Create FormatSelectionRequest and FormatSelectionResponse message types in src/AkmlSql.Core/Ipc/Messages/FormatSelectionRequest.cs and FormatSelectionResponse.cs
- [ ] T011 [P] Create FormatPreviewRequest and FormatPreviewResponse message types in src/AkmlSql.Core/Ipc/Messages/FormatPreviewRequest.cs and FormatPreviewResponse.cs
- [ ] T012 [P] Create FormatActionRequest and FormatActionResponse message types with FormatActionType enum in src/AkmlSql.Core/Ipc/Messages/FormatActionRequest.cs and FormatActionResponse.cs
- [ ] T013 [P] Create ProfileListRequest and ProfileListResponse with ProfileInfo in src/AkmlSql.Core/Ipc/Messages/ProfileListRequest.cs and ProfileListResponse.cs
- [ ] T014 [P] Create ProfileSaveRequest, ProfileSaveResponse, ProfileDeleteRequest, ProfileDeleteResponse in src/AkmlSql.Core/Ipc/Messages/ProfileSaveRequest.cs (and siblings)
- [ ] T015 [P] Create ProfileImportRequest and ProfileImportResponse in src/AkmlSql.Core/Ipc/Messages/ProfileImportRequest.cs and ProfileImportResponse.cs
- [ ] T016 [P] Create BulkFormatRequest, BulkFormatProgressInfo, BulkFormatReportResponse, BulkFormatCancelRequest in src/AkmlSql.Core/Ipc/Messages/BulkFormatRequest.cs (and siblings)
- [ ] T017 [P] Create FormatDiagnosticInfo and FileResult shared types in src/AkmlSql.Core/Ipc/Messages/FormatDiagnosticInfo.cs and FileResult.cs

### Profile Model & Serialization

- [ ] T018 [P] Create FormattingProfile model with all option category classes (WhitespaceOptions, CasingOptions, ListOptions, ParenthesisOptions, DmlOptions, JoinOptions, DdlOptions, ControlFlowOptions, CaseOptions, CteOptions, ExpressionOptions, FormatActionConfig) in src/AkmlSql.Formatting/Profiles/FormattingProfile.cs per profile-schema.md
- [ ] T019 [P] Create ProfileMetadata model with Id, SchemaVersion, Name, Description, Author, Version, Created, Modified, BasedOn, IsBuiltIn in src/AkmlSql.Formatting/Profiles/ProfileMetadata.cs
- [ ] T020 Create ProfileSerializer with System.Text.Json source generators (FormattingProfileJsonContext) for AOT-compatible serialization with [JsonExtensionData] for forward compatibility in src/AkmlSql.Formatting/Profiles/ProfileSerializer.cs
- [ ] T021 Create ProfileManager with Load, Save, List, Delete, Duplicate, GetBuiltIn methods and profile storage path resolution in src/AkmlSql.Formatting/Profiles/ProfileManager.cs

### Formatting Pipeline Skeleton

- [ ] T022 Create FormatterPipeline orchestrator that chains 6 stages (Parse → Annotate → Layout → Casing → Emit → Validate) and returns FormatResult in src/AkmlSql.Formatting/Pipeline/FormatterPipeline.cs
- [ ] T023 [P] Create FormatResult model with Success, FormattedText, WasModified, ValidationPassed, Diagnostics, ElapsedMs in src/AkmlSql.Formatting/Pipeline/FormatterPipeline.cs (or separate FormatResult.cs)
- [ ] T024 [P] Create FormatDiagnostic model with Severity enum, Message, Offset, Line in src/AkmlSql.Formatting/Pipeline/FormatDiagnostic.cs
- [ ] T025 [P] Create LayoutNode model with TokenIndex, TokenType, OriginalText, FormattedText, IndentLevel, PrecedingBreak, PrecedingSpaces, TrailingComment, IsInNoformatRegion in src/AkmlSql.Formatting/Layout/LayoutNode.cs
- [ ] T026 [P] Create CommentAttachment model with TokenIndex, Text, AttachmentType enum (Trailing, Leading, Standalone) in src/AkmlSql.Formatting/Layout/CommentAttachment.cs
- [ ] T027 [P] Create IRuleSet interface in src/AkmlSql.Formatting/Rules/IRuleSet.cs defining Apply(LayoutNode[], FormattingProfile) method

### Engine Integration

- [ ] T028 Create FormatRequestHandler in src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs that invokes FormatterPipeline for FormatRequest, FormatSelectionRequest, FormatPreviewRequest, and FormatActionRequest messages
- [ ] T029 Extend PipeRpcServer message dispatch in src/AkmlSql.Engine/Server/PipeRpcServer.cs to route MessageTypes 10-19 to FormatRequestHandler and 14-17 to ProfileManager

### Configuration

- [ ] T030 Extend AppSettings in src/AkmlSql.Core/Config/AppSettings.cs with FormatterSettings class containing Enabled, ActiveProfile, FormatOnPaste, FormatOnSave, FormatOnDelimiter, ShortcutKey, ShowProfileInStatusBar, ConfirmBulkFormat, CreateBackups, RespectNoformat, HandleParseErrors, SemanticValidation

**Checkpoint**: Foundation ready — formatting pipeline skeleton, profile system, IPC messages, and engine integration in place

---

## Phase 3: User Story 1 — One-Click Format Document (Priority: P1) MVP

**Goal**: User presses Ctrl+K, Y and the entire SQL document is reformatted according to the active profile

**Independent Test**: Open any SQL file, trigger format, verify output matches Default profile rules and is semantically identical

### Pipeline Core Implementation

- [ ] T031 [US1] Implement Stage 1 (Parse) in FormatterPipeline: call TSqlParser.Create(SqlVersion.Sql170).Parse(), handle parse errors, extract ScriptTokenStream in src/AkmlSql.Formatting/Pipeline/FormatterPipeline.cs
- [ ] T032 [US1] Implement AstAnnotator (Stage 2) that builds CommentAttachment map by scanning ScriptTokenStream and classifying comments as Trailing/Leading/Standalone in src/AkmlSql.Formatting/Pipeline/AstAnnotator.cs
- [ ] T033 [US1] Implement LayoutEngine (Stage 3) that walks AST via TSqlFragmentVisitor, applies formatting rules per node type, and produces List<LayoutNode> in src/AkmlSql.Formatting/Pipeline/LayoutEngine.cs
- [ ] T034 [US1] Implement CasingEngine (Stage 4) that applies casing rules to each token based on TokenType and profile casing options in src/AkmlSql.Formatting/Pipeline/CasingEngine.cs
- [ ] T035 [US1] Implement TextEmitter (Stage 5) that serializes LayoutNode list to formatted string with correct whitespace, newlines, and indentation in src/AkmlSql.Formatting/Pipeline/TextEmitter.cs
- [ ] T036 [US1] Implement SemanticValidator (Stage 6) that re-parses formatted output, generates normalized scripts from both ASTs via Sql170ScriptGenerator, and compares for equivalence in src/AkmlSql.Formatting/Pipeline/SemanticValidator.cs

### Core Rules (Minimal Set for MVP)

- [ ] T037 [P] [US1] Implement WhitespaceRules with tabStyle, tabSize, lineBreakBeforeClause, emptyLineBetweenStatements, trailingWhitespace, finalNewline, spaceAfterComma, spaceAroundOperators in src/AkmlSql.Formatting/Rules/WhitespaceRules.cs
- [ ] T038 [P] [US1] Implement CasingRules with reservedKeywords and builtInFunctions casing (5 modes: UPPERCASE, lowercase, PascalCase, camelCase, AsIs) in src/AkmlSql.Formatting/Rules/CasingRules.cs
- [ ] T039 [P] [US1] Implement DmlRules with selectItemsOnNewLine, fromOnNewLine, whereOnNewLine, andOrNewLine, groupByOnNewLine, orderByOnNewLine in src/AkmlSql.Formatting/Rules/DmlRules.cs
- [ ] T040 [P] [US1] Implement JoinRules with onNewLine, onConditionNewLine, onConditionIndent in src/AkmlSql.Formatting/Rules/JoinRules.cs

### Layout Helpers

- [ ] T041 [P] [US1] Implement IndentationTracker that maintains current indent depth based on AST nesting in src/AkmlSql.Formatting/Layout/IndentationTracker.cs
- [ ] T042 [P] [US1] Implement LineBreakDecider that determines PrecedingBreak for each token based on rule set and AST context in src/AkmlSql.Formatting/Layout/LineBreakDecider.cs

### Built-in Default Profile

- [ ] T043 [US1] Create Default built-in profile JSON (default.akmlstyle) with all options set to their default values per profile-schema.md in src/AkmlSql.Formatting/Profiles/BuiltIn/default.akmlstyle

### Shell Integration

- [ ] T044 [US1] Implement FormatDocumentCommand that sends FormatRequest to engine via PipeRpcClient and applies result to editor text buffer in src/AkmlSql.Shell.Shared/Formatting/FormatDocumentCommand.cs
- [ ] T045 [US1] Register FormatDocumentCommand with Ctrl+K, Y keyboard shortcut in the VSPackage command table (.vsct) and wire to shell command infrastructure

### End-to-End Validation

- [ ] T046 [US1] Wire FormatterPipeline end-to-end: raw SQL input → Parse → Annotate → Layout → Casing → Emit → Validate → FormatResult output, verify with simple SELECT/FROM/WHERE test case

**Checkpoint**: Ctrl+K, Y formats a SQL document with the Default profile. Core pipeline works end-to-end.

---

## Phase 4: User Story 2 — Format Selection (Priority: P1)

**Goal**: User selects a portion of SQL and formats only the selection

**Independent Test**: Select a single statement in a multi-statement file, format selection, verify only selected portion changes

- [ ] T047 [US2] Implement SelectionFormatter that finds the smallest enclosing TSqlFragment for a given offset range, formats it via FormatterPipeline, and splices the result back in src/AkmlSql.Formatting/Selection/SelectionFormatter.cs
- [ ] T048 [US2] Implement FormatSelectionCommand that sends FormatSelectionRequest to engine and applies result to the selected text range in src/AkmlSql.Shell.Shared/Formatting/FormatSelectionCommand.cs
- [ ] T049 [US2] Register FormatSelectionCommand with Ctrl+K, F keyboard shortcut in VSPackage command table

**Checkpoint**: Ctrl+K, F formats only the selected text

---

## Phase 5: User Story 3 — Predefined Formatting Profiles (Priority: P1)

**Goal**: 5 built-in profiles available for quick switching via toolbar dropdown

**Independent Test**: Switch between profiles, format the same SQL, verify each produces visually distinct output

### Built-in Profiles

- [ ] T050 [P] [US3] Create Compact built-in profile (compact.akmlstyle) with minimized vertical space settings in src/AkmlSql.Formatting/Profiles/BuiltIn/compact.akmlstyle
- [ ] T051 [P] [US3] Create Expanded built-in profile (expanded.akmlstyle) with maximum readability settings and leading commas variant in src/AkmlSql.Formatting/Profiles/BuiltIn/expanded.akmlstyle
- [ ] T052 [P] [US3] Create Leading Commas built-in profile (leading-commas.akmlstyle) in src/AkmlSql.Formatting/Profiles/BuiltIn/leading-commas.akmlstyle
- [ ] T053 [P] [US3] Create Minimalist built-in profile (minimalist.akmlstyle) with casing + whitespace cleanup only in src/AkmlSql.Formatting/Profiles/BuiltIn/minimalist.akmlstyle

### Profile Switching UI

- [ ] T054 [US3] Implement ProfileSelectorDropdown as a toolbar combo box that lists available profiles and sends ProfileListRequest to engine, sets active profile on selection in src/AkmlSql.Shell.Shared/Ui/ProfileSelectorDropdown.cs
- [ ] T055 [US3] Extend StatusBar to show active profile name indicator in src/AkmlSql.Shell.Shared/StatusBar/ (extend existing status bar infrastructure)
- [ ] T056 [US3] Wire ProfileManager.GetBuiltInProfiles() to load embedded .akmlstyle resources from the BuiltIn/ directory in src/AkmlSql.Formatting/Profiles/ProfileManager.cs

**Checkpoint**: 5 built-in profiles available, quick-switch via toolbar, status bar shows active profile

---

## Phase 6: User Story 5 — 250+ Formatting Options (Priority: P2)

**Goal**: Full rule coverage across all 8 option categories

**Independent Test**: Toggle individual options, format SQL, verify each option produces the expected change

### Complete Rule Implementations

- [ ] T057 [P] [US5] Complete WhitespaceRules with all remaining options: indentStyle, maxLineWidth, lineBreakAfterClause, lineBreakBeforeComma, lineBreakAfterComma, emptyLineBeforeGO, emptyLineAfterGO, preserveEmptyLines, maxConsecutiveEmptyLines, spaceAroundBooleanOperators, spaceInsideParentheses, spaceBeforeParentheses, lineBreakAfterSemicolon in src/AkmlSql.Formatting/Rules/WhitespaceRules.cs
- [ ] T058 [P] [US5] Complete CasingRules with builtInDataTypes, systemObjects, globalVariables, localVariables, identifiers casing in src/AkmlSql.Formatting/Rules/CasingRules.cs
- [ ] T059 [P] [US5] Implement ListRules with commaPosition, alignItemsAcrossClauses, alignAliases, oneItemPerLine, collapseShortLists, collapseThreshold, indentListItems, alignDataTypesInDDL, alignValuesInInsert in src/AkmlSql.Formatting/Rules/ListRules.cs
- [ ] T060 [P] [US5] Implement ParenthesisRules with openOnSameLine, closeOnNewLine, collapseShort, collapseThreshold, indentContents, spaceInside, removeRedundant, createTableColumns, procedureParameters, subqueryStyle in src/AkmlSql.Formatting/Rules/ParenthesisRules.cs
- [ ] T061 [P] [US5] Complete DmlRules with all remaining options: selectStarOnSameLine, topOnSameLine, distinctOnSameLine, andOrIndent, havingOnNewLine, intoOnNewLine, valuesOnNewLine, setOnNewLine, deleteFromOnSameLine, mergeWhenOnNewLine, collapseShortStatements, collapseThreshold, collapseShortSubqueries, subqueryCollapseThreshold in src/AkmlSql.Formatting/Rules/DmlRules.cs
- [ ] T062 [P] [US5] Complete JoinRules with all remaining options: indentJoin, multipleOnConditions, emptyLineBeforeJoin, alignJoinKeyword, joinTypeStyle, crossApplyNewLine in src/AkmlSql.Formatting/Rules/JoinRules.cs
- [ ] T063 [P] [US5] Implement DdlRules with createTableColumnsOnNewLine, alignDataTypes, alignConstraints, constraintsOnNewLine, inlineConstraintStyle, tableConstraintsSeparate, firstParameterOnNewLine, parameterAlignment, alignParameterDataTypes, alignParameterDefaults, asOnNewLine, beginOnNewLine, collapseShortDDL, collapseThreshold in src/AkmlSql.Formatting/Rules/DdlRules.cs
- [ ] T064 [P] [US5] Implement ControlFlowRules with beginOnNewLine, endOnNewLine, indentBetweenBeginEnd, collapseShortIfElse, elseOnNewLine, elseAlignWithIf, tryCatchOnNewLine + CASE rules (whenOnNewLine, thenOnNewLine, endOnNewLine, indentWhen, alignThen, collapseShortCase) + CTE rules (withOnNewLine, cteBodyIndent, commaBeforeCte, emptyLineBetweenCtes) + expression rules (booleanOperatorNewLine, betweenOnOneLine, inListStyle, existsSubqueryIndent) in src/AkmlSql.Formatting/Rules/ControlFlowRules.cs

### Layout Helpers (Complete)

- [ ] T065 [P] [US5] Implement AlignmentCalculator for vertical alignment of aliases, data types, constraints in column lists in src/AkmlSql.Formatting/Layout/AlignmentCalculator.cs
- [ ] T066 [P] [US5] Implement CollapseEvaluator that measures formatted length of AST subtrees and collapses short constructs below threshold in src/AkmlSql.Formatting/Layout/CollapseEvaluator.cs

**Checkpoint**: All 250+ formatting options functional, each producing correct output when toggled

---

## Phase 7: User Story 7 — Noformat Regions (Priority: P2)

**Goal**: `--noformat` / `--endnoformat` tags preserve enclosed text exactly

**Independent Test**: Place noformat tags around SQL, format, verify tagged content is byte-for-byte identical

- [ ] T067 [US7] Implement NoformatScanner that pre-scans token stream for noformat tags (line and block comment variants), builds sorted NoformatRegion list, handles unmatched tags and nesting in src/AkmlSql.Formatting/Pipeline/NoformatScanner.cs
- [ ] T068 [US7] Integrate NoformatScanner into FormatterPipeline as Stage 0a (before parsing), pass NoformatRegion list to AstAnnotator and TextEmitter in src/AkmlSql.Formatting/Pipeline/FormatterPipeline.cs
- [ ] T069 [US7] Update TextEmitter to check IsInNoformatRegion flag and emit OriginalText verbatim for tokens inside noformat regions in src/AkmlSql.Formatting/Pipeline/TextEmitter.cs
- [ ] T070 [US7] Implement SqlcmdPreprocessor (Stage 0b) that replaces SQLCMD line directives with sentinel comments and inline $(Var) with placeholder identifiers, only outside noformat regions, with post-processing restore in src/AkmlSql.Formatting/Pipeline/SqlcmdPreprocessor.cs

**Checkpoint**: Noformat regions and SQLCMD directives are preserved perfectly during formatting

---

## Phase 8: User Story 6 — Casing Rules with Database Sync (Priority: P2)

**Goal**: Casing rules for all token types, plus identifier sync with database catalog

**Independent Test**: Format SQL with various casing rules, verify correct transformation; with active connection, verify identifier casing matches catalog

- [ ] T071 [US6] Extend CasingEngine to accept optional DatabaseCache reference from Phase 2 schema cache for identifier sync when casing.syncWithDatabase is true in src/AkmlSql.Formatting/Pipeline/CasingEngine.cs
- [ ] T072 [US6] Implement CamelCaseDictionary with word boundary detection for splitting compound identifiers (e.g., customerorderid → CustomerOrderId) using common English word list in src/AkmlSql.Formatting/CamelCase/CamelCaseDictionary.cs
- [ ] T073 [US6] Integrate CamelCaseDictionary into CasingEngine when casing.camelCaseDictionary is true in src/AkmlSql.Formatting/Pipeline/CasingEngine.cs

**Checkpoint**: All 10 casing options work correctly; database sync matches catalog identifiers

---

## Phase 9: User Story 8 — Standalone Format Actions (Priority: P2)

**Goal**: Individual formatting actions (casing only, insert semicolons, expand wildcards, etc.) run independently

**Independent Test**: Trigger each action individually, verify it applies only its specific transformation

- [ ] T074 [P] [US8] Create IFormatAction interface with Execute(string sql, FormattingProfile profile, DatabaseCache? cache) returning FormatResult in src/AkmlSql.Formatting/Actions/IFormatAction.cs
- [ ] T075 [P] [US8] Implement CasingOnlyAction that applies casing rules without layout changes in src/AkmlSql.Formatting/Actions/CasingOnlyAction.cs
- [ ] T076 [P] [US8] Implement InsertSemicolonsAction that adds missing statement terminators by walking AST statement nodes in src/AkmlSql.Formatting/Actions/InsertSemicolonsAction.cs
- [ ] T077 [P] [US8] Implement RemoveSemicolonsAction in src/AkmlSql.Formatting/Actions/RemoveSemicolonsAction.cs
- [ ] T078 [P] [US8] Implement ExpandWildcardsAction that replaces SELECT * with explicit column list from DatabaseCache in src/AkmlSql.Formatting/Actions/ExpandWildcardsAction.cs
- [ ] T079 [P] [US8] Implement QualifyObjectNamesAction that adds schema prefixes using DatabaseCache in src/AkmlSql.Formatting/Actions/QualifyObjectNamesAction.cs
- [ ] T080 [P] [US8] Implement ToggleBracketsAction that adds/removes square brackets on identifiers in src/AkmlSql.Formatting/Actions/ToggleBracketsAction.cs
- [ ] T081 [P] [US8] Implement ToggleAsKeywordAction that adds/removes AS on alias definitions in src/AkmlSql.Formatting/Actions/ToggleAsKeywordAction.cs

### Shell Commands for Actions

- [ ] T082 [P] [US8] Implement CasingOnlyCommand (Ctrl+B, Ctrl+U) in src/AkmlSql.Shell.Shared/Formatting/CasingOnlyCommand.cs
- [ ] T083 [P] [US8] Implement InsertSemicolonsCommand (Ctrl+B, Ctrl+S) in src/AkmlSql.Shell.Shared/Formatting/InsertSemicolonsCommand.cs
- [ ] T084 [P] [US8] Implement ExpandWildcardsCommand (Ctrl+B, Ctrl+W) in src/AkmlSql.Shell.Shared/Formatting/ExpandWildcardsCommand.cs
- [ ] T085 [P] [US8] Implement QualifyNamesCommand (Ctrl+B, Ctrl+Q) in src/AkmlSql.Shell.Shared/Formatting/QualifyNamesCommand.cs
- [ ] T086 [P] [US8] Implement ToggleBracketsCommand (Ctrl+B, Ctrl+B) and ToggleAsCommand (Ctrl+B, Ctrl+A) in src/AkmlSql.Shell.Shared/Formatting/ToggleBracketsCommand.cs and ToggleAsCommand.cs
- [ ] T087 [US8] Register all standalone action commands in VSPackage command table (.vsct) with keyboard shortcuts

**Checkpoint**: All 8 standalone actions work independently; configurable inclusion in full Format SQL

---

## Phase 10: User Story 4 — Custom Formatting Profiles (Priority: P2)

**Goal**: Users can create, edit, duplicate, export, import, delete, and compare custom profiles

**Independent Test**: Create a custom profile, change options, export it, import on another path, verify round-trip

- [ ] T088 [US4] Implement profile Create (from scratch or copy), Save, Delete operations in ProfileManager with file I/O to %AppData%/AKML SQL/profiles/ in src/AkmlSql.Formatting/Profiles/ProfileManager.cs
- [ ] T089 [US4] Implement profile Export (copy .akmlstyle to user-chosen path) and Import (copy .akmlstyle from user-chosen path, assign new GUID) in ProfileManager in src/AkmlSql.Formatting/Profiles/ProfileManager.cs
- [ ] T090 [US4] Implement ProfileDiffer that compares two FormattingProfile objects and returns a list of differing options with old/new values in src/AkmlSql.Formatting/Profiles/ProfileDiffer.cs
- [ ] T091 [US4] Wire profile CRUD operations through engine IPC: ProfileSaveRequest, ProfileDeleteRequest, ProfileListRequest handlers in src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs
- [ ] T092 [US4] Implement built-in profile protection: prevent Save/Delete on profiles where IsBuiltIn=true, offer Duplicate instead in ProfileManager

**Checkpoint**: Full profile CRUD lifecycle works; profiles persist as .akmlstyle files

---

## Phase 11: User Story 9 — Auto-Format Triggers (Priority: P3)

**Goal**: Format-on-paste, format-on-save, format-on-delimiter triggers

**Independent Test**: Enable each trigger, perform the action, verify formatting is applied automatically

- [ ] T093 [P] [US9] Implement FormatOnPasteHandler that intercepts clipboard paste, detects SQL content via keyword heuristic (first 200 chars), formats if SQL, skips if not in src/AkmlSql.Shell.Shared/Formatting/FormatOnPasteHandler.cs
- [ ] T094 [P] [US9] Implement FormatOnSaveHandler that hooks IVsRunningDocumentTable.OnBeforeSave and formats .sql files before save in src/AkmlSql.Shell.Shared/Formatting/FormatOnSaveHandler.cs
- [ ] T095 [P] [US9] Implement FormatOnDelimiterHandler that detects `;` or `GO` keystroke, identifies the completed statement, and formats it in src/AkmlSql.Shell.Shared/Formatting/FormatOnDelimiterHandler.cs
- [ ] T096 [US9] Wire all auto-format handlers to FormatterSettings toggles (disabled by default) and register/unregister based on config changes in src/AkmlSql.Shell.Shared/Formatting/ handlers

**Checkpoint**: All 3 auto-format triggers work when enabled; all disabled by default

---

## Phase 12: User Story 10 — Profile Editor with Live Preview (Priority: P3)

**Goal**: Visual split-pane profile editor with live preview, search, undo/redo

**Independent Test**: Open profile editor, change options, verify preview updates in real-time, save, verify profile persists

### Profile Editor Dialog

- [ ] T097 [US10] Create ProfileEditorDialog extending DialogWindow with programmatic WPF Grid layout: left pane (TreeView + options), right pane (before/after preview + your-code preview), bottom buttons (Cancel, Save, Save & Apply) in src/AkmlSql.Shell.Shared/Ui/ProfileEditorDialog.cs
- [ ] T098 [US10] Create ProfileEditorViewModel with property change notifications, undo/redo stack (List<ProfileSnapshot>), current profile state, and preview text in src/AkmlSql.Shell.Shared/Ui/ProfileEditorViewModel.cs
- [ ] T099 [US10] Create OptionCategoryTreeBuilder that builds TreeView items for all 8 option categories with expandable subcategories in src/AkmlSql.Shell.Shared/Ui/OptionCategoryTreeBuilder.cs
- [ ] T100 [US10] Create dynamic option controls: generate CheckBox for bool options, ComboBox for enum options, NumericUpDown for int options, each bound to ProfileEditorViewModel in src/AkmlSql.Shell.Shared/Ui/ProfileEditorDialog.cs (option panel builder method)
- [ ] T101 [US10] Create SqlPreviewRenderer using RichTextBox with FlowDocument and syntax-colored Run elements (keywords=blue, strings=red, comments=green) in src/AkmlSql.Shell.Shared/Ui/SqlPreviewRenderer.cs
- [ ] T102 [US10] Implement live preview: on any option change, send FormatPreviewRequest to engine with current profile state, update preview pane within 100ms in src/AkmlSql.Shell.Shared/Ui/ProfileEditorViewModel.cs
- [ ] T103 [US10] Implement option search: TextBox at top of left pane, filter/highlight matching options by name/description in src/AkmlSql.Shell.Shared/Ui/ProfileEditorDialog.cs
- [ ] T104 [US10] Extend ThemeManager with EnvironmentColors resource keys for profile editor (ToolWindowBackground, ToolWindowText, ToolWindowBorder, ButtonFace, ButtonText) in src/AkmlSql.Shell.Shared/Ui/ThemeManager.cs
- [ ] T105 [US10] Implement per-category Reset and full Reset to base profile defaults in ProfileEditorViewModel in src/AkmlSql.Shell.Shared/Ui/ProfileEditorViewModel.cs
- [ ] T106 [US10] Wire profile editor launch command (AKML SQL menu → Edit Profile / Options command) in src/AkmlSql.Shell.Shared/Commands/

**Checkpoint**: Profile editor opens, all 250+ options visible with live preview, undo/redo, search, save

---

## Phase 13: User Story 11 — Bulk File Formatting (Priority: P3)

**Goal**: Format entire directories of SQL files with reporting

**Independent Test**: Run bulk format against a test directory, verify all files formatted, report generated

- [ ] T107 [US11] Create BulkFormatWizard dialog (programmatic WPF) with source selection (directory/file list), profile picker, mode (format/preview/output dir), backup checkbox in src/AkmlSql.Shell.Shared/Ui/BulkFormatWizard.cs
- [ ] T108 [US11] Create BulkFormatProgressDialog showing progress bar, current file, count in src/AkmlSql.Shell.Shared/Ui/BulkFormatProgressDialog.cs
- [ ] T109 [US11] Implement bulk format engine logic using Parallel.ForEachAsync with per-file read-format-write pipeline, configurable parallelism, ConcurrentBag<FileFormatResult> collection in src/AkmlSql.Formatting/Pipeline/BulkFormatter.cs
- [ ] T110 [US11] Implement BulkFormatReport JSON generation with timestamp, profile, total/formatted/error counts, per-file details in src/AkmlSql.Formatting/Pipeline/BulkFormatter.cs
- [ ] T111 [US11] Wire BulkFormatRequest/BulkFormatProgress/BulkFormatReportResponse IPC flow through engine in src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs
- [ ] T112 [US11] Implement backup (.bak) creation before modifying files and read-only file skip in BulkFormatter in src/AkmlSql.Formatting/Pipeline/BulkFormatter.cs

**Checkpoint**: Bulk format wizard formats directory of files, shows progress, generates report

---

## Phase 14: User Story 12 — Command-Line Formatter (Priority: P3)

**Goal**: Standalone CLI tool for CI/CD integration with format, check, diff, pipe modes

**Independent Test**: Run CLI against SQL files in all modes, verify correct output and exit codes

- [ ] T113 [US12] Implement CLI argument parser in Program.cs with --file, --directory, --recursive, --profile, --profile-file, --check, --diff, --stdin, --stdout, --report, --list-profiles, --parallel flags in src/AkmlSql.Formatter/Program.cs
- [ ] T114 [P] [US12] Implement FormatCommand that formats files in-place using FormatterPipeline and ProfileManager in src/AkmlSql.Formatter/Commands/FormatCommand.cs
- [ ] T115 [P] [US12] Implement CheckCommand that validates formatting without modifying files, returns exit code 0 (formatted) or 1 (violations) in src/AkmlSql.Formatter/Commands/CheckCommand.cs
- [ ] T116 [P] [US12] Implement DiffCommand that computes diff via DiffPlex InlineDiffBuilder and outputs via UnifiedDiffFormatter in src/AkmlSql.Formatter/Commands/DiffCommand.cs
- [ ] T117 [P] [US12] Implement UnifiedDiffFormatter that converts DiffPlex DiffPaneModel to standard unified diff format with --- / +++ / @@ headers in src/AkmlSql.Formatter/Output/UnifiedDiffFormatter.cs
- [ ] T118 [P] [US12] Implement ReportGenerator for JSON bulk format reports in src/AkmlSql.Formatter/Output/ReportGenerator.cs
- [ ] T119 [P] [US12] Implement ConsoleRenderer for colored console output (formatted/error/diff highlighting) in src/AkmlSql.Formatter/Output/ConsoleRenderer.cs
- [ ] T120 [US12] Implement ProfileCommand for --list-profiles and profile comparison from CLI in src/AkmlSql.Formatter/Commands/ProfileCommand.cs
- [ ] T121 [US12] Implement pipe mode: read from stdin, format, write to stdout in src/AkmlSql.Formatter/Program.cs
- [ ] T122 [US12] Implement exit code logic per contract: 0=success, 1=violations, 2=parse error, 3=file not found, 4=invalid profile, 5=internal error with highest-severity aggregation in src/AkmlSql.Formatter/Program.cs

**Checkpoint**: CLI tool passes all modes and exit codes; suitable for Git pre-commit hooks and CI/CD

---

## Phase 15: User Story 13 — SQL Prompt Profile Import (Priority: P3)

**Goal**: Import Redgate SQL Prompt .sqlpromptstyle files with best-effort option mapping

**Independent Test**: Import a .sqlpromptstyle file, verify resulting profile produces similar output

- [ ] T123 [US13] Implement SqlPromptImporter with static mapping table of SQL Prompt option names to AKML SQL option paths, supporting all common SQL Prompt formatting options in src/AkmlSql.Formatting/Profiles/SqlPromptImporter.cs
- [ ] T124 [US13] Implement import result reporting: mapped count, unmapped count, unmapped option names, suggested defaults for unmapped in src/AkmlSql.Formatting/Profiles/SqlPromptImporter.cs
- [ ] T125 [US13] Wire ProfileImportRequest/ProfileImportResponse IPC flow and add Import UI button to profile management in src/AkmlSql.Shell.Shared/Ui/ and src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs

**Checkpoint**: SQL Prompt profiles import with 90%+ option mapping accuracy

---

## Phase 16: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [ ] T126 Extend src/AkmlSql.Installer/AkmlSqlSetup.iss to deploy CLI binary (akmlsql-format.exe), built-in profiles (.akmlstyle files), and create %AppData%/AKML SQL/profiles/ directory
- [ ] T127 [P] Add all new formatter commands (Format Document, Format Selection, 8 standalone actions) to the .vsct command table for all 6 shell targets with configurable keyboard shortcuts
- [ ] T128 [P] Implement error-tolerant formatting for partial parse: identify successfully parsed fragments by StartOffset/FragmentLength, format valid portions, preserve invalid regions verbatim in src/AkmlSql.Formatting/Pipeline/FormatterPipeline.cs
- [ ] T129 Ensure idempotent formatting: format already-formatted SQL with same profile produces identical output — add idempotency assertion to SemanticValidator in src/AkmlSql.Formatting/Pipeline/SemanticValidator.cs
- [ ] T130 [P] Verify formatting works across all 6 IDE targets (SSMS 20/21/22, VS 2019/2022/2026) by building each shell project and testing format command
- [ ] T131 Run quickstart.md validation: verify all build commands, development workflow steps, and integration test steps work end-to-end
- [ ] T132 Performance optimization: profile FormatterPipeline with BenchmarkDotNet, ensure <200ms for 1K lines, <500ms for 10K lines per performance budget in contracts/formatter-pipeline.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **US1 Format Document (Phase 3)**: Depends on Foundational — core pipeline MVP
- **US2 Format Selection (Phase 4)**: Depends on US1 (uses FormatterPipeline)
- **US3 Predefined Profiles (Phase 5)**: Depends on Foundational (ProfileManager)
- **US5 250+ Options (Phase 6)**: Depends on US1 (extends rule implementations)
- **US7 Noformat (Phase 7)**: Depends on US1 (extends pipeline)
- **US6 Casing + DB Sync (Phase 8)**: Depends on US1 (extends CasingEngine)
- **US8 Standalone Actions (Phase 9)**: Depends on US1 (uses pipeline components)
- **US4 Custom Profiles (Phase 10)**: Depends on US3 (extends ProfileManager)
- **US9 Auto-Format (Phase 11)**: Depends on US1 (triggers format pipeline)
- **US10 Profile Editor (Phase 12)**: Depends on US4 + US5 (edits all options)
- **US11 Bulk Format (Phase 13)**: Depends on US1 (formats multiple files)
- **US12 CLI (Phase 14)**: Depends on Foundational (uses Formatting library directly)
- **US13 SQL Prompt Import (Phase 15)**: Depends on US4 (profile system)
- **Polish (Phase 16)**: Depends on all desired user stories

### User Story Dependencies

```
Phase 1 (Setup) → Phase 2 (Foundational) → Phase 3 (US1: Format Document) ─┐
                                          → Phase 5 (US3: Profiles) ────────┤
                                          → Phase 14 (US12: CLI) ──────────┤
                                                                            │
Phase 3 (US1) → Phase 4 (US2: Selection)                                   │
             → Phase 6 (US5: 250+ Options) → Phase 12 (US10: Editor)      │
             → Phase 7 (US7: Noformat)                                     │
             → Phase 8 (US6: Casing + DB)                                  │
             → Phase 9 (US8: Actions)                                      │
             → Phase 11 (US9: Auto-Format)                                 │
             → Phase 13 (US11: Bulk Format)                                │
                                                                            │
Phase 5 (US3) → Phase 10 (US4: Custom Profiles) → Phase 15 (US13: Import) │
                                                 → Phase 12 (US10: Editor) │
```

### Parallel Opportunities

- Phase 1: T005, T006, T007 can run in parallel (different directories)
- Phase 2: T009–T017 (IPC messages) all parallel; T018, T019 parallel
- Phase 3: T037–T042 (rules + helpers) all parallel
- Phase 5: T050–T053 (built-in profiles) all parallel
- Phase 6: T057–T066 (all rule implementations + helpers) all parallel
- Phase 9: T074–T086 (all actions + commands) mostly parallel
- Phase 14: T114–T119 (CLI commands + output) all parallel
- US3 (Profiles) and US12 (CLI) can start in parallel after Foundational
- US5, US6, US7, US8, US9, US11 can start in parallel after US1

---

## Parallel Example: Phase 6 (US5 — 250+ Options)

```
# All rule files are independent — launch in parallel:
Task: T057 "Complete WhitespaceRules in src/AkmlSql.Formatting/Rules/WhitespaceRules.cs"
Task: T058 "Complete CasingRules in src/AkmlSql.Formatting/Rules/CasingRules.cs"
Task: T059 "Implement ListRules in src/AkmlSql.Formatting/Rules/ListRules.cs"
Task: T060 "Implement ParenthesisRules in src/AkmlSql.Formatting/Rules/ParenthesisRules.cs"
Task: T061 "Complete DmlRules in src/AkmlSql.Formatting/Rules/DmlRules.cs"
Task: T062 "Complete JoinRules in src/AkmlSql.Formatting/Rules/JoinRules.cs"
Task: T063 "Implement DdlRules in src/AkmlSql.Formatting/Rules/DdlRules.cs"
Task: T064 "Implement ControlFlowRules in src/AkmlSql.Formatting/Rules/ControlFlowRules.cs"
Task: T065 "Implement AlignmentCalculator in src/AkmlSql.Formatting/Layout/AlignmentCalculator.cs"
Task: T066 "Implement CollapseEvaluator in src/AkmlSql.Formatting/Layout/CollapseEvaluator.cs"
```

---

## Implementation Strategy

### MVP First (Phase 1 + Phase 2 + Phase 3)

1. Complete Phase 1: Setup (8 tasks)
2. Complete Phase 2: Foundational — IPC, profiles, pipeline skeleton, engine integration (22 tasks)
3. Complete Phase 3: US1 — Format Document with Default profile (16 tasks)
4. **STOP and VALIDATE**: Ctrl+K, Y formats SQL with Default profile, semantically validated
5. Deploy/demo if ready — core formatter works

### Incremental Delivery

1. Setup + Foundational → Pipeline skeleton ready
2. US1 (Format Document) → Core formatting works → **MVP**
3. US2 (Selection) + US3 (Profiles) → 5 profiles, selection format → **Beta 1**
4. US5 (250+ Options) + US7 (Noformat) → Full rule coverage, safe for real code → **Beta 2**
5. US6 (Casing+DB) + US8 (Actions) + US4 (Custom Profiles) → Power features → **Beta 3**
6. US9 (Auto) + US10 (Editor) + US11 (Bulk) + US12 (CLI) + US13 (Import) → Complete → **Release**

### Parallel Team Strategy

With multiple developers after Foundational:
- Developer A: US1 → US2 → US9 → US11
- Developer B: US3 → US4 → US10 → US13
- Developer C: US5 → US7 → US6 → US8
- Developer D: US12 (CLI — independent path)

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable after its prerequisites
- MVP = 46 tasks (Phase 1 + Phase 2 + Phase 3)
- Total = 132 tasks across 16 phases
- 52 tasks marked [P] for parallel execution
