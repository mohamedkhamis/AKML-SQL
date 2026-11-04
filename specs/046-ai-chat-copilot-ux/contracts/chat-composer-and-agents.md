# Contract: composer keys and hints, the agent selector, New chat, and the reply's agent fields

Satisfies **FR-010 – FR-014** (US3), **FR-030 – FR-034** (US4), **FR-040 – FR-043** (US5), the web
half of **US6**. Research R4, R9, R10, R14.

**Anchors**: `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` (`OnInputKeyDown`, `OnSendClick`,
`RenderAgentState`, `OnChatAgentSelected`, `AddConversationNotice`) · `src/AkmlSql.Shell.Shared/Ai/AiAgentPicker.cs` ·
`src/AkmlSql.Core/Config/AiProviderIds.cs:37` (`DisplayName`) · `src/AkmlSql.Core/Ipc/Messages/AiChatResponse.cs` ·
`src/AkmlSql.Engine/Handlers/Ai/AiChatHandler.cs:76` · `src/AkmlSql.Web/Shared/AiChatPanel.razor` ·
`src/AkmlSql.Web/Services/IAiPreference.cs` · `src/AkmlSql.Web/Services/IAiKeyVault.cs:84` (`AiProviderConfig`)

---

## Part 1 — Keys (both editions)

| Key | Composer action |
|-----|-----------------|
| Enter | send (unchanged) |
| Ctrl+Enter | send (already true in both editions — only the hint is new) |
| Shift+Enter | new line (unchanged) |
| empty or whitespace composer | Enter / Ctrl+Enter do nothing |

SSMS `OnInputKeyDown` is unchanged in behaviour. Web `OnKeyDown` is unchanged. Quickstart §SSMS
verifies Ctrl+Enter reaches the composer in SSMS 22 (R14 fallback: `PreviewKeyDown` on the root).

## Part 2 — Tooltips and placeholder

Send (`ToolTip` in WPF, `title` in HTML), recomputed on text change, send state and agent state:

| State | Text |
|-------|------|
| sendable | `Send (Ctrl+Enter)` |
| empty composer | `Type a question first` |
| answer in flight | `Wait for the answer to finish` |
| no usable agent | SSMS `No AI agent can answer yet — add one in Options`; web `No AI agent can answer yet — add one in Settings` |

Placeholder: SSMS `Ask about this database…   (Enter or Ctrl+Enter sends · Shift+Enter for a new line)`;
web `Ask anything about your SQL…   (Enter or Ctrl+Enter sends · Shift+Enter for a new line)`.

Every control has a tooltip and an accessible name (FR-013):

| Control | Tooltip |
|---------|---------|
| New chat | see Part 5 |
| Agent selector | `Choose which agent answers your messages` |
| Copy conversation (SSMS) | `Copy the whole conversation to the clipboard` (existing) |
| Export (web) | `Download the conversation as Markdown` |
| Per-message copy | `Copy message` (existing) |
| Code block Copy | `Copy this SQL` |
| Code block Insert | SSMS `Insert this SQL into the query window at the caret`; web `Insert this SQL at the editor caret` |
| Starter question | `Send this question` |

While an answer is in flight the working state reads `Asking {AgentLabel}…` (SSMS replaces
`Thinking...`; web replaces `Thinking…`).

## Part 3 — Agent label

`AgentLabel(name, providerId, model)` → `{name} · {AiProviderIds.DisplayName(providerId)} · {model}`,
omitting blank parts with their separators. Lives in Core beside `AiProviderIds` (static helper
`AiAgentLabel.Format`) so both editions format identically. Examples:
`Gemini Flash · Google Gemini · gemini-2.5-flash`; `Local · Ollama · llama3`; name only when the
rest is unknown.

## Part 4 — Attribution and the reply's new fields (FR-022, R4)

`AiChatResponse` (175) gains `AgentProvider` (Key 7) and `AgentModel` (Key 8). The engine sets
them from the agent that answered: `ExecuteWithFallbackAsync` returns the answering `AiAgent?` in
addition to its name (null for the offline provider, where only the provider id is set).

Shell caption: `AttributionText(label, selectedLabel)` keeps today's rule with labels in place
of names — same agent → `{label}`; different → `{label} answered — {selectedLabel} was unavailable.`;
null name → no caption. `doc/ipc-api.md` › `AiChat (75) → AiChatResult (175)` gains two rows in
the Key 6 style.

Web: the assistant turn's `AgentLabel` is set when the turn is created; the role label shows it.

## Part 5 — New chat (FR-040 – FR-043)

| | SSMS | Web |
|---|---|---|
| Control | header button `＋ New chat` (left of Copy conversation) | header button `New chat` replacing `Clear`, shown whenever a provider is configured |
| Enabled | turns exist and not sending | turns exist (also while streaming) |
| Tooltip | `Start a new conversation — clears this one`; disabled while sending → `Wait for the answer to finish`; disabled when empty → `The conversation is already empty` | same, minus the sending case |
| Effect | clears `_conversationPanel`, `_history`, `_historyAgentNames`; shows the welcome card; keeps `_binding`, the picker, the privacy and schema notes; focuses the composer | cancels the stream (`_cts.Cancel()`), `History.ClearAsync()`, new conversation, empty state, focuses the composer via `focusElement` |
| Context | the next request's `History` is empty | the next request's history snapshot is empty |

No keyboard shortcut is bound; the control is reachable by Tab. Accessible name `New chat`.

## Part 6 — The agent selector

**SSMS** (`AiAgentPicker`, moved into the composer bar):

```
┌──────────────────────────────────────────────────────────────┐
│  [ Ask about this database…                                ] │
│  [ Gemini Flash · Google Gemini · gemini-2.5-flash  ▾ ]  [Send] │
└──────────────────────────────────────────────────────────────┘
```

- `SetAgents` lists usable agents (spec 037 S1) as labels (Part 3) and the trailing `Add agent…`
  entry (opens Options › AI Assistance, FR-032); selection = the resolved chat agent.
- `OnChatAgentSelected` is unchanged (persists `FeatureAgents.Chat`); after the re-render, when
  the resolved agent id differs from the one before the pick, the panel adds
  `AddConversationNotice($"Now answering with {AgentLabel}.")` (FR-034). Picking the already
  selected agent adds nothing. An in-flight answer keeps its agent (unchanged).
- The header now holds: title, binding label, New chat, Copy conversation.

**Web** (`AiAgentSelector.razor`, in the chat composer footer):

- Source `IAiKeyVault.ListAsync()`; usable = `Enabled && (HasKey || !AiAgentResolver.RequiresApiKey(ProviderId))`.
- `<select aria-label="Answering agent" data-testid="chat-agent-select">` of labels (Part 3),
  selected = `IAiPreference.GetActiveAsync()`, final option `Manage agents…` (navigates to
  `settings/ai-assistance` without changing the selection).
- Change → `IAiPreference.SetActiveAsync(id)` (the shared active agent — all web AI actions
  follow, per spec Assumptions) → the panel re-reads `_hasProvider` and adds the FR-034 line as a
  non-persisted notice.
- When no usable agent exists the selector shows only `Manage agents…` and the existing
  "No AI provider configured" state stays.

## Part 7 — Acceptance mapping

| Spec scenario | Covered by |
|---|---|
| US3-1/2 keys | existing key tests (shell, web bUnit, E2E) — unchanged behaviour |
| US3-3/4/5 tooltips, placeholder | `AiChatComposerStateTests`, web `AiChatPanelComposerTests`, E2E tooltip text |
| US4-1/2 label format | `AiChatAgentPickerTests`, `AiAgentSelectorTests` |
| US4-3 attribution | `AiChatResponseAgentFieldsTests` (engine), `AttributionText` unit cases |
| US4-4 persistence | existing `FeatureAgents.Chat` tests; web preference fake |
| US4-5 web selector | `AiAgentSelectorTests` |
| US4-6 switch note | `AiChatAgentPickerTests` (notice present once) |
| US5-1..5 New chat | `AiChatComposerStateTests`, web `AiChatPanelComposerTests`, E2E |
