---
description: "Task list for 046-ai-chat-copilot-ux"
---

# Tasks: AI Chat — Copilot-style experience in SSMS and the web edition

**Input**: Design documents from `/specs/046-ai-chat-copilot-ux/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/chat-binding.md](./contracts/chat-binding.md), [contracts/chat-rendering.md](./contracts/chat-rendering.md), [contracts/chat-composer-and-agents.md](./contracts/chat-composer-and-agents.md), [quickstart.md](./quickstart.md)

**Tests are not optional here.** Constitution III requires every new behaviour to land with
automated checks and the existing gates to stay green. In every phase, write the test tasks
first, see them fail, then implement. The SSMS scenarios that cannot be automated (the
Messages-pane refusal, theme and screen-reader passes) are owner-run steps from quickstart.md and
end by recording the result in `baseline.md`.

**Organization**: one phase per user story in spec priority order (US1 binding P1, US2 SSMS
Copilot-style P1, US3 keys and hints P2, US4 agent selector P2, US5 New chat P2, US6 web parity
P3). Phase 2 holds only what more than one story needs (the agent label helper, the block parser,
the two reply fields). US1 needs nothing from Phase 2 and can run beside it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel. The task touches different files and does not depend on an unfinished task.
- **[Story]**: `[US1]`…`[US6]`, mapping to the user stories in spec.md.
- `R4`, `Part 3` refer to research.md entries and contract parts. Read the referenced entry before
  starting a task; it holds the verbatim strings, the decision table and the rejected alternatives.

## Rules for every task

- Repository root: `D:\Repo\01-Khamis-Projects\AKML-SQL`. Shell sources are in the shared project
  `src/AkmlSql.Shell.Shared/` (a `.projitems`, compiled into `src/AkmlSql.Ssms22`); build shell
  projects with **MSBuild only**, never `dotnet build`. Core is `src/AkmlSql.Core/` (must compile for
  **netstandard2.0 and net10.0**: no `Range`/`Index` operators, no `string.Contains(char)`, no
  `Span` APIs), the engine `src/AkmlSql.Engine/`, the web `src/AkmlSql.Web/`, tests under `tests/`.
- **Strings are contract text.** Every user-facing string (refusal texts, tooltips, placeholder,
  notices, labels, starters) is written verbatim from the contracts and data-model.md. Tests assert
  the exact text.
- **One binding** (chat-binding.md Part 2): after US1, `AiChatPanel` reads the session, the header,
  the schema poll and the insert target from `_binding` only. `RefactorCommandHelper.TryGetActiveRealSessionId`
  is never called from the chat again; the other AI commands keep using it.
- **Never a fabricated session id** (spec 036 R1): a refusal sends nothing.
- **Notices are not history**: `AddConversationNotice` lines are not sent, copied or exported.
- **MessagePack DTOs**: append `[Key(7)]`, `[Key(8)]` only; never renumber; no new message type
  or capability string.
- **No new theme token**: WPF brushes only through `SetResourceReference(…, ThemeTokens.X)`;
  web colours only through the generated `--akml-*` variables; no hex literal in new code.
  `FontFamily` instances are `static readonly`.
- **WPF tests**: `[StaFact]` in `[Collection("AkmlSql ThemeRegistry")]`, settings through
  `AiChatPanel.TestSettingsProvider`, the binding through the new `AiChatPanel.TestBindingResolver`
  seam (US1), Options through `ShowOptionsRoute`, inserts through `AiEditorSqlInserter.OverrideForTests`.
- **Web tests**: bUnit classes derive from `BunitContext` and register the fakes in
  `tests/AkmlSql.Web.Tests/AiChatPanelFakes.cs`; JS modules are mocked with `JSInterop.SetupModule`.
- **Browser tests need Debug builds** of `src/AkmlSql.Web` and `src/AkmlSql.Engine`; run
  `tests/AkmlSql.Web.E2E.Tests` serially (`-- xUnit.ParallelizeTestCollections=false`).
- **SSMS steps are the owner's.** Do not drive SSMS or the desktop by automation while the owner is
  working; hand over the quickstart step, wait for the recorded result.
- Deliver uncommitted. Say "Ready to commit?" after a phase; commit only on the owner's word.

---

## Phase 1: Setup (baseline and gates)

**Purpose**: record today's behaviour and prove the gates are green before anything changes.

- [ ] T001 Create `specs/046-ai-chat-copilot-ux/baseline.md` with a "Before" section: the owner's
      reproduction of the refusal (quickstart S1-1 as it behaves today), the log evidence of
      2026-11-04 (three `AiChat` successes, none after 17:03:53, caption detector still reporting
      the window), and an empty results table for every quickstart scenario (S1-1…S5-3, W1–W6).
- [ ] T002 Run the existing chat suites and record their counts in `baseline.md`:
      `dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj --filter "FullyQualifiedName~AiChat"`,
      `dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj --filter "FullyQualifiedName~AiChat"`,
      `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "FullyQualifiedName~Agent|FullyQualifiedName~AiSessionBinding"`,
      `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj --filter "Category!=InstallerSmoke"`.
- [ ] T003 [P] Build `src/AkmlSql.Web/AkmlSql.Web.csproj` and `src/AkmlSql.Engine/AkmlSql.Engine.csproj`
      in Debug and confirm Playwright's Chromium is installed (`tests/AkmlSql.Web.E2E.Tests` skips
      otherwise); note in `baseline.md` whether the browser suites can run on this machine.

---

## Phase 2: Foundational (shared seams)

**Purpose**: the three pieces more than one story needs — the agent label helper (US2, US4, US6),
the block parser (US2, US6) and the reply's agent fields (US2, US4).

**⚠️ CRITICAL**: US2, US4 and US6 wait for this phase. US1 does not.

### Tests for Phase 2 (write first; they must fail)

- [ ] T004 [P] Create `tests/AkmlSql.Core.Tests/Config/AiAgentLabelTests.cs`: `AiAgentLabel.Format`
      returns `Gemini Flash · Google Gemini · gemini-2.5-flash` for a full agent, omits a blank
      model (`Local · Ollama`), omits a blank provider, returns the name alone when the rest is
      blank, and returns `Google Gemini · gemini-2.5-flash` when the name is blank; provider ids go
      through `AiProviderIds.DisplayName` (`gemini` → `Google Gemini`, unknown id → the id itself).
- [ ] T005 [P] Create `tests/AkmlSql.Core.Tests/Text/ChatMarkdownTests.cs` covering every case in
      chat-rendering.md Part 1: empty and whitespace input → no blocks; one and two paragraphs;
      headings 1–3 and `####` as a paragraph; `-` and `*` bullets into one unordered list; numbered
      list; list followed by a paragraph; fenced `sql` block between prose (Index 1); two fences
      (Index 1 and 2, `Count` of code blocks = 2); fence without a language; unclosed fence keeps
      all remaining text with `Unclosed = true`; inline code and bold; unclosed backtick is literal;
      CRLF input equals LF input; a fence marker inside a paragraph line is literal; language is
      lower-cased and trimmed.
- [ ] T006 [P] Create `tests/AkmlSql.Engine.Tests/Ai/AiChatResponseAgentFieldsTests.cs`: a MessagePack
      round trip of `AiChatResponse` keeps `AgentProvider` (Key 7) and `AgentModel` (Key 8); a payload
      serialized from a six-key copy of the type deserializes with both null; the handler path fills
      them from a fallback-chain agent and sets only the provider id for the offline provider —
      drive the handler the way `tests/AkmlSql.Engine.Tests/Ai/AgentFallbackChainTests.cs` does.

### Implementation for Phase 2

- [ ] T007 [P] Create `src/AkmlSql.Core/Config/AiAgentLabel.cs`: `public static string Format(string? name, string? providerId, string? model)`
      joining the non-blank parts with ` · ` (U+00B7 with spaces), the provider through
      `AiProviderIds.DisplayName`; XML doc naming the six consumers (contract Part 3).
- [ ] T008 [P] Create `src/AkmlSql.Core/Text/ChatMarkdown.cs`: `ChatBlock` (`Paragraph`, `Heading`,
      `ListBlock`, `CodeBlock` with `Language`, `Code`, `Index`, `Unclosed`), `ChatInline` (`Text`,
      `Bold`, `Code`) and `IReadOnlyList<ChatBlock> Parse(string text)` per the grammar in
      chat-rendering.md Part 1 (line-based, blank lines separate blocks, no `Range`/`Index`
      operators); make T005 pass.
- [ ] T009 Add `AgentProvider` (`[Key(7)]`) and `AgentModel` (`[Key(8)]`) to
      `src/AkmlSql.Core/Ipc/Messages/AiChatResponse.cs` with doc comments in the Key 6 style
      (append-only, null from an older engine).
- [ ] T010 Extend `ExecuteWithFallbackAsync` in `src/AkmlSql.Engine/Ai/AiPipelineServices.cs` to also
      return the answering `AiAgent?` (null for the offline provider, whose provider id is returned
      beside it), and set `AgentProvider` / `AgentModel` in
      `src/AkmlSql.Engine/Handlers/Ai/AiChatHandler.cs` (R4); make T006 pass. Update every other
      caller of the tuple in `src/AkmlSql.Engine/Handlers/Ai/` to discard the new element.
- [ ] T011 Add the two rows under `AiChat (75) → AiChatResult (175)` in `doc/ipc-api.md`, in the Key 6
      row's style (spec 046, R4, append-only, older-engine behaviour).

**Checkpoint**: Core and engine tests green; `AiAgentLabel`, `ChatMarkdown` and the reply fields exist.

---

## Phase 3: User Story 1 — The chat keeps answering about the connected database (Priority: P1) 🎯 MVP

**Goal**: the SSMS chat follows the active query window whatever pane last had the keyboard,
header and send never disagree, refusals say what they found and log it, "Insert into query"
targets the same window.

**Independent Test**: quickstart S1-1…S1-7 in SSMS 22 plus `ChatBindingResolverTests` and the
extended `AiChatSessionBindingTests`.

### Tests for User Story 1 (write first; they must fail)

- [ ] T012 [P] [US1] Create `tests/AkmlSql.Shell.Shared.Tests/ChatBindingResolverTests.cs` for the pure
      `ChatBindingResolver.Decide`: all six rows of the decision table in chat-binding.md Part 1
      (bound; tracked but no caption → `NotConnected` with the session id; active document not
      tracked with a usable previous → previous with `IsSticky`; without → `NotQueryWindow`; no
      document with usable previous → sticky; without → `NoDocument`); a previous binding whose
      window closed is dropped; `DocumentName` is the file name of `DocumentPath`; `BindingKey`
      compares ordinal-ignore-case; no row ever yields a session id that was not supplied.
- [ ] T013 [P] [US1] Extend `tests/AkmlSql.Shell.Shared.Tests/AiChatSessionBindingTests.cs` through the
      new `AiChatPanel.TestBindingResolver` seam: header text `{Server}.{Database}` and tooltip
      `{DocumentName} — {Server}.{Database}` for `Bound`; `Not connected` otherwise; a send under
      `Bound` passes the binding gate — off-host `EngineLifecycle.Manager` is null, so the
      observable is the existing `Error: AI engine is not connected…` message and none of the three
      refusal texts; `NotConnected`, `NotQueryWindow`, `NoDocument` add the three verbatim Part 3
      texts and send nothing; each refusal writes one Information line matching
      `AiChatPanel: refused to send — {State}; document=…; queryWindow=…; session=…; connected=…`
      captured through an `ILogEventSink` implemented inline in the test and swapped into
      `Log.Logger` for its duration (no sink package is added — technology pins); the rebind
      notice `Now answering about {Server}.{Database} ({DocumentName}).` appears once on a key change
      and never on the first binding or an unchanged key; all `NoConnectionMessage` references removed.
- [ ] T014 [P] [US1] Extend `tests/AkmlSql.Shell.Shared.Tests/SsmsConnectionDetectorTests.cs` with the
      caption of a **disconnected** query window (the exact text the owner records from SSMS 22 in
      S1-6; until then use `SQLQuery1.sql - not connected`) → `ParseCaption` returns null, and the
      `Executing...` suffix keeps parsing as connected (research R1 edge, advisor note 1).
- [ ] T015 [P] [US1] Extend `tests/AkmlSql.Shell.Shared.Tests/AiChatInsertSqlTests.cs`:
      `AiEditorSqlInserter.TryInsertAtCaret(sql, documentPath)` passes the path to the
      `OverrideForTests` seam; a null path takes the active-document route; the panel's insert button
      passes `_binding.DocumentPath`.

### Implementation for User Story 1

- [ ] T016 [US1] Add `TryFindTrackedView(string filePath, out string sessionId, out IWpfTextView view)`
      to `src/AkmlSql.Shell.Shared/Editor/ConnectionWiringHelper.cs`: iterate `OpenViews`, drop closed
      or collected entries, compare each view's `ITextDocument.FilePath` ordinal-ignore-case.
- [ ] T017 [US1] Create `src/AkmlSql.Shell.Shared/Ai/ChatBindingResolver.cs` with `ChatBindingState`,
      `ChatBinding` (fields and `BindingKey` per data-model.md), `ActiveDocumentInfo`,
      `TrackedViewInfo`, the pure `Decide(...)` (make T012 pass) and the UI-thread `Resolve(previous)`:
      `DTE.ActiveDocument?.FullName` → `TryFindTrackedView` → `SsmsConnectionDetector.TryDetectConnection(sp, view)`;
      sticky check = previous view still open and its caption still parses; any host exception →
      `NoDocument` with a Debug log (R1).
- [ ] T018 [US1] Change `src/AkmlSql.Shell.Shared/Ai/AiEditorSqlInserter.cs` to
      `TryInsertAtCaret(string sql, string? documentPath)`: find the `DTE.Documents` item whose
      `FullName` equals the path (indexed loop, as `SsmsConnectionDetector` does), insert at its
      selection; null path keeps today's active-document route; `OverrideForTests` receives the path;
      make T015 pass.
- [ ] T019 [US1] Rewire `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` to one binding: `_binding` field
      and `internal static Func<ChatBinding?, ChatBinding>? TestBindingResolver`; `RefreshBinding`
      sets `_binding = ChatBindingResolver.Resolve(_binding)` (load, tick, tool-window creation, start
      of `SendMessageAsync`); `SetDatabaseContext` and the header tooltip from the binding;
      `PollSchemaStatusAsync(_binding.SessionId)` only when `Bound`; `SendMessageAsync` sends
      `_binding.SessionId` when `Bound`, otherwise adds the Part 3 text for the state and returns;
      `OnInsertSqlClick` passes `_binding.DocumentPath`; delete `NoConnectionMessage` and the
      `TryGetActiveRealSessionId` call (chat-binding.md Part 2–3).
- [ ] T020 [US1] In `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` add the FR-005 `Log.Information` line
      (Part 4, written before the refusal bubble, file name only, no path/server/login/SQL) and the
      FR-007 rebind notice through `AddConversationNotice` (Part 5, previous bound key kept in
      `_lastBindingKey`); make T013 pass; run the whole `tests/AkmlSql.Shell.Shared.Tests` project.
- [ ] T021 [US1] Owner runs quickstart S1-1…S1-7 in SSMS 22; record each result, the disconnected
      caption text (feed it back into T014) and the log lines in `baseline.md`. Known edge to
      record, not fix: a brand-new query window can refuse once as "still connecting" before SSMS
      registers its document (advisor note 2).

**Checkpoint**: the reported defect is gone; the spec's MVP ships here if wanted.

---

## Phase 4: User Story 2 — A Copilot-style conversation in SSMS (Priority: P1)

**Goal**: rendered answers with code panels carrying their own Copy and Insert, attribution with
provider and model, a welcome card with starters, a working state that names the agent, and a
view that follows only when the user is at the bottom.

**Independent Test**: quickstart S2-1…S2-4 plus `AiChatRenderingTests`, `AiChatWelcomeCardTests`
and the extended copy tests.

### Tests for User Story 2 (write first; they must fail)

- [ ] T022 [P] [US2] Create `tests/AkmlSql.Shell.Shared.Tests/AiChatRenderingTests.cs` (STA) for
      `ChatMessageRenderer.Build`: prose blocks become one read-only, borderless `RichTextBox` whose
      `FlowDocument` holds paragraphs, a bold heading, a `List` and a `Bold` run and an inline-code run
      in the editor font; one `Border` per code block with a read-only `TextBox` holding the code; the
      header row has `⧉ Copy` and, for `sql`/`tsql`/empty languages only, `⇩ Insert into query`;
      labels `Copy SQL block 1 of 2` / `Insert SQL block 2 of 2` when two blocks exist; an unclosed
      fence still renders a panel; Copy and Insert invoke the callbacks with that block's code;
      every button has an automation name and the tooltips from chat-composer-and-agents.md Part 2.
- [ ] T023 [P] [US2] Create `tests/AkmlSql.Shell.Shared.Tests/AiChatWelcomeCardTests.cs` (STA): title
      `Ask about {server}.{database}` for a bound binding and `Not connected to a database`
      otherwise; exactly the three starter texts of chat-rendering.md Part 4 with tooltip
      `Send this question`; a click raises `StarterRequested` with the text; `IsBusy = true` disables
      the starters.
- [ ] T024 [P] [US2] Extend `tests/AkmlSql.Shell.Shared.Tests/AiChatPanelCopyButtonTests.cs`: the
      per-message copy of an answer with a fenced block copies the raw text with the fences; copy
      conversation is unchanged; `AttributionText` with labels — same label → the label, different
      → `{label} answered — {selectedLabel} was unavailable.`, blank name → null.
- [ ] T025 [P] [US2] Extend `tests/AkmlSql.Shell.Shared.Tests/AiChatComposerStateTests.cs`: the
      working indicator reads `Asking {label}…` while sending; the pure
      `AiChatPanel.ShouldFollow(verticalOffset, scrollableHeight)` is true within 8 px of the bottom
      and false above it.

### Implementation for User Story 2

- [ ] T026 [P] [US2] Create `src/AkmlSql.Shell.Shared/Ai/ChatMessageRenderer.cs` per chat-rendering.md
      Part 2: `Build(IReadOnlyList<ChatBlock>, Action<string> copy, Action<string> insert) → StackPanel`;
      prose in a read-only `RichTextBox` (`IsDocumentEnabled`, `PagePadding 0`, transparent,
      `TextPrimary`), headings 14/13/12 semibold, `List` markers Disc/Decimal, inline code in
      `ThemeTokens.TypographyEditor` on `SurfaceInputReadOnly`; code panels on `SurfaceCanvas` with
      `BorderDefault`, radius 4, header row (language label `TextSecondary`, Copy / Insert buttons on
      `SurfaceElevated` with `TextLink`, `FocusVisualStyles.HighStakes`), read-only editor-font
      `TextBox` with horizontal scroll; `FlashButtonContent` outcomes `✓ Copied` / `⚠ Copy failed` /
      `✓ Inserted` / `⚠ No query window`; make T022 pass.
- [ ] T027 [P] [US2] Create `src/AkmlSql.Shell.Shared/Ai/AiChatWelcomeCard.cs` per chat-rendering.md
      Part 4: `SurfaceElevated` card, `SetBinding(ChatBinding)` updates the title, three starter
      buttons, `StarterRequested` event, `IsBusy`; make T023 pass.
- [ ] T028 [US2] Update `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs`: `AddAssistantMessage` renders
      through `ChatMessageRenderer` (user bubbles unchanged, per-message copy keeps the raw text) and
      no longer appends the detached action row (`CodeActions` ignored for rendering); attribution
      caption = `AiAgentLabel.Format(response.AgentName, response.AgentProvider, response.AgentModel)`
      compared with the selected agent's label; `_thinkingIndicator` text `Asking {label}…`;
      `RenderAgentState` shows `AiChatWelcomeCard` instead of the greeting bubble while usable and
      `_history` is empty, removes it on the first user message, re-shows it after New chat (US5);
      a starter sets the composer text and calls `SendMessageAsync`; `ScrollToBottom(bool force)`
      uses `ShouldFollow` captured before the append (the user's own message forces); make T024,
      T025 pass.
- [ ] T029 [US2] Theme and accessibility pass in SSMS (quickstart S2-4): light, dark, high contrast;
      Tab reaches every control; Narrator announces each by name; fix any hardcoded brush or missing
      automation name found; record in `baseline.md`.
- [ ] T030 [US2] Owner runs quickstart S2-1…S2-3; record in `baseline.md`.

**Checkpoint**: the SSMS conversation reads like Copilot; US1 + US2 is the owner's "more in SSMS" ask.

---

## Phase 5: User Story 3 — Send with Ctrl+Enter and discover it (Priority: P2)

**Goal**: the Send control, the placeholder and every control explain themselves on hover in both
editions. Key handling is unchanged (both editions already send on Ctrl+Enter, R14).

**Independent Test**: quickstart S3-1…S3-3 and W3 plus the extended composer tests.

### Tests for User Story 3 (write first; they must fail)

- [ ] T031 [P] [US3] Extend `tests/AkmlSql.Shell.Shared.Tests/AiChatComposerStateTests.cs`: Send tooltip
      `Send (Ctrl+Enter)` with text and idle, `Type a question first` when empty,
      `Wait for the answer to finish` while sending, `No AI agent can answer yet — add one in Options`
      with no usable agent; the placeholder reads
      `Ask about this database…   (Enter or Ctrl+Enter sends · Shift+Enter for a new line)`; the
      picker tooltip is `Choose which agent answers your messages`; Copy conversation and
      per-message copy keep their tooltips.
- [ ] T032 [P] [US3] Extend `tests/AkmlSql.Web.Tests/AiChatPanelComposerTests.cs`: the Send `title` for
      the four states (web no-agent wording `… add one in Settings`); the textarea placeholder
      `Ask anything about your SQL…   (Enter or Ctrl+Enter sends · Shift+Enter for a new line)`; the
      Export button `title` `Download the conversation as Markdown`.
- [ ] T033 [P] [US3] Extend `tests/AkmlSql.Web.E2E.Tests/AiChatComposerKeysTests.cs`: with text in the
      composer the Send button's `title` is `Send (Ctrl+Enter)`; Ctrl+Enter still sends (existing
      assertion kept).

### Implementation for User Story 3

- [ ] T034 [US3] In `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` add `UpdateSendTooltip()` (called from
      `TextChanged`, the `_isSending` transitions and `RenderAgentState`), the new placeholder text,
      and the tooltips of chat-composer-and-agents.md Part 2 on the picker, Copy conversation and
      per-message copy; make T031 pass.
- [ ] T035 [US3] In `src/AkmlSql.Web/Shared/AiChatPanel.razor` compute the Send `title` for the four
      states, set the new placeholder and the Export `title`; make T032 pass; rebuild the web app in
      Debug and run T033.
- [ ] T036 [US3] Owner runs quickstart S3-1…S3-3. If S3-1 shows Ctrl+Enter does not reach the composer
      in SSMS 22, add a `PreviewKeyDown` handler on the panel root in
      `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` that routes Ctrl+Enter to `SendMessageAsync`
      (R14 fallback) and re-run; record in `baseline.md`.

**Checkpoint**: the panel is self-explaining; nothing about sending changed.

---

## Phase 6: User Story 4 — Switch the answering model clearly (Priority: P2)

**Goal**: the selector shows name, provider and model, sits at the composer in both editions, a
switch is announced in the conversation, and the web edition gets a selector in its chat.

**Independent Test**: quickstart S4-1…S4-3 and W1–W2 plus `AiChatAgentPickerTests`,
`AiAgentSelectorTests` and the web attribution tests.

### Tests for User Story 4 (write first; they must fail)

- [ ] T037 [P] [US4] Extend `tests/AkmlSql.Shell.Shared.Tests/AiChatAgentPickerTests.cs`: entries read
      `AiAgentLabel.Format(name, provider, model)`; `Add agent…` stays last; selection by agent id
      still works; after a pick that changes the resolved agent the panel adds one notice
      `Now answering with {label}.`; re-picking the same agent and choosing `Add agent…` add none.
- [ ] T038 [P] [US4] Create `tests/AkmlSql.Web.Tests/AiAgentSelectorTests.cs` (bUnit, with a new
      `FakeAiKeyVault` in `tests/AkmlSql.Web.Tests/AiChatPanelFakes.cs`): only usable agents are listed
      (`Enabled` and `HasKey` or a provider for which `AiAgentResolver.RequiresApiKey` is false);
      option text is the label; the selected option is the active id; the last option is
      `Manage agents…` and choosing it navigates to `settings/ai-assistance` (bUnit's
      `NavigationManager`) without calling `SetActiveAsync`; choosing an agent calls
      `IAiPreference.SetActiveAsync(id)` and raises `Changed`; the `select` has
      `aria-label="Answering agent"` and `data-testid="chat-agent-select"`.
- [ ] T039 [P] [US4] Create `tests/AkmlSql.Web.Tests/AiChatPanelAgentTests.cs` (bUnit): an answer's
      `ChatTurn.AgentLabel` is the active agent's label at send time; the role label shows it; a
      stored turn without a label renders `Assistant`; after the selector's `Changed` the panel shows
      the line `Now answering with {label}.` and it is not saved to the history store;
      `ChatConversation.ToMarkdown` uses the label when present.

### Implementation for User Story 4

- [ ] T040 [US4] In `src/AkmlSql.Shell.Shared/Ai/AiAgentPicker.cs` build each entry with
      `AiAgentLabel.Format(agent.Name, agent.Provider, agent.Model)`, keep `Add agent…`, set the
      tooltip `Choose which agent answers your messages`, widen `MinWidth` for the longer labels.
- [ ] T041 [US4] In `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` move the picker into a composer bar
      (input on top; a bottom row with the picker left and Send right, chat-composer-and-agents.md
      Part 6); the header keeps title, binding label, New chat (US5 slot) and Copy conversation; in
      `OnChatAgentSelected` compare the resolved agent id before and after `RenderAgentState` and add
      `AddConversationNotice($"Now answering with {label}.")` on a change; make T037 pass.
- [ ] T042 [P] [US4] Add `AgentLabel` (string?, additive) to `ChatTurn` in
      `src/AkmlSql.Web/Services/IChatHistoryStore.cs` and use it in `ChatConversation.ToMarkdown`
      (`AgentLabel ?? "Assistant"` as the speaker).
- [ ] T043 [P] [US4] Create `src/AkmlSql.Web/Shared/AiAgentSelector.razor` per Part 6 (injects
      `IAiKeyVault`, `IAiPreference`, `NavigationManager`; `EventCallback<string> Changed`; labels
      through `AiAgentLabel.Format(c.DisplayName, c.ProviderId, c.Model)`; the `select` carries
      `title="Choose which agent answers your messages"`, FR-013); make T038 pass.
- [ ] T044 [US4] In `src/AkmlSql.Web/Shared/AiChatPanel.razor` host `AiAgentSelector` in the composer
      footer, set `assistant.AgentLabel` from the active agent when the turn is created, render the
      role label as `AgentLabel ?? "Assistant"`, on `Changed` re-read `_hasProvider` and add the
      non-persisted `Now answering with {label}.` line, replace the working text with
      `Asking {label}…`; make T039 pass.
- [ ] T045 [US4] Owner runs quickstart S4-1…S4-3 and W1–W2; record in `baseline.md`.

**Checkpoint**: the answering model is visible and switchable at the composer in both editions.

---

## Phase 7: User Story 5 — Start a new conversation easily (Priority: P2)

**Goal**: one New chat control in each edition clears the conversation, keeps binding and agent,
focuses the composer, and sends no earlier turn afterwards.

**Independent Test**: quickstart S5-1…S5-3 and W5 plus the extended composer tests.

### Tests for User Story 5 (write first; they must fail)

- [ ] T046 [P] [US5] Extend `tests/AkmlSql.Shell.Shared.Tests/AiChatComposerStateTests.cs`: the header
      has a button named `New chat` with tooltip `Start a new conversation — clears this one`,
      disabled with `The conversation is already empty` when there are no turns and with
      `Wait for the answer to finish` while sending; after `NewConversation()` the conversation panel
      holds only the welcome card, the next request carries an empty `History` (assert through the
      panel's `internal int HistoryCount`), the header text and the picker selection are unchanged,
      and `NewConversation()` requested composer focus — assert the new
      `internal bool ComposerFocusRequested` seam, because the STA harness constructs the panel
      without a shown `Window` and WPF `Focus()` cannot succeed there; grep the test project for
      `Focus()` first and follow any existing pattern.
- [ ] T047 [P] [US5] Extend `tests/AkmlSql.Web.Tests/AiChatPanelComposerTests.cs`: a `New chat` button
      (`data-testid="chat-new"`) exists whenever a provider is configured, is disabled with
      `The conversation is already empty` when there are no turns, and no `Clear` button remains;
      clicking it calls `IChatHistoryStore.ClearAsync`, empties the turns and shows the empty state;
      clicking it during a never-completing stream from `FakeAiClientFactory` cancels the stream and
      leaves `_busy` false.
- [ ] T048 [P] [US5] Extend `tests/AkmlSql.Web.E2E.Tests/UserStory5AiTests.cs` (quickstart W5): New
      chat during a streaming mock answer stops it and empties the conversation; a reload shows the
      empty conversation.

### Implementation for User Story 5

- [ ] T049 [US5] In `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` add the header button `＋ New chat`
      (automation name `New chat`, `FocusVisualStyles.HighStakes`, tooltips per the composer-state
      table in data-model.md) and `internal void NewConversation()`: clear `_conversationPanel`,
      `_history`, `_historyAgentNames`, `_greetingBubble`; keep `_binding`, `_lastBindingKey`, the
      picker, the schema and privacy notes; call `RenderAgentState` so the welcome card returns;
      `_inputBox.Focus()`; update the button's enablement on turn count and `_isSending`; make T046 pass.
- [ ] T050 [US5] In `src/AkmlSql.Web/Shared/AiChatPanel.razor` replace `Clear` with `New chat`
      (`data-testid="chat-new"`, shown whenever `_hasProvider`, disabled with the empty tooltip when
      there are no turns): cancel `_cts`, run the existing `ClearAsync` logic, then focus the textarea
      through `akml-ui.js focusElement`; update `tests/AkmlSql.Web.Tests/Services/CommandRegistryStateTests.cs`
      only if it referenced the chat's Clear (check first); make T047 pass; run T048.
- [ ] T051 [US5] Owner runs quickstart S5-1…S5-3; record in `baseline.md`.

**Checkpoint**: a clean slate is one click in both editions.

---

## Phase 8: User Story 6 — The web edition gets the shared improvements (Priority: P3)

**Goal**: rendered answers with code panels and Insert at caret, starters in the empty state,
follow-scroll, in the web chat. (The web selector, New chat and hints landed in US3–US5.)

**Independent Test**: quickstart W3–W6 plus `ChatMessageViewTests`, the scroll node test and the
E2E code-panel scenario.

### Tests for User Story 6 (write first; they must fail)

- [ ] T052 [P] [US6] Create `tests/AkmlSql.Web.Tests/ChatMessageViewTests.cs` (bUnit, `akml-ui.js`
      mocked with `JSInterop.SetupModule`): paragraphs, headings (`h4`–`h6`), `ul`/`ol`, `strong` and
      `code.akml-chat-inline` render; one `[data-testid='chat-code-block']` per fence with a `Copy`
      button and an `Insert at caret` button when `CanInsert`; `CanInsert=false` renders no Insert;
      Insert invokes `OnInsert` with that block's code; Copy calls `copyText` and shows `Copied`;
      labels `Copy SQL block 1 of 2` when two blocks exist; the tooltips of
      chat-composer-and-agents.md Part 2.
- [ ] T053 [P] [US6] Create `tests/AkmlSql.Web.Tests/js/akml-ui.scroll.test.mjs`: `isAtBottom` is true
      within the 8 px threshold and false above it (fake element with `scrollTop`, `scrollHeight`,
      `clientHeight`); `scrollToBottom` sets `scrollTop` to `scrollHeight`.
- [ ] T054 [P] [US6] Extend `tests/AkmlSql.Web.E2E.Tests/AiChatComposerKeysTests.cs` (quickstart W4): a
      mock answer containing a ```` ```sql ```` block renders `chat-code-block`; `Insert at caret`
      puts exactly that SQL into the editor (`sql-editor` content).
- [ ] T055 [P] [US6] Extend `tests/AkmlSql.Web.Tests/AiChatPanelCopyTests.cs`: per-turn copy of an
      answer with a fenced block copies the raw text; Export keeps the fences; the empty state shows
      three `[data-testid='chat-starter']` buttons with the Part 4 texts and clicking one sends it.

### Implementation for User Story 6

- [ ] T056 [P] [US6] Add `export function isAtBottom(el, threshold = 8)` and
      `export function scrollToBottom(el)` to `src/AkmlSql.Web/wwwroot/js/akml-ui.js`; make T053 pass.
- [ ] T057 [P] [US6] Create `src/AkmlSql.Web/Shared/ChatMessageView.razor` per chat-rendering.md Part 3
      (parameters `Text`, `CanInsert`, `OnInsert`; `ChatMarkdown.Parse`; styles on
      `--akml-surface-canvas`, `--akml-border-default`, `--akml-surface-inputreadonly` and the editor
      font variables; `copyText` through `akml-ui.js`; `Copied` for 1.5 s); make T052 pass.
- [ ] T058 [US6] In `src/AkmlSql.Web/Shared/AiChatPanel.razor` render assistant turns through
      `ChatMessageView`, add `[Parameter] public EventCallback<string> OnInsertSql` (`CanInsert =
      OnInsertSql.HasDelegate`), add the three starter buttons (`title="Send this question"`) to
      `akml-chat-empty`, capture
      `isAtBottom` before adding turns or tokens and call `scrollToBottom` after render when it was
      true or the user sent, let assistant turns span the full width (user turns keep their bubble);
      make T055 pass.
- [ ] T059 [US6] In `src/AkmlSql.Web/Pages/Editor.razor` pass `OnInsertSql="OnAiAcceptResultAsync"` to
      `<AiChatPanel />`; rebuild Debug and run T054.
- [ ] T060 [US6] Run quickstart W3–W6 (Playwright where listed, Chromium by hand otherwise); record in
      `baseline.md`.

**Checkpoint**: both editions deliver every story.

---

## Phase 9: Polish & cross-cutting concerns

- [ ] T061 [P] Update `doc/use-cases.md` (AI Chat: the query-window binding rule and the three refusal
      texts, Ctrl+Enter, the selector with provider and model, New chat, code panels with Copy and
      Insert) — the served docs ingest `doc/` automatically.
- [ ] T062 [P] Add the spec 046 section to `doc/progress.md` (phases, decisions R1–R14, deferred items
      below) and one bullet under "Latest merged work" in `CLAUDE.md`.
- [ ] T063 Run the full gates from quickstart §Build and §Tests: one-pass Release solution build
      (MSBuild), `generate-theme-css.ps1 -CheckOnly`, whole `tests/AkmlSql.Shell.Shared.Tests`,
      `tests/AkmlSql.Web.Tests`, `tests/AkmlSql.Core.Tests` (`Category!=InstallerSmoke`),
      `tests/AkmlSql.Engine.Tests`, `node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"`, the chat
      E2E classes serially; record counts in `baseline.md`.
- [ ] T064 Record the success criteria in `baseline.md`: SC-001/002 from the S1 runs, SC-003 from the log
      check, SC-004/005/006 timings, SC-007 the five-item side-by-side checklist against GitHub
      Copilot Chat, SC-008 the screen-reader names in three themes.
- [ ] T065 Review pass over the diff: the new log line carries no path, server, login or SQL;
      `AiChatPanel.cs` no longer references `TryGetActiveRealSessionId` or `NoConnectionMessage`;
      no hex literal in new WPF or Razor code (`grep -n "#[0-9A-Fa-f]\{6\}"`); every new control has a
      tooltip and an automation name; `AiChatRequest` unchanged.
- [ ] T066 Check every FR-001…FR-043 against a task or a `baseline.md` entry; note any gap in
      `specs/046-ai-chat-copilot-ux/checklists/requirements.md`; say "Ready to commit?".

---

## Deferred (recorded with reasons; constitution Development Workflow)

- **Streaming answers in SSMS** — owner's decision of 2026-11-04 (Q1: A): a follow-up spec; it
  needs an engine-to-shell streaming contract and a Stop control.
- **Browsable history of past conversations** — owner's decision (Q2: A): one conversation, New
  chat replaces it.
- **A chat-only agent in the web edition** — the web keeps one active agent shared by all AI
  actions (spec Assumptions); a separate chat preference is a new store.
- **`PreviewKeyDown` Ctrl+Enter fallback** — only if S3-1 fails in SSMS 22 (R14).
- **Transient "still connecting" refusal on a brand-new query window** — the detector's own retry
  window; the wording covers it; a stickier wait is not worth the complexity (advisor note 2).
- **Engine `CodeActions` on the reply** — kept on the wire for older shells; the renderer derives
  actions from the parser; removing the field would be an IPC change.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: after Setup. Blocks US2 (parser, label, reply fields), US4 (label,
  reply fields) and US6 (parser). Does **not** block US1.
- **US1 (Phase 3)**: after Setup only; runs beside Phase 2.
- **US2 (Phase 4)**: after Phase 2 and US1 (the welcome card reads `ChatBinding`).
- **US3 (Phase 5)**: after US2 for the SSMS tooltips on renderer buttons (T022 covers them) and
  otherwise independent; the web half is independent.
- **US4 (Phase 6)**: after Phase 2; the SSMS composer-bar move (T041) should land after US2's
  renderer so the panel layout is touched once.
- **US5 (Phase 7)**: SSMS after US2 (re-showing the welcome card); web independent.
- **US6 (Phase 8)**: after Phase 2; T058 after T044 and T050 (same file, `AiChatPanel.razor`).
- **Polish (Phase 9)**: after every story the owner wants shipped.

### Cross-story touch points (soft dependencies)

- `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` is edited in US1, US2, US3, US4 and US5 — do those
  phases in order; do not run two of them in parallel.
- `src/AkmlSql.Web/Shared/AiChatPanel.razor` is edited in US3, US4, US5 and US6 — same rule.
- `tests/AkmlSql.Shell.Shared.Tests/AiChatComposerStateTests.cs` grows in US2, US3 and US5;
  `tests/AkmlSql.Web.Tests/AiChatPanelComposerTests.cs` in US3 and US5 — append sections, keep
  earlier cases green.

### Within each story

- Tests first (they must fail), then implementation, then the owner's quickstart steps recorded in
  `baseline.md`.
- Pure helpers (`Decide`, `ShouldFollow`, `AiAgentLabel`, `ChatMarkdown`) before the UI that uses them.

### Parallel opportunities

- Phase 2 tests T004–T006 together; then T007–T008 together.
- US1 tests T012–T015 together; T016 → T017 → T019 is sequential; T018 beside T017.
- US2 tests T022–T025 together; T026 and T027 together; then T028.
- US4: T040 beside T042–T043; T041 and T044 after their tests.
- US6 tests T052–T055 together; T056 and T057 together; then T058, T059.
- Web stories (US4 web half, US5 web half, US6) can proceed while the SSMS stories wait for the
  owner's manual runs, as long as `AiChatPanel.razor` is edited by one phase at a time.

---

## Parallel Example: User Story 1 (tests)

```bash
# Four test files, no shared state:
Task: "T012 ChatBindingResolverTests — decision table, sticky, keys"
Task: "T013 AiChatSessionBindingTests — header, refusals, log line, rebind notice"
Task: "T014 SsmsConnectionDetectorTests — disconnected caption"
Task: "T015 AiChatInsertSqlTests — insert targets the bound document"
```

## Parallel Example: User Story 6 (web surfaces after the parser exists)

```bash
Task: "T056 akml-ui.js isAtBottom / scrollToBottom"
Task: "T057 ChatMessageView.razor"
# then, sequentially on AiChatPanel.razor:
Task: "T058 render through ChatMessageView, starters, follow-scroll"
Task: "T059 Editor.razor wiring"
```

---

## Implementation Strategy

### MVP first (User Story 1)

1. Phase 1 (baseline), then Phase 3 (US1) — the defect the owner reported, nine tasks plus the
   owner's S1 run.
2. **Stop and validate**: S1-1…S1-7 recorded; "Ready to commit?".

### Incremental delivery (what ships value soonest)

1. Phase 2 → US2 (the Copilot look in SSMS) → S2 run → commit.
2. US3 (hints) and US5 (New chat) are small; US4 (selector) next.
3. US6 closes the web parity.
4. Phase 9 last: docs, full gates, success criteria.

### Parallel team strategy

With two people: one on the SSMS line (US1 → US2 → US3 → US4 → US5 SSMS halves), one on Phase 2
then the web halves (US3 web, US4 web, US5 web, US6), meeting at Phase 9.

---

## Notes

- Every string in a test is the contract's text; change the contract first if a string must change.
- The chat must never answer "as if the database were empty" — a refusal sends nothing.
- Do not add a keyboard shortcut for New chat (none is bound; `ShortcutCollisionTests` untouched).
- Ask "Ready to commit?" at each checkpoint; never commit on your own.
