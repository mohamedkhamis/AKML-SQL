# Research — spec 046 AI Chat (Copilot-style, SSMS + web)

Phase 0 output. Every unknown in the plan's Technical Context is resolved here; each entry records
the decision, why, and what was rejected. File anchors are as of 2026-11-04 on `master`.

## R1 — How the SSMS chat decides which database it answers about

**Finding (the reported defect)**: `AiChatPanel.SendMessageAsync` calls
`RefactorCommandHelper.TryGetActiveRealSessionId()` (`Refactoring/RefactorCommandHelper.cs:111`),
which calls `IVsTextManager.GetActiveView(fMustHaveFocus: 1, …)` and requires the returned view's
buffer to carry `AkmlSqlSessionId`. `TextViewCreationListener` tags only `SQL` / `T-SQL` /
`SQL Server Tools` document views. SSMS's results **Messages** pane is its own text view inside the
same query window and is never tagged; after the user clicks into it (to copy an error) it is the
"active view", so the send path refuses with `NoConnectionMessage` — while the header
(`RefreshBinding` → `SsmsConnectionDetector.TryDetectConnection(provider)`, caption of
`DTE.ActiveDocument.ActiveWindow`) still shows the database, and `AiEditorSqlInserter`
(`DTE.ActiveDocument` → `TextDocument.Selection.Insert`) keeps working. The log of 2026-11-04
shows no `AiChat` request after 17:03:53 while the caption detector reported the connected window
every 2 s; the refusal path logs nothing.

**Decision**: a new `ChatBindingResolver` (shell, `Ai/`) is the single source for the panel:

1. Read `DTE.ActiveDocument?.FullName` (UI thread). SSMS keeps `ActiveDocument` on the last
   document even while a tool window (the chat, Object Explorer) has the keyboard, so the
   document, not the pane, is what "active" means — the user's own definition.
2. Match the path against the extension's tracked editors: new
   `ConnectionWiringHelper.TryFindTrackedView(filePath, out sessionId, out view)` iterates
   `OpenViews` (`Editor/ConnectionWiringHelper.cs:261`) and compares each view's
   `ITextDocument.FilePath` ordinal-ignore-case (the same path identity
   `SsmsConnectionDetector.TryDetectConnection(sp, textView)` already relies on, lines 140–215).
3. Connection for that view: `SsmsConnectionDetector.TryDetectConnection(sp, view)` → server and
   database, or null → **NotConnected**.
4. If the active document is not a tracked SQL view (another document type) or there is no
   document: keep the **last** binding if its view is still open and still connected (sticky,
   FR-003); otherwise **NotQueryWindow** / **NoDocument**.
5. The result (`ChatBinding`, see data-model) is stored once per refresh and read by the header,
   the schema poll, Send and Insert into query (FR-002, FR-008). `RefreshBinding()` keeps its
   2-second tick and runs again at send time.

The pure decision (`Decide(active, findTracked, last, isOpen)`) is a static function with no VS
types so it is unit-tested off-host; the UI-thread `Resolve()` only gathers inputs.

**Rationale**: the header already proves the document route is right for the user's mental model;
the tracked-view table already exists; no SSMS-internal interop. Sticky fallback gives FR-003
without a focus tracker.

**Alternatives considered**: `GetActiveView(0, …)` — still a *text view* (the Messages pane
qualifies), not the document tab. `GotAggregateFocus` tracking on tagged views — misses a tab
activation that lands focus in the results grid, so the header (document) and the tracker would
disagree again. `IVsMonitorSelection` document frame + `VsShellUtilities.GetTextView(frame)` —
depends on SSMS's editor pane implementing `IVsCodeWindow`, unverified; the DTE path is already
exercised by the connection detector. Changing `TryGetActiveRealSessionId` for everyone — the
editor-context AI commands are invoked from the editor and must keep the focused-view rule.

## R2 — Diagnosability of a refusal (FR-005)

**Decision**: every refusal writes one `Log.Information` line:
`AiChatPanel: refused to send — {Reason}; document={Document}; queryWindow={IsQueryWindow}; session={HasSession}; connected={Connected}`
(reason = the `ChatBindingState` name; document = file name only, no path; no server, user or
SQL text). Information level so it survives a user's `logMinimumLevel: Information`.

**Rationale**: the 2026-11-04 report could not be proven from the log. **Rejected**: Debug level
(hidden on tightened configs); logging the question text (not needed, could carry data).

## R3 — Telling the user when the binding changed (FR-007)

**Decision**: `RefreshBinding` compares the new binding's `{server}.{database}` (ordinal-ignore-
case) with the previous *bound* one; on a change it calls the existing `AddConversationNotice`
(`AiChatPanel.cs`, FR-049 idiom) with `Now answering about {server}.{database} ({document}).`
No note on the first binding of an empty conversation (the welcome card names it) and none for a
rebind to the same database from another window. Notices are not history (not sent, not copied).

**Rejected**: clearing the conversation on rebind (the user may want to carry a question over).

## R4 — Attribution with provider and model (FR-022, FR-030)

**Finding**: `AiChatResponse` carries `AgentName` (Key 6) only; the engine's
`ExecuteWithFallbackAsync` returns `(response, usedFallback, agentName)`
(`Engine/Ai/AiPipelineServices.cs`), and `AiChatHandler.cs:76–100` builds the reply.

**Decision**: two append-only fields — `AgentProvider` (Key 7, provider id such as `gemini`) and
`AgentModel` (Key 8). The engine fills them from the agent that actually answered (resolved or
fallback-chain agent); for the offline provider it fills the provider id and leaves the model null.
The shell formats `Name · {AiProviderIds.DisplayName(provider)} · {model}`, dropping any null part;
an older engine (nulls) shows the name only, as today. `doc/ipc-api.md` gains the two rows.

**Rationale**: exact for fallback answers (the shell cannot look a fallback agent up by name
reliably — names are not unique); nulls degrade to today's behaviour. **Rejected**: `AgentId`
only (needs a config lookup in the shell and fails for the offline provider); formatting the label
engine-side (the shell owns display names).

## R5 — Rendering answers: a shared block parser, two renderers

**Decision**: `AkmlSql.Core/Text/ChatMarkdown.cs` — `IReadOnlyList<ChatBlock> Parse(string)`
over this subset: fenced code (```` ``` ```` with optional language; an unclosed fence runs to the
end), headings `#`–`###`, bullet lists (`-`, `*`), numbered lists (`1.`), paragraphs; inlines
`**bold**` and `` `code` ``. Everything else is literal text. Blank lines separate blocks.
Netstandard2.0-compatible, no regex beyond simple prefixes. Tested in Core.

SSMS renders through a new `ChatMessageRenderer` (R6); the web through `ChatMessageView.razor`.
The engine's `ExtractCodeActions` (```` ```sql ```` only) is left as is for the IPC; the shell and
web derive per-block actions from the parser instead (every fenced block gets Copy; blocks whose
language is `sql`, `tsql` or empty also get Insert).

**Rationale**: one behaviour in both editions; the subset is what the chat system prompt
produces; the shell cannot take a new assembly cheaply (constitution V, VSIX assembly resolution).
**Rejected**: Markdig (present only in the SSR site; different output per edition; package in the
VSIX); rendering raw text with monospaced fences (today — the complaint).

## R6 — WPF rendering that stays selectable and themed

**Decision**: `ChatMessageRenderer.Build(blocks, …)` returns a `StackPanel`: prose blocks become
one read-only, borderless, transparent `RichTextBox` holding a `FlowDocument` (Paragraph /
List / headings as bold larger paragraphs; `Run` for text, `Bold`, and inline code as a `Run` in
`ThemeTokens.TypographyEditor` with `SurfaceInputReadOnly` background) — selectable with the
pointer, Ctrl+C copies the selection (spec 036 FR-017 kept). Each code block is a `Border`
(`SurfaceCanvas` background, `BorderDefault`, 4 px radius) with a header row (language label,
`⧉ Copy`, `⇩ Insert into query`) and a read-only `TextBox` in the editor font (selectable).
All brushes via `SetResourceReference` (theme follows). `FlashButtonContent` shows "✓ Copied" /
"✓ Inserted" / "⚠ Copy failed". The per-message copy button copies the raw text (FR-026).

**Rejected**: `TextBlock` with inlines (not selectable); `WebBrowser` control (no theme, heavy).

## R7 — Welcome and starter questions (FR-023)

**Decision**: `AiChatWelcomeCard` replaces the greeting bubble while an agent can answer and the
conversation is empty: title `Ask about {server}.{database}` or `Not connected to a database`,
one sentence on what the chat does, and three starter buttons (`What are the largest tables in
this database?`, `Which tables have no primary key?`, `Which stored procedures were modified most
recently?`). A click puts the text in the composer and sends. The no-agent onboarding card
(`AiChatEmptyState`) is unchanged. The web `akml-chat-empty` block gains the same three buttons.

**Rejected**: AI-generated starters (a request before the user asked anything).

## R8 — Follow-scroll (FR-024)

**Decision**: SSMS — before appending, `wasAtBottom = VerticalOffset >= ScrollableHeight - 8`;
`ScrollToEnd()` only when it was. Web — `akml-ui.js` gains `isAtBottom(el, threshold = 8)` and
`scrollToBottom(el)`; `AiChatPanel.razor` checks before `StateHasChanged` on a new turn and
scrolls after render. Node test for the helper.

## R9 — The agent selector (FR-030 – FR-033)

**Decision**: SSMS — `AiAgentPicker` keeps its `ComboBox` of plain strings (the
`ComboBoxTheming` contract) but each entry reads `{Name} · {Provider} · {model}` via
`AiProviderIds.DisplayName`; the trailing `Add agent…` entry stays (it opens the agent settings,
FR-032; spec 037 FR-041 tests keep passing). The picker moves from the header to a **composer
bar** under the input (picker left, Send right) so the current agent is always visible beside
where the user types (FR-030). Selection persistence is unchanged (`OnChatAgentSelected` writes
`FeatureAgents.Chat`). On a change of the *resolved* agent the panel adds the FR-034 notice
`Now answering with {Name} · {Provider} · {model}.` through `AddConversationNotice`.

Web — new `AiAgentSelector.razor`: `IAiKeyVault.ListAsync()` filtered to usable agents
(`Enabled` and (`HasKey` or `!AiAgentResolver.RequiresApiKey(provider)`)), a `<select
aria-label="Answering agent">` with the same label format and a final `Manage agents…` option
that navigates to `settings/ai-assistance`; a change calls `IAiPreference.SetActiveAsync` (the one
active agent all web AI actions share — Assumptions) and the chat re-reads `_hasProvider` and the
label. The same FR-034 note is added as a system turn-like line (not persisted).

**Rejected**: a chat-only agent in the web (new preference, out of scope); a two-line templated
ComboBox item in WPF (breaks `ComboBoxTheming`'s plain-string contract).

## R10 — Attribution in the web edition

**Decision**: `ChatTurn` gains `AgentLabel` (string?, additive) written when the answer starts:
the selector's label of the active agent. The role label renders `AgentLabel ?? "Assistant"`.
Older stored turns have no label and render as before.

**Rejected**: resolving the label at render from the vault (breaks when the agent is renamed or
deleted; the turn should say who answered at the time).

## R11 — Theme and tokens

**Decision**: no new token. WPF: code panel `SurfaceCanvas` + `BorderDefault`; inline code
`SurfaceInputReadOnly`; notices `TextSecondary`; welcome card `SurfaceElevated`. Web: the
generated `--akml-surface-canvas`, `--akml-border-default`, `--akml-surface-inputreadonly`,
`--akml-chat-*` variables. Constitution II stays green without touching `theme-tokens.json`.

## R12 — Tests and what existing tests must change

**Decision**: new — `ChatMarkdownTests` (Core), `ChatBindingResolverTests` (pure `Decide`),
`AiChatRenderingTests` (STA), `AiChatResponseAgentFieldsTests` (engine round trip),
`ChatMessageViewTests`, `AiAgentSelectorTests` (bUnit), `akml-ui.scroll.test.mjs` (node).
Extended — `AiChatSessionBindingTests` (refusal texts and the log line through a Serilog test
sink; header/send agreement), `AiChatComposerStateTests` (Send tooltip states, placeholder text,
New chat enablement), `AiChatAgentPickerTests` (label format, FR-034 notice),
`AiChatPanelCopyButtonTests` and web `AiChatPanelCopyTests` (bubble structure), web
`AiChatPanelComposerTests` (tooltip, New chat), E2E `AiChatComposerKeysTests` (tooltip text,
rendered code panel, Insert at caret). The SSMS Messages-pane scenario is manual (quickstart).

## R13 — Documentation touched

`doc/ipc-api.md` (AiChatResult keys 7–8), `doc/use-cases.md` (chat section: binding rule,
Ctrl+Enter, selector, New chat), `doc/progress.md` (spec 046 entry with deferred items),
`CLAUDE.md` (one bullet under latest work). The served docs ingest `doc/` automatically.

## R14 — Ctrl+Enter in a VS tool window

**Finding**: both composers already send on Ctrl+Enter — SSMS `OnInputKeyDown` only excludes
Shift (`AiChatPanel.cs:915`), the web `OnKeyDown` tests `Enter && !ShiftKey`. What is missing is
the **hint**. Risk: SSMS's keyboard preprocessing could claim Ctrl+Enter before the WPF `TextBox`
(VS binds it to `Edit.LineOpenAbove` in the text-editor scope only).

**Decision**: keep the key handling, add the tooltip and placeholder; quickstart §SSMS verifies
Ctrl+Enter reaches the composer in SSMS 22; if it does not, the fix is a `PreviewKeyDown` handler
on the panel root (recorded as a fallback, not implemented up front).
