# Contract: the chat panel's empty state, picker and attribution

Satisfies **FR-015 – FR-023** and **FR-037 – FR-045**. Shell-side, WPF, programmatic (no XAML).

**Anchors**: `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs:40,76,238,243,255,353,423,666` · `src/AkmlSql.Shell.Shared/Commands/AiCommandVisibility.cs:17` · `src/AkmlSql.Shell.Shared/Commands/OptionsCommand.cs:44`

---

## Part 1 — The empty state

**The rule**: the panel must never invite a question it cannot answer.

### What is wrong today

`AiChatPanel`'s constructor calls
`AddAssistantMessage("Hello! I'm your AI SQL assistant. …")` unconditionally
(`AiChatPanel.cs:238`), before any configuration is read. An unconfigured user is greeted warmly and
then gets a raw provider error on their first question. That call is what the empty state replaces.

### Required

```
render():
    if no usable agent (S1 over settings.Ai.Agents):
        show the onboarding card
        _inputBox.IsEnabled  := false
        _sendButton.IsEnabled := false
    else:
        show the greeting, naming the resolved chat agent
        _inputBox.IsEnabled  := true
        _sendButton.IsEnabled := true
```

### The card

```
┌────────────────────────────────────────────────────────────┐
│  No AI agent is set up                                     │
│                                                            │
│  AKML SQL needs an AI agent before it can answer questions │
│  about your database.                                      │
│                                                            │
│              [  Add AI agent  ]                            │
└────────────────────────────────────────────────────────────┘
```

One primary action (FR-016). Not two buttons, not a link, not a settings gear — the user asked for
simple.

### Wording when agents exist but none is usable (FR-021)

The card names the specific reason and the offending agent, and its button opens Options **on that
agent** rather than on a blank one:

| Condition | Card text |
|---|---|
| No agents at all | "No AI agent is set up." |
| Agent needs a key | "*Claude (work)* needs an API key before it can answer." |
| Agent needs a model | "*Claude (work)* has no model selected." |
| Agent needs an endpoint | "*Azure* needs an endpoint URL." |
| Key will not decrypt | "*Claude (work)*'s stored API key could not be read on this machine — re-enter it." |
| All agents disabled | "Every AI agent is turned off." |

When several agents are unusable, the card names the first in list order and its button opens that
one.

### The route into Options (FR-017)

```
OnAddAgentClick:
    saved := OptionsCommand.ShowOptions(pageKey: "AI Assistance", agentId: <offending id or null>)
    RefreshConfiguration()        // immediate — do not wait for the poll
```

`ShowOptions` is the shared entry point that performs `ConfigManager.Save` **and** the
`AnalysisSettingsChanged` notification to the engine (`OptionsCommand.cs:69`). The panel must not
save settings itself; skipping that notification leaves the engine serving stale settings and
FR-019's "no restart" silently fails.

Cancelling leaves the card in place (FR-020) — `RefreshConfiguration` simply finds the same state.

### Other AI surfaces (FR-022)

Explain, Fix, Optimize, Index Analysis and Text-to-SQL must report the same state rather than a
provider error. They already share `AiCommandVisibility`, which hides them when
`settings.Ai.Enabled` is false — and `Enabled` is now exactly "at least one usable agent" (V19). For
the surfaces that are reachable anyway, the message is the card's text plus the same
`ShowOptions` route.

**Ghost text is excluded by FR-022 and must stay silent** — no error, no prompt, no route into
Options. It is the one AI feature the user does not invoke: it fires on typing, so a user who has
configured nothing is not asking for a completion and must not be interrupted while editing.

---

## Part 2 — Noticing that configuration changed

**The rule**: reuse the two polling mechanisms that already exist. Add no third.

```
_bindingTimer tick (every 2 s, already running — AiChatPanel.cs:243):
    RefreshBinding()             // existing: editor session, schema status, privacy note
    RefreshConfiguration()       // NEW: reads AI config through a 5-second cache
```

The 5-second cached read is the idiom `AiCommandVisibility` already uses for this exact problem
(`AiCommandVisibility.cs:17`, `CacheDurationMs = 5000`). No `FileSystemWatcher`, no new timer, no
cross-component event.

`RefreshConfiguration` also runs immediately on `Loaded` and immediately after `ShowOptions`
returns, which is the path FR-019 measures — that transition is instant. A user who changes agents
from the Tools menu while the panel is open waits up to 5 seconds, which is acceptable and
documented.

**It must be cheap when nothing changed.** Compare a signature (agent count, active id, chat
assignment, usability) and return without touching the visual tree when it is unchanged — this runs
every 2 seconds inside the host process.

---

## Part 3 — The picker

**The rule**: the picker changes the agent **for chat**, and nothing else.

### What it writes

```
OnAgentSelected(agent):
    if agent.Id == settings.Ai.ActiveAgentId:
        settings.Ai.FeatureAgents.Chat := ""        // follow the active agent
    else:
        settings.Ai.FeatureAgents.Chat := agent.Id
    persist via the shared save+notify path
```

Not `ActiveAgentId` — see research R7. This is GitHub Copilot's behaviour, and it is what keeps US4
usable: a user who assigned ghost text to a local model must not have that undone by trying a
different chat model.

### What it shows

The **resolved** chat agent (S3): `FeatureAgents.Chat` when set and usable, otherwise the active
agent. A user who never touches the picker still sees the correct name (FR-037).

### Obligations

| Requirement | Obligation |
|---|---|
| FR-038 | Selectable from inside the panel — no dialog, no menu round-trip |
| FR-039 | Takes effect from the next message; the conversation is **not** cleared; no restart |
| FR-040 | Persisted, so it survives panel close/reopen and host restart |
| FR-041 | Last entry is **Add agent…**, which calls `ShowOptions("AI Assistance", null)`; on save the new agent becomes the selection |
| FR-044 | Shown even with exactly one agent — the user must always know who is answering |
| Disabled agents | Not listed |
| Mid-flight switch | Allowed; applies from the next message. The in-flight answer keeps the agent that produced it |

Placement: the header row, beside the database context label and the existing **⧉ Conversation**
button (`AiChatPanel.cs:76-118`). The header is already the panel's "who and what" strip.

---

## Part 4 — Attribution

**The rule**: in a mixed conversation, every answer says who produced it.

### In the transcript (FR-042)

Each assistant bubble carries the agent name from `AiChatResponse.AgentName` (key 6). When the field
is `null` — an older engine — no attribution is rendered rather than a guessed one.

When the engine reports a fallback was used, the panel says so plainly:

> *Kimi* answered — *Claude (work)* was unavailable.

### In the copied conversation (FR-043)

`OnCopyConversationClick` (`AiChatPanel.cs:666`) currently writes `"You:"` / `"Assistant:"` from
`_history`. `_history` holds `ChatTurnDto { Role, Content }` and has no room for a name, so the
panel keeps a parallel per-turn agent name list (or a small local turn record) and writes:

```
You:
what tables do I have?

Claude (work):
Your database has 12 tables…

You:
same question, different model

Kimi:
The database contains…
```

The existing behaviour is otherwise preserved: order kept, one blank line between turns, trailing
whitespace trimmed, the clipboard funnel and its `✓ Copied` / `⚠ Copy failed` flash unchanged.

---

## Part 5 — What must not change (FR-045)

| Behaviour | Anchor |
|---|---|
| Binding to the active editor's real session, re-resolved at send time | `AiChatPanel.cs:441` |
| Refusal to send when unbound, with `NoConnectionMessage` | `:31`, `:446` |
| Header showing `server.database`, `"Not connected"` when unbound | `:38`, `:398` |
| Schema-loading notice from `SchemaStatusRequest` (80) | `:311` |
| Privacy-mode consequence note | `:353` |
| Copy-SQL code actions on answers | `:517` |
| Copy-whole-conversation button | `:76`, `:666` |
| The 2-second binding poll and its start/stop on Loaded/Unloaded | `:243` |

The empty state gates **sending**, not binding. A user with no agent still sees their database
context — the panel just cannot answer yet.

---

## Test coverage

| Test | Location | Asserts |
|---|---|---|
| No agents ⇒ card shown, greeting absent | `AiChatEmptyStateTests.cs` | FR-015 |
| No agents ⇒ input and send disabled | same | FR-018 |
| Card offers exactly one primary action | same | FR-016 |
| Unusable agent ⇒ reason-specific wording | same | FR-021, all six conditions |
| Card names the offending agent | same | FR-021 |
| Adding a usable agent ⇒ card replaced, input enabled | same | FR-019, no restart |
| Cancelling Options ⇒ card remains | same | FR-020 |
| Refresh is a no-op when the signature is unchanged | same | The 2-second-tick cost bound |
| Picker lists enabled agents only | `AiChatAgentPickerTests.cs` | Part 3 |
| Picker shows the resolved chat agent | same | FR-037, S3 |
| Selecting writes `FeatureAgents.Chat`, not `ActiveAgentId` | same | R7 — the decision most likely to be implemented wrong |
| Selecting the active agent clears the assignment | same | S3 |
| Selection survives reopen | same | FR-040 |
| Conversation is not cleared on switch | same | FR-039 |
| Picker shown with one agent | same | FR-044 |
| **Add agent…** routes through `ShowOptions` | same | FR-041 |
| Answers carry their agent name | same | FR-042 |
| `AgentName == null` renders no attribution | same | Older-engine tolerance |
| Fallback is stated, not hidden | same | FR-052 |
| Copied conversation attributes each answer | same | FR-043 |
| Existing chat contracts still hold | `AiChatSessionBindingTests.cs`, `AiChatPanelCopyButtonTests.cs` | FR-045 — these must stay green unmodified |
