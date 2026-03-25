# Tasks: AI-Powered SQL Assistance

**Input**: Design documents from `/specs/009-ai-sql-assistance/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Organization**: Tasks are grouped by user story to enable independent implementation and testing. US2 (Provider Configuration) is foundational infrastructure — all other stories depend on it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: IPC message types, configuration POCOs, NuGet packages, command IDs — the skeleton that all features build on.

- [x] T001 Add AI message types (70-78, 170-178) to `src/AkmlSql.Core/Ipc/RpcMessage.cs` per contracts/ai-ipc.md
- [x] T002 [P] Add `AiSettings` configuration class to `src/AkmlSql.Core/Config/AppSettings.cs` per contracts/ai-settings.md with all 20 fields, nested under root `AppSettings`
- [x] T003 [P] Add AI NuGet packages to `src/AkmlSql.Engine/AkmlSql.Engine.csproj`: Microsoft.Extensions.AI 10.4.1, Microsoft.Extensions.AI.OpenAI 10.4.1, Anthropic.SDK 5.10.0, OllamaSharp 5.4.25, Mscc.GenerativeAI 3.1.0, Microsoft.ML.Tokenizers 2.0.0, Microsoft.ML.Tokenizers.Data.Cl100kBase 2.0.0
- [x] T004 [P] Create IPC request POCOs: `src/AkmlSql.Core/Ipc/Messages/AiTextToSqlRequest.cs`, `AiExplainRequest.cs`, `AiFixRequest.cs`, `AiOptimizeRequest.cs`, `AiIndexAnalysisRequest.cs`, `AiChatRequest.cs`, `AiGhostTextRequest.cs`, `AiProviderTestRequest.cs`, `AiStreamCancelRequest.cs` — all `[MessagePackObject]` with `[Key(N)]` attributes per contracts/ai-ipc.md
- [x] T005 [P] Create IPC response POCOs: `src/AkmlSql.Core/Ipc/Messages/AiTextToSqlResponse.cs`, `AiExplainResponse.cs`, `AiFixResponse.cs`, `AiOptimizeResponse.cs`, `AiIndexAnalysisResponse.cs`, `AiChatResponse.cs`, `AiGhostTextResponse.cs`, `AiProviderTestResponse.cs`, `AiStreamChunkMessage.cs` — all per contracts/ai-ipc.md
- [x] T006 [P] Create shared IPC DTOs: `src/AkmlSql.Core/Ipc/Messages/ChatTurnDto.cs`, `AnnotationDto.cs`, `IndexSuggestionDto.cs`, `CodeActionDto.cs` — all `[MessagePackObject]` per data-model.md
- [x] T007 [P] Create AI model classes: `src/AkmlSql.Core/Models/Ai/SchemaContext.cs`, `SchemaObjectSummary.cs`, `ColumnSummary.cs`, `PrivacyTransformation.cs` per data-model.md
- [x] T008 [P] Add AI command IDs (0x0700–0x070F) to `src/AkmlSql.Shell.Shared/PackageGuids.cs`: CmdTextToSql, CmdAiExplain, CmdAiFix, CmdAiOptimize, CmdAiIndexAnalysis, CmdAiChatPanel
- [x] T009 [P] Add AI button definitions and keybindings (Ctrl+Shift+G, Ctrl+Shift+E, Shift+Alt+R, Ctrl+Shift+O, Ctrl+Shift+A) to all 6 VSCT files: `src/AkmlSql.Ssms20/AkmlSqlSsms20.vsct` through `src/AkmlSql.VS2026/AkmlSqlVS2026.vsct`. Include `CommandPlacement` entries targeting the SQL editor context menu group for AI Explain, AI Fix, and AI Optimize commands (per FR-014, FR-020 "or context menu" requirement)
- [x] T009b [P] Audit Phase 9 keyboard shortcuts against VS/SSMS default bindings — `Ctrl+Shift+E` conflicts with VS "Open in Solution Explorer", `Ctrl+Shift+O` conflicts with VS "Open File". For VS targets (VS2019/VS2022/VS2026), use alternative bindings or chord sequences (e.g., `Ctrl+K, Ctrl+E` for Explain, `Ctrl+K, Ctrl+O` for Optimize) to avoid overriding native VS shortcuts. SSMS targets keep the primary bindings. Follow the Phase 8 precedent where `Ctrl+T` was changed to `Ctrl+Shift+T` in VS targets
- [x] T010 Add AI settings tab to `src/AkmlSql.Shell.Shared/Dialogs/SettingsDialog.cs` with provider dropdown, API key input (masked), endpoint field, privacy mode radio buttons, per-feature toggles, and Test Connection button

**Checkpoint**: All POCOs, message types, and package references in place. Build should pass.

---

## Phase 2: Foundational — Provider Infrastructure (US2, Priority: P1)

**Purpose**: Multi-model provider abstraction, credential encryption, schema context building, and privacy transformation. ALL other AI features depend on this.

**Goal**: Users can configure any AI provider in settings, test the connection, and the engine can create an `IChatClient` for that provider.

**Independent Test**: Open settings → select provider → enter API key → click Test Connection → see success/failure.

**⚠️ CRITICAL**: No AI feature work can begin until this phase is complete.

### Credential Security

- [x] T011 [US2] Create `src/AkmlSql.Engine/Ai/Security/CredentialManager.cs` — DPAPI encrypt/decrypt using `ProtectedData.Protect/Unprotect` with `DataProtectionScope.CurrentUser`, app-specific entropy SHA256("AkmlSql-ApiKey-v1"), store as "dpapi:" + base64 blob. Follow existing pattern in `src/AkmlSql.Engine/History/HistoryEncryption.cs`

### Provider Factory

- [x] T012 [US2] Create `src/AkmlSql.Engine/Ai/Providers/AiProviderFactory.cs` — factory that accepts `AiSettings` config and returns `IChatClient` for the configured provider. Use switch on provider name: "anthropic" → `AnthropicClient`, "openai" → `OpenAIClient.GetChatClient().AsIChatClient()`, "azure" → `AzureOpenAIClient`, "ollama" → `OllamaApiClient`, "lmstudio"/"custom" → `OpenAIClient` with custom endpoint. Handle missing/invalid config with descriptive errors
- [x] T013 [US2] Create `src/AkmlSql.Engine/Ai/Providers/GeminiChatClientAdapter.cs` — thin `IChatClient` adapter wrapping `Mscc.GenerativeAI.GenerativeModel`. Implement `GetResponseAsync` and `GetStreamingResponseAsync` by mapping between `ChatMessage`/`ChatResponse` and Gemini's request/response types
- [x] T014 [US2] Create provider test handler in `src/AkmlSql.Engine/Ai/AiProviderTestHandler.cs` — handles `MessageTypes.AiProviderTest` (77), creates `IChatClient` via factory, sends a simple test prompt ("Say hello"), returns `AiProviderTestResponse` with success/error/latency

### Schema Context

- [x] T015 [P] [US2] Create `src/AkmlSql.Engine/Ai/Context/SchemaContextBuilder.cs` — builds `SchemaContext` from `DatabaseCache`. Accept session ID → resolve connection/database via session lookup → get `DatabaseCache` → filter objects by relevance (keyword matching against prompt) → cap at 500 objects → select compression level (1-4) based on request type and token budget
- [x] T016 [P] [US2] Create `src/AkmlSql.Engine/Ai/Context/SchemaContextFormatter.cs` — formats `SchemaContext` into compact DDL-like text: `dbo.Orders(OrderId INT PK, CustomerId INT FK→dbo.Customers, OrderDate DATE)`. Include Level 1 (names + row counts), Level 2 (columns), Level 3 (keys/indexes), Level 4 (descriptions) based on compression level
- [x] T017 [P] [US2] Create `src/AkmlSql.Engine/Ai/Context/TokenEstimator.cs` — estimate token count using `Microsoft.ML.Tokenizers` with `Cl100kBase` tokenizer. Provide `EstimateTokens(string text)` returning int. Apply 1.1x multiplier as conservative estimate for non-OpenAI models

### Privacy Transformer

- [x] T018 [P] [US2] Create `src/AkmlSql.Engine/Ai/Privacy/LiteralRedactor.cs` — AST-based literal replacement using `TsqlParserService`. Walk parsed AST visiting `StringLiteral` → `'__STR_N__'`, `IntegerLiteral` → `__INT_N__`, `NumericLiteral` → `__NUM_N__`. Preserve NULL and boolean constants. Keep mapping `N → original_value`. Fallback to regex for unparseable SQL
- [x] T019 [P] [US2] Create `src/AkmlSql.Engine/Ai/Privacy/IdentifierHasher.cs` — HMAC-SHA256 based deterministic hashing with per-session 32-byte random key. Hash identifiers to 8-byte hex with category prefix: `t_` (table), `c_` (column), `s_` (schema). Keep reverse `Dictionary<string, string>` for de-hashing responses
- [x] T020 [US2] Create `src/AkmlSql.Engine/Ai/Privacy/PrivacyTransformer.cs` — orchestrates redaction pipeline. Accept privacy mode + SQL + schema context. For "full": no-op. For "schemaOnly": apply `LiteralRedactor` to SQL. For "anonymous": apply both `LiteralRedactor` and `IdentifierHasher` to SQL + schema context. For "offline"/"disabled": reject request. Provide `DeTransform(string aiResponse)` to reverse hashing/redaction in AI output

### Streaming

- [x] T021 [P] [US2] Create `src/AkmlSql.Engine/Ai/Streaming/StreamCoalescer.cs` — batches `IAsyncEnumerable<StreamingChatCompletionUpdate>` from IChatClient into coalesced chunks. Emit a chunk every 50ms or 5 tokens (whichever first). Each chunk is an `AiStreamChunkMessage` with accumulated text delta, running token count, and `IsComplete` flag

### Main AI Handler + PipeRpcServer Wiring

- [x] T022 [US2] Create `src/AkmlSql.Engine/Ai/AiRequestHandler.cs` — main dispatcher for all AI message types (70-78). Accept `SchemaCacheManager`, `TsqlParserService`, `AiSettings`. For each request: load settings → build schema context → apply privacy transform → create `IChatClient` via factory → build prompt → send to provider (streaming or buffered) → reverse privacy transform on response → return typed response. Handle timeouts, rate limits, retries with exponential backoff
- [x] T023 [US2] Wire `AiRequestHandler` and `AiProviderTestHandler` into `src/AkmlSql.Engine/Server/PipeRpcServer.cs` — add fields, constructor initialization (passing `_schemaCacheManager`, `_parserService`), and dispatch cases for message types 70-78 in the `DispatchAsync` switch. Cache `AiSettings` from config (invalidate on settings change)
- [x] T024 [US2] Create `src/AkmlSql.Shell.Shared/Ai/AiSettingsValidator.cs` — called from `AkmlSqlPackage.InitializeAsync()` in non-critical init block. Loads config, checks if AI is enabled, logs provider status. Does NOT block package initialization
- [x] T025 [US2] Register AI commands in all 6 `AkmlSqlPackage.cs` files: `src/AkmlSql.Ssms20/AkmlSqlPackage.cs` through `src/AkmlSql.VS2026/AkmlSqlPackage.cs`. Add TextToSqlCommand, AiExplainCommand, AiFixCommand, AiOptimizeCommand, AiChatPanelCommand initialization calls after existing Phase 8 commands
- [x] T026 [US2] Add shared items to `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems` — include all new AI command files, tool window files, adornment files, and dialog additions

**Checkpoint**: Provider factory works, schema context builds, privacy transforms apply, IPC dispatch routes AI messages. Test Connection works from settings dialog. Build and test pass.

---

## Phase 3: User Story 1 — Text-to-SQL Generation (Priority: P1) 🎯 MVP

**Goal**: Users type natural language → get generated SQL in a diff preview → accept/reject.

**Independent Test**: Press Ctrl+Shift+G → type "show all orders from last 30 days" → see generated SQL in diff preview → click Accept to insert or Reject to dismiss.

### Prompt Template

- [x] T027 [US1] Create `src/AkmlSql.Engine/Ai/Prompts/PromptTemplates.cs` — base class with shared system prompt instructions (SQL dialect, schema format, output format). Include `BuildSystemPrompt(SchemaContext context, string privacyMode)` that generates the system message with schema context
- [x] T028 [US1] Create `src/AkmlSql.Engine/Ai/Prompts/TextToSqlPrompt.cs` — builds the text-to-SQL prompt. System message: "You are a SQL expert. Generate a single SQL query for the following request. Use only the tables and columns from the provided schema. Return ONLY the SQL query, no explanations." User message: user's natural language prompt. Include schema context at appropriate compression level (Level 2-3)

### Engine Handler

- [x] T029 [US1] Implement text-to-SQL handling in `src/AkmlSql.Engine/Ai/AiRequestHandler.cs` — `HandleTextToSqlAsync(RpcMessage, sessionLookup, ct)`: deserialize `AiTextToSqlRequest`, build schema context (Level 2-3), apply privacy transform, build prompt via `TextToSqlPrompt`, send to `IChatClient` (streaming), reverse privacy transform on response, validate generated SQL against schema using `TsqlParserService.Parse()`, return `AiTextToSqlResponse` with success/generatedSql/error/tokens/latency

### Shell Command + Diff Preview UI

- [x] T030 [US1] Create `src/AkmlSql.Shell.Shared/Commands/TextToSqlCommand.cs` — handles CmdTextToSql (Ctrl+Shift+G). On execute: show input dialog for natural language prompt (or detect `--ai:` prefix in editor). Send `AiTextToSqlRequest` via `EngineProcessManager.Client.SendRequestAsync`. On response: open `DiffPreviewPanel` with generated SQL. If no AI provider configured: show message directing to settings. Use `JoinableTaskFactory.RunAsync` pattern
- [x] T031 [US1] Create `src/AkmlSql.Shell.Shared/Ai/DiffPreviewPanel.cs` — WPF UserControl showing generated SQL with syntax highlighting. Include Accept button (inserts SQL at cursor position in active editor), Edit button (opens SQL in new editor tab), Reject button (dismisses panel). Show token usage and latency info. Support streaming: progressively update the SQL preview as `AiStreamChunk` messages arrive. Before displaying final SQL, format it using the Phase 3 `FormatRequestHandler` (send a format request via IPC) to ensure consistent style. Also run Phase 5 `AnalysisEngine` rules and display any warnings as non-blocking annotations in the preview (per spec dependencies on Phase 3/Phase 5)
- [x] T032 [US1] Create `src/AkmlSql.Shell.Shared/Ai/TextToSqlInputDialog.cs` — WPF dialog with text input for natural language prompt, Submit and Cancel buttons. Include `--ai:` prefix detection logic: when user types `--ai:` in the editor, extract the text after the prefix and auto-populate the dialog
- [x] T033 [US1] Handle `--ai:` prefix detection in editor — add a check in the text-to-SQL command that inspects the current line for the `--ai:` prefix pattern and extracts the prompt text automatically without showing the input dialog

**Checkpoint**: Text-to-SQL works end-to-end. Users can generate SQL from natural language, see it in a diff preview, and accept/reject. MVP is functional.

---

## Phase 4: User Story 3 — AI Explain (Priority: P2)

**Goal**: Users select SQL → get a structured explanation with Purpose, Step-by-step, Key Details, Suggestions.

**Independent Test**: Select any SQL block → press Ctrl+Shift+E → see structured explanation panel.

- [x] T034 [P] [US3] Create `src/AkmlSql.Engine/Ai/Prompts/ExplainPrompt.cs` — builds the explain prompt. System message instructs AI to return structured response with sections: Purpose (1 sentence), Step-by-step (numbered), Key Details (types, performance, edge cases), Suggestions (optional improvements). Include schema context at Level 2
- [x] T035 [US3] Implement explain handling in `src/AkmlSql.Engine/Ai/AiRequestHandler.cs` — `HandleExplainAsync`: deserialize `AiExplainRequest`, build schema context, apply privacy transform, build prompt, send to `IChatClient`, parse structured response into `AiExplainResponse` sections (Purpose, StepByStep, KeyDetails, Suggestions), return response
- [x] T036 [US3] Create `src/AkmlSql.Shell.Shared/Commands/AiExplainCommand.cs` — handles CmdAiExplain (Ctrl+Shift+E). On execute: get selected text (or current statement if no selection). Send `AiExplainRequest`. On response: open `ExplanationPanel`. Use `JoinableTaskFactory.RunAsync` pattern
- [x] T037 [US3] Create `src/AkmlSql.Shell.Shared/Ai/ExplanationPanel.cs` — WPF UserControl with collapsible sections: Purpose, Step-by-step, Key Details, Suggestions. Render markdown content with basic formatting (bold, code blocks, lists). Show token usage and latency. Support streaming updates

**Checkpoint**: AI Explain works independently. Select SQL → get structured explanation.

---

## Phase 5: User Story 4 — AI Fix SQL (Priority: P2)

**Goal**: After a query error, users can get an AI-suggested fix displayed in a diff view with annotations.

**Independent Test**: Run SQL with an intentional error → click "Fix with AI" → see diff with corrected SQL and annotations.

- [x] T038 [P] [US4] Create `src/AkmlSql.Engine/Ai/Prompts/FixPrompt.cs` — builds the fix prompt. System message: "Fix the following SQL query that produced this error. Return the corrected SQL followed by a brief explanation of what was wrong. Mark each change as SAFE or REVIEW." Include error message, error number, failing SQL, and schema context (Level 2-3)
- [x] T039 [US4] Implement fix handling in `src/AkmlSql.Engine/Ai/AiRequestHandler.cs` — `HandleFixAsync`: deserialize `AiFixRequest`, build schema context, apply privacy transform, build prompt, send to `IChatClient`, parse response into `AiFixResponse` (FixedSql, Explanation, Annotations), return response
- [x] T040 [US4] Create `src/AkmlSql.Shell.Shared/Commands/AiFixCommand.cs` — handles CmdAiFix (Shift+Alt+R). On execute: capture current SQL + last execution error from `ExecutionCapture` service. Send `AiFixRequest`. On response: show diff preview (original vs fixed) with annotations. Use `JoinableTaskFactory.RunAsync` pattern
- [x] T041 [US4] Integrate auto-fix-on-error in `src/AkmlSql.Shell.Shared/Execution/ExecutionCapture.cs` (or appropriate execution hook) — when `AutoFixOnError` setting is enabled and a query fails, show a non-intrusive notification offering "Fix with AI" action. Clicking the notification invokes `AiFixCommand`
- [x] T042 [US4] Extend `src/AkmlSql.Shell.Shared/Ai/DiffPreviewPanel.cs` to support annotation overlays — display `AnnotationDto` markers in the diff view with colored indicators ("safe" = green, "review" = yellow) and tooltip descriptions

**Checkpoint**: AI Fix works independently. Error → Fix with AI → diff view with annotations → accept/reject.

---

## Phase 6: User Story 5 — AI Optimize SQL (Priority: P2)

**Goal**: Users select a query → get an optimized version with categorized annotations and optional index suggestions.

**Independent Test**: Select a known-inefficient query → press Ctrl+Shift+O → see optimized SQL with "Safe"/"Review" annotations.

- [x] T043 [P] [US5] Create `src/AkmlSql.Engine/Ai/Prompts/OptimizePrompt.cs` — builds the optimize prompt. System message instructs AI to analyze for: SARGability, JOIN ordering, index utilization, unnecessary DISTINCT/subquery, cursor-to-set-based alternatives. Return optimized SQL + categorized annotations (SAFE/REVIEW) + optional CREATE INDEX scripts with estimates. Include schema context at Level 3 (with indexes)
- [x] T044 [US5] Implement optimize handling in `src/AkmlSql.Engine/Ai/AiRequestHandler.cs` — `HandleOptimizeAsync`: deserialize `AiOptimizeRequest`, build schema context (Level 3), apply privacy transform, build prompt, send to `IChatClient`, parse response into `AiOptimizeResponse` (OptimizedSql, Explanation, Annotations, IndexSuggestions), return response
- [x] T045 [US5] Create `src/AkmlSql.Shell.Shared/Commands/AiOptimizeCommand.cs` — handles CmdAiOptimize (Ctrl+Shift+O). On execute: get selected text. Send `AiOptimizeRequest`. On response: show diff preview with annotations + index suggestion panel. Use `JoinableTaskFactory.RunAsync` pattern

**Checkpoint**: AI Optimize works independently. Select query → see optimized version with annotations.

---

## Phase 7: User Story 6 — AI Index Suggestions (Priority: P3)

**Goal**: Users analyze a query (or execution plan) for missing index opportunities and get CREATE INDEX scripts.

**Independent Test**: Select a query with known missing index → invoke AI Index Analysis → see CREATE INDEX scripts with estimates.

- [x] T046 [P] [US6] Create `src/AkmlSql.Engine/Ai/Prompts/IndexAnalysisPrompt.cs` — builds the index analysis prompt. Include query text, execution plan XML (if available), existing indexes from schema cache, and table structures. Instruct AI to return CREATE INDEX scripts with improvement estimates, size estimates, and write impact ratings
- [x] T047 [US6] Implement index analysis handling in `src/AkmlSql.Engine/Ai/AiRequestHandler.cs` — `HandleIndexAnalysisAsync`: deserialize `AiIndexAnalysisRequest`, build schema context (Level 3 with existing indexes), apply privacy transform, build prompt (include execution plan XML if provided), send to `IChatClient`, parse response into `AiIndexAnalysisResponse` (IndexSuggestions, Summary), return response
- [x] T048 [US6] Create index analysis command entry point — add context menu item or command palette entry (no dedicated keyboard shortcut). Shell command sends `AiIndexAnalysisRequest`. On response: show index suggestions in a panel with CREATE INDEX scripts, copy button, and apply button

**Checkpoint**: AI Index Analysis works independently. Submit query → get index suggestions with scripts.

---

## Phase 8: User Story 7 — AI Chat Panel (Priority: P3)

**Goal**: Users open a dockable chat panel and have multi-turn conversations with schema-aware AI.

**Independent Test**: Press Ctrl+Shift+A → chat panel opens → ask a question → get schema-aware response with actionable buttons.

- [x] T049 [P] [US7] Create `src/AkmlSql.Engine/Ai/Prompts/ChatSystemPrompt.cs` — builds the chat system prompt. Include full schema context, current database name, instructions to be a helpful SQL assistant. Support multi-turn context by including conversation history in the prompt
- [x] T050 [US7] Implement chat handling in `src/AkmlSql.Engine/Ai/AiRequestHandler.cs` — `HandleChatAsync`: deserialize `AiChatRequest` (with conversation history), build schema context, apply privacy transform, build prompt with system message + history + new user message, send to `IChatClient` (streaming), parse response for code blocks → create `CodeActionDto` entries ("Apply Fix", "Copy Script"), return `AiChatResponse`
- [x] T051 [US7] Create `src/AkmlSql.Shell.Shared/Ai/AiChatToolWindow.cs` — `ToolWindowPane` subclass hosting `AiChatPanel`. GUID: `A1B2C3D4-AAAA-BBBB-CCCC-DDEEFF000003`. Register with `[ProvideToolWindow]` attribute in all 6 package files
- [x] T052 [US7] Create `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` — WPF UserControl with: conversation history area (scrollable), text input field with Send button, loading indicator during AI response. Render AI responses as markdown with syntax-highlighted code blocks. Include actionable buttons per `CodeActionDto`: "Apply Fix" (inserts code at cursor), "Copy Script" (copies to clipboard). Maintain conversation history as `List<ChatTurnDto>` — send full history with each request
- [x] T053 [US7] Create `src/AkmlSql.Shell.Shared/Commands/AiChatPanelCommand.cs` — handles CmdAiChatPanel (Ctrl+Shift+A). On execute: `_package.FindToolWindow(typeof(AiChatToolWindow), 0, true)` to open/focus the chat panel. Use `JoinableTaskFactory.RunAsync` pattern
- [x] T054 [US7] Handle database context changes in chat — when the user switches databases or connections, refresh the schema context in the chat panel and show a notification "Context updated to database X"

**Checkpoint**: AI Chat Panel works independently. Open panel → ask questions → get schema-aware responses → apply suggestions.

---

## Phase 9: User Story 8 — Inline Ghost Text Completion (Priority: P3)

**Goal**: As users type SQL, AI predicts the next line(s) and shows gray ghost text. Tab to accept, Escape to dismiss.

**Independent Test**: Enable inline completion in settings → type `SELECT * FROM dbo.` → see ghost text prediction → Tab to accept.

- [x] T055 [P] [US8] Create `src/AkmlSql.Engine/Ai/Prompts/GhostTextPrompt.cs` — builds the ghost text prompt. System message: "Complete the following SQL. Return ONLY the predicted continuation, no explanations." Include ~500 chars before cursor and schema context (Level 2, compact). Instruct model to predict 1-5 lines maximum
- [x] T056 [US8] Implement ghost text handling in `src/AkmlSql.Engine/Ai/AiRequestHandler.cs` — `HandleGhostTextAsync`: deserialize `AiGhostTextRequest`, build minimal schema context (Level 1-2, token-budgeted for < 500ms total latency), apply privacy transform, build prompt with preceding text, send to `IChatClient` (non-streaming for speed), return `AiGhostTextResponse` with predicted text. Implement debounce: if a new ghost text request arrives while one is pending, cancel the previous
- [x] T057 [US8] Create `src/AkmlSql.Shell.Shared/Ai/GhostTextAdornmentProvider.cs` — MEF `IWpfTextViewCreationListener` that creates `GhostTextAdornment` for SQL editor views. Check `AiSettings.InlineCompletion` config (cached) — skip if disabled. Follow existing adornment pattern in `src/AkmlSql.Shell.Shared/Editor/Adornments/`
- [x] T058 [US8] Create `src/AkmlSql.Shell.Shared/Ai/GhostTextAdornment.cs` — editor adornment that: (1) detects typing pauses (300ms debounce), (2) sends `AiGhostTextRequest` via IPC, (3) renders response as gray semi-transparent text at cursor position using `IAdornmentLayer`, (4) accepts on Tab (inserts predicted text into buffer), (5) dismisses on Escape or continued typing. Must NOT interfere with Phase 2 IntelliSense completion popup — check if completion session is active via `ICompletionBroker` and suppress ghost text when IntelliSense is showing
- [x] T059 [US8] Implement ghost text cancellation — when user types while a ghost text request is pending, send `AiStreamCancel` to abort the previous request and start a new debounce timer. Prevent stale predictions from appearing after the user has moved on

**Checkpoint**: Ghost text completion works independently. Type SQL → see predictions → Tab to accept.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Privacy consent, error resilience, cross-target testing, and UX polish.

- [x] T060 [P] Implement privacy consent notice — on first cloud AI request, show a one-time dialog explaining what data will be sent and to which provider. Store consent flag in `AiSettings`. Block the request until user confirms. Per FR-034
- [x] T061 [P] Implement rate limit handling in `src/AkmlSql.Engine/Ai/AiRequestHandler.cs` — detect HTTP 429 responses from providers, implement exponential backoff queue (1s, 2s, 4s, 8s, max 30s), send `AiStreamChunk` with status updates ("Rate limited, retrying in Xs..."), gracefully degrade to showing retry option. Per FR-037
- [x] T062 [P] Implement offline fallback in `src/AkmlSql.Engine/Ai/AiRequestHandler.cs` — when primary cloud provider fails (timeout, auth error, rate limit exhausted), check if `OfflineProvider` is configured, switch to offline `IChatClient`, notify shell via `AiStreamChunk` status message ("Switched to offline model"). Per FR-006
- [x] T063 [P] Implement AI-generated SQL validation in `src/AkmlSql.Engine/Ai/AiRequestHandler.cs` — parse AI-generated SQL with `TsqlParserService`, check all referenced objects against schema cache, add warning annotations to `DiffPreviewPanel` for non-existent objects. Per FR-039
- [x] T064 [P] Add AI feature visibility control — when `AiSettings.Enabled` is false or `PrivacyMode` is "disabled", hide all AI commands from menus via `BeforeQueryStatus` handlers on `OleMenuCommand`. Ensure zero AI code paths execute. Per FR-005
- [x] T065 Build and test across all 6 targets — build Engine with `dotnet publish`, build each shell project with MSBuild separately, verify no VSCT conflicts, run `dotnet test` for all unit tests
- [x] T066 Run quickstart.md validation — verify architecture diagram matches implementation, all file paths exist, build commands work, package versions are correct

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — BLOCKS all AI features
- **User Stories (Phase 3–9)**: All depend on Phase 2 (Foundational) completion
  - US1 (Text-to-SQL): Independent — first MVP feature
  - US3 (Explain): Independent — can run in parallel with US1
  - US4 (Fix): Independent — can run in parallel with US1/US3, shares `DiffPreviewPanel` from US1
  - US5 (Optimize): Independent — can run in parallel, shares `DiffPreviewPanel` and annotation overlays from US4
  - US6 (Index): Independent — can run in parallel
  - US7 (Chat): Independent — can run in parallel, largest UI component
  - US8 (Ghost Text): Independent — can run in parallel, most complex editor integration
- **Polish (Phase 10)**: Depends on all desired user stories being complete

### User Story Dependencies

- **US2 (Provider Config)**: Foundational — BLOCKS everything
- **US1 (Text-to-SQL)**: Depends on US2 only. Creates `DiffPreviewPanel` that US4/US5 reuse
- **US3 (Explain)**: Depends on US2 only
- **US4 (Fix)**: Depends on US2. Benefits from `DiffPreviewPanel` (US1) but can create its own if US1 not done
- **US5 (Optimize)**: Depends on US2. Benefits from `DiffPreviewPanel` (US1) and annotation overlays (US4)
- **US6 (Index)**: Depends on US2 only
- **US7 (Chat)**: Depends on US2 only
- **US8 (Ghost Text)**: Depends on US2 only

### Within Each User Story

- Prompt template before handler implementation
- Engine handler before shell command
- Shell command before UI components
- Core implementation before polish

### Parallel Opportunities

- Phase 1: T002–T010 are all [P] — can run in parallel
- Phase 2: T015–T021 are all [P] — schema context, privacy, streaming can develop in parallel
- Phase 3–9: All user stories can start in parallel after Phase 2 completes (if team capacity allows)
- Phase 10: T060–T064 are all [P] — cross-cutting concerns can develop in parallel

---

## Parallel Example: Phase 1 Setup

```
# These 8 tasks can all run in parallel (different files):
T002: AiSettings in AppSettings.cs
T003: NuGet packages in Engine.csproj
T004: Request POCOs in Core/Ipc/Messages/
T005: Response POCOs in Core/Ipc/Messages/
T006: Shared DTOs in Core/Ipc/Messages/
T007: Model classes in Core/Models/Ai/
T008: Command IDs in PackageGuids.cs
T009: VSCT button definitions in 6 .vsct files
```

## Parallel Example: Phase 2 Foundational

```
# These 4 tasks can run in parallel (different files, no dependencies):
T015: SchemaContextBuilder.cs
T016: SchemaContextFormatter.cs
T017: TokenEstimator.cs
T018: LiteralRedactor.cs + T019: IdentifierHasher.cs
```

## Parallel Example: User Stories (after Phase 2)

```
# All user story phases can start simultaneously:
Phase 3 (US1 Text-to-SQL): T027–T033
Phase 4 (US3 Explain): T034–T037
Phase 5 (US4 Fix): T038–T042
Phase 6 (US5 Optimize): T043–T045
Phase 7 (US6 Index): T046–T048
Phase 8 (US7 Chat): T049–T054
Phase 9 (US8 Ghost Text): T055–T059
```

---

## Implementation Strategy

### MVP First (Text-to-SQL Only)

1. Complete Phase 1: Setup (T001–T010)
2. Complete Phase 2: Foundational/US2 (T011–T026)
3. Complete Phase 3: US1 Text-to-SQL (T027–T033)
4. **STOP and VALIDATE**: Configure a provider, test connection, generate SQL from natural language, verify diff preview works
5. Deploy/demo if ready — users can already generate SQL from natural language

### Incremental Delivery

1. Setup + Foundational → Provider infrastructure ready
2. Add US1 (Text-to-SQL) → Test independently → **Deploy MVP**
3. Add US3 (Explain) + US4 (Fix) → Test independently → Deploy
4. Add US5 (Optimize) + US6 (Index) → Test independently → Deploy
5. Add US7 (Chat) → Test independently → Deploy
6. Add US8 (Ghost Text) → Test independently → Deploy
7. Polish → Final release

### Parallel Team Strategy

With multiple developers after Phase 2 completes:
- Developer A: US1 (Text-to-SQL) + US4 (Fix) — shares DiffPreviewPanel
- Developer B: US3 (Explain) + US5 (Optimize) — analysis-focused features
- Developer C: US7 (Chat Panel) — largest standalone UI component
- Developer D: US8 (Ghost Text) — most complex editor integration
- Developer E: US6 (Index Analysis) — specialized feature

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable after Phase 2
- All AI features follow the same pattern: prompt template → engine handler → shell command → UI component
- Privacy transforms and schema context are shared infrastructure (Phase 2) used by ALL features
- Ghost text (US8) is the most complex due to editor integration — tackle last if sequential
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
