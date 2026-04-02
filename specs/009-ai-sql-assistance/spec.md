# Feature Specification: AI-Powered SQL Assistance

**Feature Branch**: `009-ai-sql-assistance`
**Created**: 2026-03-25
**Status**: Draft
**Input**: User description: "Phase 9 — AI-Powered SQL Assistance: Multi-model AI integration for text-to-SQL, AI explain, AI fix, AI optimize, AI index suggestions, AI chat panel, and inline ghost text completion."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Text-to-SQL Generation (Priority: P1)

A database developer wants to quickly generate a complex SQL query without writing it manually. They type a natural language description (e.g., "show me the top 10 customers by total order amount this year") using a keyboard shortcut or the `--ai:` prefix in the editor. The system sends the prompt along with relevant database schema context to the configured AI provider and displays the generated SQL in a diff-style preview. The developer reviews the suggestion and chooses to accept, edit, or reject it. The generated SQL is never auto-inserted or auto-executed.

**Why this priority**: Text-to-SQL is the highest-value AI feature — it saves the most time for the broadest set of users and is the primary differentiator that transforms AKML SQL from a productivity tool into an AI-powered development platform.

**Independent Test**: Can be fully tested by triggering the text-to-SQL command, entering a natural language prompt, and verifying that generated SQL appears in a diff preview. Delivers immediate value even without other AI features.

**Acceptance Scenarios**:

1. **Given** a connected database session with schema loaded, **When** the user presses `Ctrl+Shift+G` and types "show all orders from last 30 days with customer names", **Then** the system generates syntactically valid SQL referencing actual tables/columns from the connected database and displays it in a diff-style preview panel.
2. **Given** a generated SQL preview is displayed, **When** the user clicks "Accept", **Then** the SQL is inserted into the editor at the cursor position. **When** the user clicks "Reject", **Then** the preview is dismissed with no changes.
3. **Given** a user types `--ai: get inactive customers` in the editor, **When** the AI prefix is detected, **Then** the text-to-SQL flow is triggered automatically using the text after the prefix as the prompt.
4. **Given** no AI provider is configured, **When** the user triggers text-to-SQL, **Then** the system shows a helpful message directing them to configure an AI provider in settings.
5. **Given** the AI provider returns an error or times out, **When** waiting for a response, **Then** the system displays a clear error message and offers retry or cancel options.

---

### User Story 2 - Multi-Model Provider Configuration (Priority: P1)

An enterprise DBA wants full control over which AI provider processes their database schema and queries. They open the AKML SQL settings and configure their preferred provider — choosing from cloud providers (Anthropic Claude, OpenAI GPT, Azure OpenAI, Google Gemini) or local providers (Ollama, LM Studio) for complete offline operation. They set their API key (which is stored encrypted), select a privacy mode controlling what data is transmitted, and optionally configure a fallback offline provider for when cloud connectivity is unavailable.

**Why this priority**: Provider configuration is a prerequisite for all AI features. Without it, nothing else works. The multi-model and privacy architecture is also the key enterprise differentiator.

**Independent Test**: Can be tested by configuring each supported provider in settings and verifying the connection works (e.g., a "Test Connection" action). Delivers value by enabling all downstream AI features.

**Acceptance Scenarios**:

1. **Given** the user opens AI settings, **When** they select a provider (e.g., "Anthropic"), enter an API key, and click "Test Connection", **Then** the system validates the key and confirms the connection works.
2. **Given** the user selects "Ollama" as provider with a local endpoint, **When** they save settings, **Then** all AI features operate entirely locally with zero data transmitted externally.
3. **Given** the user sets privacy mode to "schemaOnly", **When** any AI request is made, **Then** table and column names are sent but all literal data values in the query are redacted before transmission.
4. **Given** the user sets privacy mode to "anonymous", **When** any AI request is made, **Then** all table and column names are hashed before transmission while preserving structural relationships.
5. **Given** the user sets privacy mode to "offline", **When** any AI feature is triggered, **Then** only the configured local model is used and no network requests are made to external services.
6. **Given** the user has configured both a cloud provider and an offline fallback, **When** the cloud provider is unreachable, **Then** the system automatically falls back to the local model and notifies the user.

---

### User Story 3 - AI Explain (Priority: P2)

A developer inherits a complex stored procedure written by a former colleague. They select the SQL code, right-click and choose "AI Explain" (or press `Ctrl+Shift+E`). The system sends the selected code along with relevant schema context to the AI and displays a structured explanation including: a one-sentence purpose summary, step-by-step clause explanation, key details about data types and performance, and optional improvement suggestions.

**Why this priority**: Understanding existing code is the second most common developer need after writing new code. This feature has a low barrier to adoption — users try it once on confusing code and immediately see value.

**Independent Test**: Can be tested by selecting any SQL block, triggering explain, and verifying a structured explanation appears. Works independently of text-to-SQL.

**Acceptance Scenarios**:

1. **Given** the user has selected a multi-statement SQL block, **When** they press `Ctrl+Shift+E`, **Then** a panel displays a structured explanation with Purpose, Step-by-step, Key Details, and Suggestions sections.
2. **Given** no text is selected, **When** the user triggers AI Explain, **Then** the system explains the entire current statement at the cursor position.
3. **Given** the selected SQL references database objects, **When** the explanation is generated, **Then** the AI receives relevant schema context (table structures, relationships) to provide accurate explanations.

---

### User Story 4 - AI Fix SQL (Priority: P2)

A developer runs a query that fails with a SQL error. The system captures the error message and the failing SQL, then offers a "Fix with AI" action. The AI analyzes the error, the SQL, and the database schema to produce a corrected version. The user sees a side-by-side diff showing the original vs. fixed SQL with annotations explaining what was wrong and what was changed. The user can accept or reject the fix.

**Why this priority**: Error recovery is a high-frequency, high-frustration scenario. Automating the fix-debug cycle saves significant time and reduces context switching.

**Independent Test**: Can be tested by deliberately running SQL with common errors (missing column, wrong syntax, type mismatch) and verifying the AI proposes a correct fix in the diff view.

**Acceptance Scenarios**:

1. **Given** a query execution fails with an error, **When** the system offers "Fix with AI" and the user clicks it, **Then** the AI receives the SQL + error message + schema context and returns a corrected SQL displayed in a diff view.
2. **Given** the user presses `Shift+Alt+R` on a SQL block, **When** the AI processes the request, **Then** a diff preview shows original vs. fixed SQL with inline annotations explaining each change.
3. **Given** the "auto-fix on error" setting is enabled, **When** a query fails, **Then** the system automatically offers the fix suggestion without the user needing to invoke the command manually. This setting is disabled by default.
4. **Given** the AI cannot determine a fix, **When** the response is received, **Then** the system displays a message explaining it could not resolve the error and suggests the user check the error details manually.

---

### User Story 5 - AI Optimize SQL (Priority: P2)

A DBA is performance-tuning a slow query. They select the query, press `Ctrl+Shift+O`, and the AI analyzes it for performance improvements — considering SARGability, JOIN ordering, index utilization, unnecessary DISTINCT/subquery elimination, and set-based alternatives to cursor patterns. The result is an optimized SQL version with categorized annotations distinguishing safe changes from changes that need human review.

**Why this priority**: Performance optimization requires deep expertise that many developers lack. AI can surface improvements that would otherwise require a senior DBA.

**Independent Test**: Can be tested by selecting known-inefficient queries and verifying the AI returns an optimized version with categorized annotations.

**Acceptance Scenarios**:

1. **Given** the user selects a SQL query and presses `Ctrl+Shift+O`, **When** the AI analyzes it, **Then** the system displays an optimized version in a diff view with annotations categorized as "Safe changes" (formatting, redundant code) and "Review changes" (restructuring, index suggestions).
2. **Given** the query has no obvious optimization opportunities, **When** the AI analyzes it, **Then** the system reports that no significant improvements were found.
3. **Given** the AI suggests index creation, **When** the suggestion is displayed, **Then** it includes the estimated improvement, index size impact, and effect on write performance.

---

### User Story 6 - AI Index Suggestions (Priority: P3)

A DBA wants to identify missing indexes for a slow query. They invoke "AI Index Analysis" which sends the query (or an execution plan XML) along with existing index information to the AI. The AI returns CREATE INDEX scripts with expected improvement estimates, index size estimates, write performance impact, and covering index recommendations.

**Why this priority**: Index analysis is a specialized task that directly impacts production performance. While valuable, it serves a narrower audience (DBAs) compared to the general-purpose features above.

**Independent Test**: Can be tested by submitting a query with known missing index opportunities and verifying correct CREATE INDEX scripts are suggested.

**Acceptance Scenarios**:

1. **Given** a query that would benefit from an index, **When** the user invokes AI Index Analysis, **Then** the system returns one or more CREATE INDEX scripts with improvement estimates.
2. **Given** an execution plan XML is available, **When** the user invokes index analysis, **Then** the AI incorporates the plan's missing index hints into its recommendations.
3. **Given** a query that already uses optimal indexes, **When** the AI analyzes it, **Then** the system reports no additional indexes are needed.

---

### User Story 7 - AI Chat Panel (Priority: P3)

A developer wants to have an interactive conversation with AI about their database and queries. They open the AI Chat panel (docked in the IDE) via `Ctrl+Shift+A` and ask questions in natural language. The AI responds with schema-aware answers, can generate SQL snippets inline, and provides actionable buttons (e.g., "Apply Fix", "Copy Script") for code suggestions. The conversation maintains context across multiple turns.

**Why this priority**: The chat panel is a flexible interface that covers long-tail use cases not addressed by the focused features above. It requires the most UI development but serves as the catch-all interaction model.

**Independent Test**: Can be tested by opening the chat panel, asking database-related questions, and verifying responses are schema-aware with actionable code suggestions.

**Acceptance Scenarios**:

1. **Given** the user presses `Ctrl+Shift+A`, **When** the chat panel opens, **Then** it is docked in the IDE with a text input field and conversation history area.
2. **Given** an active database connection, **When** the user asks "How can I improve the performance of my GetOrdersByDate procedure?", **Then** the AI provides a schema-aware response referencing actual table/column names and includes actionable suggestions with "Apply" buttons.
3. **Given** a multi-turn conversation, **When** the user asks a follow-up question, **Then** the AI maintains context from previous messages in the conversation.
4. **Given** the AI suggests a code change, **When** the user clicks "Apply Fix", **Then** the suggestion is applied to the active editor at the appropriate location.

---

### User Story 8 - Inline Ghost Text Completion (Priority: P3)

A developer is writing SQL and wants AI-powered predictive completion beyond single-token IntelliSense. As they type, the AI predicts the next line(s) of SQL and displays them as gray ghost text (similar to Copilot). They press Tab to accept the suggestion or Escape to dismiss it. This feature is opt-in and operates alongside (not replacing) the deterministic Phase 2 IntelliSense.

**Why this priority**: Inline completion is the most ambitious feature requiring careful UX integration with the existing editor. It is opt-in by default because it has the highest potential for disruption and requires significant latency optimization.

**Independent Test**: Can be tested by enabling the feature in settings, typing SQL, and verifying ghost text predictions appear after a brief delay. Tab accepts, Escape dismisses.

**Acceptance Scenarios**:

1. **Given** inline completion is enabled and the user is typing SQL, **When** a pause in typing is detected, **Then** gray ghost text appears showing predicted continuation.
2. **Given** ghost text is displayed, **When** the user presses Tab, **Then** the prediction is accepted and inserted. **When** the user presses Escape, **Then** the ghost text is dismissed.
3. **Given** ghost text is displayed, **When** the user continues typing, **Then** the prediction updates or dismisses based on the new input context.
4. **Given** inline completion is disabled (default), **When** the user types in the editor, **Then** no ghost text predictions appear.
5. **Given** both Phase 2 IntelliSense and inline completion are active, **When** a completion popup is showing, **Then** IntelliSense takes priority and ghost text does not interfere.

---

### Edge Cases

- What happens when the AI provider rate-limits requests? The system queues requests with exponential backoff, shows a notification to the user, and gracefully degrades to Phase 2 deterministic features.
- What happens when the database schema is very large (thousands of tables)? Schema context is dynamically compressed and filtered to relevant objects based on the user's query/prompt, with a maximum of 500 objects per request.
- What happens when the user switches databases mid-conversation in the chat panel? The AI context is refreshed with the new database schema and the user is notified that the context has changed.
- What happens when the local model (Ollama/LM Studio) is not running? The system detects the unavailable endpoint and shows a helpful error message suggesting the user start the local model service.
- What happens when the AI generates SQL that references non-existent objects? The diff preview includes validation warnings highlighting references that don't match the current schema.
- What happens when the API key is invalid or expired? The system detects the authentication failure on the first request and prompts the user to update their API key in settings.
- What happens when the user is in "disabled" privacy mode? All AI features are hidden from menus and shortcuts are unbound, ensuring zero AI-related code execution.
- What happens when the user pastes sensitive data (e.g., passwords) into a prompt? In "schemaOnly" mode, literal values are redacted. In "full" mode, the system shows a one-time warning that data will be transmitted to the cloud provider.
- What happens when the network connection drops during an AI request? The system cancels the pending request after the configured timeout, notifies the user, and offers to retry or switch to offline mode.
- What happens when the AI returns invalid or non-parseable SQL? The diff preview marks the response as potentially invalid and the user can still review/edit it, but the "Accept" action shows a warning.

## Requirements *(mandatory)*

### Functional Requirements

**AI Provider Infrastructure**

- **FR-001**: System MUST support multiple AI providers: Anthropic Claude, OpenAI GPT, Azure OpenAI, Google Gemini, Ollama (local), LM Studio (local), and any custom OpenAI-compatible endpoint.
- **FR-002**: System MUST store API keys in encrypted form, never in plain text.
- **FR-003**: System MUST provide a "Test Connection" action that validates provider configuration before use.
- **FR-004**: System MUST support five privacy modes: "full" (schema + query sent), "schemaOnly" (schema + anonymized query), "anonymous" (hashed names), "offline" (local model only, zero transmission), and "disabled" (all AI features completely off with no residual behavior).
- **FR-005**: When privacy mode is "disabled", the system MUST hide all AI commands from menus, unbind AI keyboard shortcuts, and ensure zero AI-related code paths execute.
- **FR-006**: System MUST automatically fall back to the configured offline provider when the cloud provider is unreachable (if a fallback is configured).
- **FR-007**: System MUST allow users to configure model, maximum tokens, temperature, timeout, and retry count per provider.

**Schema Context**

- **FR-008**: System MUST build a compressed schema context from the connected database including table/view names, column names with types, primary keys, foreign keys, and extended property descriptions.
- **FR-009**: System MUST dynamically filter schema context to objects relevant to the user's prompt, limiting to a maximum of 500 objects per request.
- **FR-010**: System MUST support multiple compression levels (names only, columns, keys/indexes, descriptions) and select the appropriate level based on the AI request type and available context window.

**Text-to-SQL**

- **FR-011**: System MUST accept natural language input via keyboard shortcut (`Ctrl+Shift+G`) and `--ai:` prefix in the editor.
- **FR-012**: System MUST display AI-generated SQL in a diff-style preview with Accept, Edit, and Reject actions.
- **FR-013**: System MUST never auto-insert or auto-execute AI-generated SQL. All AI suggestions require explicit user confirmation.

**AI Explain**

- **FR-014**: System MUST explain selected SQL (or the current statement if nothing is selected) via `Ctrl+Shift+E` or context menu.
- **FR-015**: Explanations MUST include structured sections: Purpose, Step-by-step, Key Details, and Suggestions.

**AI Fix**

- **FR-016**: System MUST capture SQL execution errors and offer a "Fix with AI" action.
- **FR-017**: System MUST display original vs. fixed SQL in a diff view with inline annotations explaining each change.
- **FR-018**: System MUST support an optional "auto-fix on error" mode, disabled by default, that automatically offers fix suggestions when queries fail.
- **FR-019**: The fix shortcut MUST be `Shift+Alt+R`.

**AI Optimize**

- **FR-020**: System MUST analyze selected SQL for performance improvements via `Ctrl+Shift+O` or context menu.
- **FR-021**: Optimization results MUST categorize changes as "Safe" (can be applied without review) or "Review" (requires human judgment).
- **FR-022**: When applicable, optimization results MUST include index creation suggestions with estimated improvement, size impact, and write performance effects.

**AI Index Suggestions**

- **FR-023**: System MUST analyze a query or execution plan XML for missing index opportunities.
- **FR-024**: Index suggestions MUST include complete CREATE INDEX scripts with improvement estimates and impact analysis.

**AI Chat Panel**

- **FR-025**: System MUST provide a dockable chat panel accessible via `Ctrl+Shift+A`.
- **FR-026**: The chat panel MUST maintain conversation context across multiple turns within a session.
- **FR-027**: The chat panel MUST include actionable buttons (e.g., "Apply Fix", "Copy Script") for code suggestions in AI responses.

**Inline Ghost Text Completion**

- **FR-028**: System MUST display AI-predicted SQL continuations as gray ghost text in the editor.
- **FR-029**: Ghost text MUST be accepted with Tab and dismissed with Escape or continued typing.
- **FR-030**: Inline completion MUST be opt-in (disabled by default) and MUST NOT interfere with the deterministic Phase 2 IntelliSense.
- **FR-031**: Ghost text prediction latency MUST be under 500 milliseconds to avoid disrupting the typing flow.

**Privacy & Security**

- **FR-032**: In "schemaOnly" mode, the system MUST redact all literal data values from queries before transmission, replacing them with generic placeholders.
- **FR-033**: In "anonymous" mode, the system MUST hash all table and column names before transmission while preserving structural relationships (e.g., foreign key links between hashed names).
- **FR-034**: The system MUST show a one-time privacy consent notice the first time any data is sent to a cloud AI provider.
- **FR-035**: In "offline" mode, the system MUST make zero network requests to any external AI service.
- **FR-036**: All AI features MUST be opt-in only. They MUST never alter code without explicit user action.

**Error Handling & Resilience**

- **FR-037**: System MUST handle API rate limiting with request queuing and exponential backoff, displaying a notification to the user.
- **FR-038**: System MUST cancel pending AI requests after the configured timeout and notify the user.
- **FR-039**: System MUST validate AI-generated SQL against the current schema and highlight references to non-existent objects in the diff preview.

### Key Entities

- **AI Provider**: Represents a configured AI service (type, endpoint, model, API key, privacy mode). A user has exactly one active provider at a time, with an optional offline fallback.
- **AI Request**: A single interaction sent to an AI provider, containing a prompt, schema context, and metadata (request type, privacy level). Tracks status (pending, completed, failed, timed out).
- **Schema Context**: A compressed representation of the connected database structure sent with AI requests. Includes objects at varying compression levels filtered by relevance.
- **AI Response**: The result returned by an AI provider, containing generated content (SQL, explanation, fix, optimization), token usage, and latency. Displayed via diff preview, explanation panel, or chat panel.
- **Chat Session**: A sequence of AI requests and responses within the chat panel, maintaining conversational context. Scoped to a single IDE session.
- **Privacy Configuration**: The privacy rules governing what data is transmitted to AI providers. Includes the privacy mode and any redaction/hashing transformations applied.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can generate SQL from a natural language prompt and see a diff preview within 5 seconds (cloud provider) or 15 seconds (local model).
- **SC-002**: At least 85% of AI-generated SQL queries are syntactically valid and execute without error on the target database schema.
- **SC-003**: Users can receive a structured explanation of selected SQL within 3 seconds (cloud provider).
- **SC-004**: At least 70% of common SQL errors are successfully resolved by the AI Fix feature on the first attempt.
- **SC-005**: At least 60% of AI optimization suggestions measurably improve query performance when applied.
- **SC-006**: Schema context preparation completes within 200 milliseconds for databases with up to 1,000 tables.
- **SC-007**: Inline ghost text predictions appear within 500 milliseconds of the user pausing typing.
- **SC-008**: 100% of data transmission in "offline" mode is verified to be zero — no network requests to external services.
- **SC-009**: 100% of data transmitted in "schemaOnly" mode is verified to have all literal values redacted.
- **SC-010**: 100% of data transmitted in "anonymous" mode is verified to have all identifiers hashed.
- **SC-011**: At least 40% of users who install the extension enable at least one AI feature within 30 days.
- **SC-012**: All AI features operate without error across all supported IDE targets (SSMS 20/21/22, VS 2019/2022/2026).
- **SC-013**: Zero cases of AI-generated SQL being auto-executed without explicit user confirmation.
- **SC-014**: The chat panel maintains conversation context accurately across at least 10 conversational turns.

## Assumptions

- Users have access to at least one AI provider (cloud API key or local model installation). The feature gracefully handles the case where no provider is configured.
- The Phase 2 schema cache is available and populated for the connected database. AI features that require schema context fall back to a degraded mode (no schema context) if the cache is unavailable.
- Cloud AI providers (Anthropic, OpenAI, Google, Azure) maintain stable API interfaces. Version-specific adapters handle API differences.
- Local model quality (Ollama, LM Studio) is lower than cloud models. The UI sets clear expectations about model capability differences.
- Token costs for cloud AI providers are the user's responsibility. The system tracks token usage for user awareness but does not enforce spending limits.
- The encrypted API key storage uses the operating system's credential protection mechanism.
- Response streaming is used for cloud providers when available to reduce perceived latency.

## Dependencies

- **Phase 2 (IntelliSense + Schema Cache)**: Required for schema context generation. AI features depend on the schema cache populated by Phase 2.
- **Phase 3 (SQL Formatter)**: Used to format AI-generated SQL before display in the diff preview.
- **Phase 5 (Code Analysis)**: Used to validate AI-generated SQL against analysis rules before display.
- **Phase 8 (Productivity Toolkit)**: The diff preview UI component may be shared with existing Phase 8 features.

## Scope Boundaries

**In Scope:**
- All 7 AI features (text-to-SQL, explain, fix, optimize, index suggestions, chat panel, inline completion)
- Multi-model provider architecture (7 provider types)
- Privacy modes (full, schemaOnly, anonymous, offline, disabled)
- Schema context compression and filtering
- Encrypted API key storage
- Request queuing, retry, and timeout handling
- Keyboard shortcuts and context menu integration
- Settings UI for AI configuration

**Out of Scope:**
- AI-powered database design or schema generation
- AI-powered data migration or ETL
- AI training or fine-tuning on user data
- Integration with non-SQL languages (e.g., Python, R)
- Automated query scheduling based on AI recommendations
- Multi-user shared AI chat sessions
- AI-generated documentation or reports
- Token budget management or spending cap enforcement (the system tracks usage but does not enforce limits)
