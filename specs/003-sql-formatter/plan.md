# Implementation Plan: SQL Formatter & Code Beautifier

**Branch**: `003-sql-formatter` | **Date**: 2026-03-20 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/003-sql-formatter/spec.md`

## Summary

Deliver an AST-based SQL formatter for SSMS 20/21/22 and VS 2019/2022/2026 that transforms messy SQL into clean, standardized code with a single keystroke. The formatter operates inside the Phase 2 out-of-process engine (.NET 10), reusing the ScriptDom parser and named pipe communication. It adds a 6-stage formatting pipeline (Parse → Annotate → Layout → Casing → Emit → Validate) driven by 250+ configurable options organized into JSON profiles. A new shared formatting library (`AkmlSql.Formatting`) provides the core engine consumed by both the IDE extension and a standalone CLI tool (`AkmlSql.Formatter`). The profile editor is a WPF `DialogWindow` with live preview, built programmatically in Shell.Shared for cross-SDK compatibility.

## Technical Context

**Language/Version**: C# / .NET Framework 4.7.2 (shell extensions) + .NET 10 (engine, formatting library, CLI, tests)
**Primary Dependencies**: Microsoft.SqlServer.TransactSql.ScriptDom 170.191.0 (reuse from Phase 2), System.Text.Json 8.x (profile serialization with source generators), DiffPlex 1.7.x (diff mode + profile comparison), VS SDK 15.9.3-17.14.x (per target)
**Storage**: Profile files as `.akmlstyle` JSON in `%AppData%/AKML SQL/profiles/` (custom) and `<install>/profiles/` (built-in), config in `%AppData%/AKML SQL/config.json` (extended)
**Testing**: xunit 2.x, Microsoft.NET.Test.Sdk 17.x (same as Phase 1/2)
**Target Platform**: Windows 10/11, SSMS 20 (x86) / SSMS 21-22 (x64) / VS 2019-2026
**Project Type**: VS extension (desktop-app + out-of-process service) + standalone CLI tool
**Performance Goals**: <50ms (100 lines), <200ms (1K lines), <500ms (10K lines), <2s (50K lines), <100ms profile switch, <100ms live preview update, >50 files/sec bulk format
**Constraints**: <20MB additional memory, zero IDE crashes, semantic preservation (100% AST equivalence), idempotent formatting
**Scale/Scope**: 250+ formatting options, 8 option categories, 5 built-in profiles, SQL Server 2016-2025 + Azure SQL + Fabric, 6 IDE targets, 50K+ line files

## Constitution Check

*No constitution file found. Gates skipped.*

## Project Structure

### Documentation (this feature)

```text
specs/003-sql-formatter/
├── plan.md                 # This file
├── spec.md                 # Feature specification
├── research.md             # Phase 0: ScriptDom formatting, WPF ToolWindow, CLI architecture
├── data-model.md           # Phase 1: Entity model
├── quickstart.md           # Phase 1: Development setup guide
├── contracts/
│   ├── formatter-pipeline.md           # Formatting pipeline stages and interfaces
│   ├── format-protocol-extension.md    # Named pipe protocol extensions for formatter
│   └── profile-schema.md              # Profile JSON schema and option taxonomy
├── checklists/
│   └── requirements.md    # Spec quality checklist
└── tasks.md               # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── AkmlSql.Core/                          # EXTENDED: Formatter IPC messages, formatter settings
│   ├── Config/
│   │   ├── AppSettings.cs                 # Extended: FormatterSettings
│   │   └── ConfigManager.cs              # Unchanged
│   ├── Ipc/
│   │   ├── RpcMessage.cs                 # Unchanged (envelope)
│   │   ├── Messages/
│   │   │   ├── FormatRequest.cs          # NEW: Format SQL text with profile
│   │   │   ├── FormatResponse.cs         # NEW: Formatted text or error
│   │   │   ├── FormatPreviewRequest.cs   # NEW: Live preview during profile edit
│   │   │   ├── FormatPreviewResponse.cs  # NEW: Preview result
│   │   │   ├── ProfileListRequest.cs     # NEW: List available profiles
│   │   │   ├── ProfileListResponse.cs    # NEW: Profile metadata array
│   │   │   ├── BulkFormatRequest.cs      # NEW: Bulk format with file list
│   │   │   ├── BulkFormatProgress.cs     # NEW: Progress updates
│   │   │   ├── BulkFormatResponse.cs     # NEW: Final report
│   │   │   └── (existing Phase 2 messages unchanged)
│   │   └── FrameProtocol.cs             # Unchanged
│   ├── Logging/
│   └── Update/
│
├── AkmlSql.Formatting/                    # NEW: Shared formatting engine library (.NET 10)
│   ├── Pipeline/
│   │   ├── FormatterPipeline.cs          # Orchestrates 6-stage pipeline
│   │   ├── NoformatScanner.cs            # Pre-scan: identify noformat regions from tokens
│   │   ├── SqlcmdPreprocessor.cs         # Pre-process: extract SQLCMD directives
│   │   ├── AstAnnotator.cs              # Stage 2: attach comments, noformat context to AST
│   │   ├── LayoutEngine.cs              # Stage 3: apply formatting rules to produce layout tree
│   │   ├── CasingEngine.cs              # Stage 4: apply casing rules + DB identifier sync
│   │   ├── TextEmitter.cs               # Stage 5: serialize layout tree to formatted string
│   │   └── SemanticValidator.cs          # Stage 6: re-parse and compare normalized ASTs
│   ├── Layout/
│   │   ├── LayoutNode.cs                # Layout tree node (token + formatting decisions)
│   │   ├── IndentationTracker.cs        # Track indentation depth during emit
│   │   ├── LineBreakDecider.cs          # Determine line breaks per option rules
│   │   ├── AlignmentCalculator.cs       # Calculate column alignment for lists, DDL
│   │   └── CollapseEvaluator.cs         # Determine if short constructs should collapse
│   ├── Rules/
│   │   ├── WhitespaceRules.cs           # Category 1: 20+ whitespace/indentation rules
│   │   ├── CasingRules.cs              # Category 2: 10 casing rules
│   │   ├── ListRules.cs               # Category 3: 10 list/alignment rules
│   │   ├── ParenthesisRules.cs        # Category 4: 10 parenthesis rules
│   │   ├── DmlRules.cs               # Category 5: 20+ DML statement rules
│   │   ├── JoinRules.cs              # Category 6: 9 JOIN clause rules
│   │   ├── DdlRules.cs              # Category 7: 15 DDL statement rules
│   │   ├── ControlFlowRules.cs      # Category 8: CASE, CTE, control flow, expressions
│   │   └── IRuleSet.cs              # Interface for rule categories
│   ├── Profiles/
│   │   ├── FormattingProfile.cs         # Profile model with 250+ options
│   │   ├── ProfileMetadata.cs           # Name, author, version, basedOn, schemaVersion
│   │   ├── ProfileManager.cs            # CRUD, load, save, list, compare profiles
│   │   ├── ProfileSerializer.cs         # System.Text.Json source-generated serialization
│   │   ├── SqlPromptImporter.cs         # Import .sqlpromptstyle files
│   │   ├── ProfileDiffer.cs             # Side-by-side profile comparison
│   │   └── BuiltIn/                     # Embedded built-in profile JSON files
│   │       ├── default.akmlstyle
│   │       ├── compact.akmlstyle
│   │       ├── expanded.akmlstyle
│   │       ├── leading-commas.akmlstyle
│   │       └── minimalist.akmlstyle
│   ├── Actions/
│   │   ├── IFormatAction.cs             # Interface for standalone actions
│   │   ├── CasingOnlyAction.cs          # Apply casing without layout changes
│   │   ├── InsertSemicolonsAction.cs    # Add missing statement terminators
│   │   ├── RemoveSemicolonsAction.cs    # Remove statement terminators
│   │   ├── ExpandWildcardsAction.cs     # Replace SELECT * with column list
│   │   ├── QualifyObjectNamesAction.cs  # Add schema prefixes
│   │   ├── ToggleBracketsAction.cs      # Add/remove square brackets
│   │   └── ToggleAsKeywordAction.cs     # Add/remove AS on aliases
│   ├── Selection/
│   │   └── SelectionFormatter.cs        # Find enclosing AST fragment, format, splice
│   ├── CamelCase/
│   │   └── CamelCaseDictionary.cs       # Word boundary detection for compound identifiers
│   └── AkmlSql.Formatting.csproj        # .NET 10 class library
│
├── AkmlSql.Engine/                        # EXTENDED: Format request handling
│   ├── Program.cs                        # Unchanged
│   ├── Server/
│   │   ├── PipeRpcServer.cs              # Extended: dispatch FormatRequest, ProfileListRequest, etc.
│   │   └── SessionManager.cs            # Unchanged
│   ├── Formatter/
│   │   └── FormatRequestHandler.cs      # NEW: Handle format requests via pipeline
│   ├── Schema/                           # Unchanged (Phase 2)
│   ├── Parser/                           # Unchanged (Phase 2)
│   ├── Completion/                       # Unchanged (Phase 2)
│   └── AkmlSql.Engine.csproj            # Extended: reference AkmlSql.Formatting
│
├── AkmlSql.Formatter/                     # NEW: Standalone CLI formatter (.NET 10)
│   ├── Program.cs                        # Entry point: parse CLI args, dispatch commands
│   ├── Commands/
│   │   ├── FormatCommand.cs             # Format files in-place
│   │   ├── CheckCommand.cs             # Validate formatting (exit 0/1)
│   │   ├── DiffCommand.cs              # Show proposed changes
│   │   └── ProfileCommand.cs           # List, compare, import profiles
│   ├── Output/
│   │   ├── UnifiedDiffFormatter.cs      # Convert DiffPlex output to unified diff
│   │   ├── ReportGenerator.cs           # JSON report for bulk operations
│   │   └── ConsoleRenderer.cs           # Colored console output
│   └── AkmlSql.Formatter.csproj         # .NET 10, self-contained, PublishTrimmed
│
├── AkmlSql.Shell.Shared/                 # EXTENDED: Format commands, profile editor UI
│   ├── Editor/                           # Phase 2 (unchanged)
│   ├── Formatting/                       # NEW: Shell-side formatting integration
│   │   ├── FormatDocumentCommand.cs     # Ctrl+K, Y — format entire document
│   │   ├── FormatSelectionCommand.cs    # Ctrl+K, F — format selection
│   │   ├── FormatOnPasteHandler.cs      # Auto-format pasted SQL
│   │   ├── FormatOnSaveHandler.cs       # Auto-format on .sql file save
│   │   ├── FormatOnDelimiterHandler.cs  # Auto-format on ; or GO
│   │   ├── CasingOnlyCommand.cs         # Ctrl+B, Ctrl+U
│   │   ├── InsertSemicolonsCommand.cs   # Ctrl+B, Ctrl+S
│   │   ├── ExpandWildcardsCommand.cs    # Ctrl+B, Ctrl+W
│   │   ├── QualifyNamesCommand.cs       # Ctrl+B, Ctrl+Q
│   │   ├── ToggleBracketsCommand.cs     # Ctrl+B, Ctrl+B
│   │   └── ToggleAsCommand.cs           # Ctrl+B, Ctrl+A
│   ├── Ui/
│   │   ├── ProfileEditorDialog.cs       # NEW: WPF DialogWindow — split-pane profile editor
│   │   ├── ProfileEditorViewModel.cs    # NEW: Options, preview, undo/redo state
│   │   ├── OptionCategoryTreeBuilder.cs # NEW: Build TreeView for option categories
│   │   ├── SqlPreviewRenderer.cs        # NEW: RichTextBox with syntax-colored SQL preview
│   │   ├── ProfileSelectorDropdown.cs   # NEW: Toolbar dropdown for quick profile switch
│   │   ├── BulkFormatWizard.cs          # NEW: Bulk format dialog
│   │   ├── BulkFormatProgressDialog.cs  # NEW: Progress indicator during bulk format
│   │   ├── ThemeManager.cs             # Extended: EnvironmentColors for profile editor
│   │   ├── CompletionPopup.xaml(.cs)    # Unchanged (Phase 2)
│   │   ├── CompletionItemViewModel.cs   # Unchanged
│   │   └── DpiHelper.cs               # Unchanged
│   ├── Ipc/                             # Unchanged (Phase 2)
│   ├── IntelliSense/                    # Unchanged (Phase 2)
│   ├── Commands/                        # Existing Phase 1 commands
│   ├── StatusBar/                       # Extended: profile name indicator
│   ├── Dialogs/
│   └── Validation/
│
├── AkmlSql.Ssms20/                       # Unchanged structure, imports Shell.Shared
├── AkmlSql.Ssms21/                       # Unchanged
├── AkmlSql.Ssms22/                       # Unchanged
├── AkmlSql.VS2019/                       # Unchanged
├── AkmlSql.VS2022/                       # Unchanged
├── AkmlSql.VS2026/                       # Unchanged
├── AkmlSql.Updater/                      # Unchanged
└── AkmlSql.Installer/                    # Extended: deploy formatter CLI + built-in profiles

tests/
├── AkmlSql.Core.Tests/                   # Extended: formatter IPC message serialization tests
├── AkmlSql.Engine.Tests/                 # Unchanged (Phase 2)
└── AkmlSql.Formatting.Tests/             # NEW: Formatting engine test project (.NET 10)
    ├── Pipeline/
    │   ├── FormatterPipelineTests.cs    # End-to-end: input SQL → formatted output
    │   ├── NoformatScannerTests.cs      # Noformat region detection
    │   ├── SqlcmdPreprocessorTests.cs   # SQLCMD directive handling
    │   ├── SemanticValidatorTests.cs    # AST equivalence validation
    │   └── SelectionFormatterTests.cs   # Selection formatting
    ├── Rules/
    │   ├── WhitespaceRulesTests.cs      # 60+ whitespace/indentation tests
    │   ├── CasingRulesTests.cs          # 40+ casing tests
    │   ├── ListRulesTests.cs            # 50+ list/alignment tests
    │   ├── ParenthesisRulesTests.cs     # 30+ parenthesis tests
    │   ├── DmlRulesTests.cs             # 80+ DML tests
    │   ├── JoinRulesTests.cs            # 40+ JOIN tests
    │   ├── DdlRulesTests.cs             # 60+ DDL tests
    │   ├── ControlFlowRulesTests.cs     # 30+ control flow tests
    │   └── CaseExpressionTests.cs       # 25+ CASE expression tests
    ├── Profiles/
    │   ├── ProfileSerializerTests.cs    # Profile load/save round-trip
    │   ├── ProfileManagerTests.cs       # CRUD, duplicate, compare
    │   ├── SqlPromptImporterTests.cs    # SQL Prompt import mapping
    │   └── BuiltInProfileTests.cs       # Verify all 5 built-in profiles
    ├── Actions/
    │   ├── CasingOnlyActionTests.cs     # Casing without layout
    │   ├── InsertSemicolonsTests.cs     # Statement terminator insertion
    │   ├── ExpandWildcardsTests.cs      # SELECT * expansion
    │   └── QualifyNamesTests.cs         # Schema qualification
    └── Cli/
        ├── FormatCommandTests.cs        # CLI format mode
        ├── CheckCommandTests.cs         # CLI check mode + exit codes
        ├── DiffCommandTests.cs          # CLI diff output
        └── PipeModeTests.cs             # CLI stdin/stdout
```

**Structure Decision**: Phase 3 adds a new shared formatting library (`AkmlSql.Formatting`, .NET 10) that contains the core formatting engine — the 6-stage pipeline, 250+ formatting rules, profile management, and standalone actions. This library is referenced by both `AkmlSql.Engine` (for IDE-initiated formatting via named pipes) and `AkmlSql.Formatter` (the standalone CLI tool). The profile editor UI lives in `AkmlSql.Shell.Shared` as programmatic WPF (no XAML files) for cross-SDK compatibility. This extends the Phase 2 architecture without modifying it — the engine gains format request handlers, and the shell gains format commands + profile editor.

## Complexity Tracking

| Aspect | Justification | Simpler Alternative Rejected Because |
|---|---|---|
| Separate `AkmlSql.Formatting` library | Core formatting logic shared between IDE engine and CLI tool without coupling either to the other | Putting formatting in `AkmlSql.Engine` would force the CLI to carry the entire engine (schema cache, completion providers, pipe server) — excessive binary size and unnecessary dependencies |
| 6-stage formatting pipeline | Separation of concerns: parse, annotate, layout, casing, emit, validate each have distinct responsibilities | Monolithic formatter would be untestable and impossible to maintain with 250+ interacting options |
| Hybrid AST + token stream emit | AST provides structural decisions; token stream preserves comments and exact token text | Pure AST emit (ScriptDom's `SqlScriptGenerator`) drops comments; pure token stream walk cannot make structural formatting decisions |
| Programmatic WPF (no XAML) | Shell.Shared compiles into 6 target projects with different VS SDK versions; XAML compilation is fragile across SDKs | XAML in shared project causes cross-SDK compilation conflicts; existing codebase already avoids XAML |
| Pre-scan noformat + SQLCMD preprocessing | Noformat regions can span arbitrary AST boundaries; SQLCMD directives are not valid T-SQL | Handling noformat during AST walk fails when regions span multiple nodes; ScriptDom has no SQLCMD parsing mode |
