# Data model — spec 046 AI Chat

Phase 1 output. Types are described by their fields and rules; names are the ones the contracts
use. Nothing here is persisted in a new store.

## ChatBinding (SSMS, in memory)

The query window the chat answers about. Produced by `ChatBindingResolver`, held by `AiChatPanel`
as the single `_binding`, replaced on every refresh.

| Field | Type | Meaning |
|-------|------|---------|
| `State` | `ChatBindingState` | `Bound`, `NotConnected`, `NotQueryWindow`, `NoDocument` |
| `SessionId` | string? | the editor session (`AkmlSqlSessionId`) — set only when `State == Bound` or `NotConnected` |
| `DocumentName` | string? | file name of the window, e.g. `SQLQuery1.sql` (never the full path in UI or log) |
| `DocumentPath` | string? | full path, used to match tracked views and to target Insert into query |
| `Server` | string? | as the caption shows it; `Bound` only |
| `Database` | string? | `Bound` only |
| `IsSticky` | bool | true when the binding was kept from a previous refresh because the active window is not a query window |

**Rules**
- `Bound` ⇔ a tracked SQL view matched the active document (or the sticky one) **and** the caption
  parsed to a server and database.
- `NotConnected` ⇔ a tracked view matched but no server/database could be read from its caption.
- `NotQueryWindow` ⇔ the active document is not a tracked SQL view **and** no sticky binding
  survives.
- `NoDocument` ⇔ no active document **and** no sticky binding survives.
- A sticky binding survives while its view is open (`!view.IsClosed`) and still parses as
  connected; otherwise it is dropped and the state is computed from scratch.
- A fabricated or unknown session id is never produced (spec 036 R1).

**Derived display**
- Header text: `Bound` → `{Server}.{Database}`; otherwise `Not connected`.
- Header tooltip: `Bound` → `{DocumentName} — {Server}.{Database}`; `NotConnected` →
  `{DocumentName} is not connected`; else none.
- Binding key for change detection: `{Server}.{Database}` ordinal-ignore-case.

**State transitions** (per refresh; `prev` = previous binding)

```
active doc is a tracked SQL view ─┬─ caption parses → Bound (IsSticky=false)
                                  └─ caption empty  → NotConnected
active doc not tracked / none ────┬─ prev is Bound and prev.view open and still connected → prev (IsSticky=true)
                                  ├─ active doc exists → NotQueryWindow
                                  └─ no active doc    → NoDocument
```

A `Bound → Bound` transition with a different binding key raises the rebind notice (FR-007).

## ChatBlock (Core, pure)

Output of `ChatMarkdown.Parse(string)`: an ordered list.

| Kind | Fields | Notes |
|------|--------|-------|
| `Paragraph` | `Inlines` | one or more inlines |
| `Heading` | `Level` (1–3), `Inlines` | `#`, `##`, `###` |
| `ListBlock` | `Ordered` (bool), `Items` (list of inline lists) | `-`/`*` bullets or `1.` numbers; one level |
| `CodeBlock` | `Language` (string, may be empty), `Code` (string), `Index` (1-based among code blocks), `Unclosed` (bool) | fence content without the fences; trailing newline trimmed |

Inlines: `Text(value)`, `Bold(value)`, `Code(value)`.

**Rules**
- An unclosed fence runs to the end of the text and is marked `Unclosed`; nothing is dropped.
- A fence's language is the token after the opening backticks, lower-cased, trimmed.
- Inline markers that do not close are literal text.
- `Parse("")` returns an empty list; whitespace-only input returns an empty list.
- The raw text is kept by the caller (copy message, copy conversation, export use the raw text).

**Block actions derived from a `CodeBlock`**
- Every code block: Copy.
- Insert (into query / at caret) when `Language` is empty, `sql` or `tsql`.
- Labels when a message has more than one code block: `Copy SQL block {Index} of {Count}` /
  `Insert SQL block {Index} of {Count}`; single block: `Copy` / `Insert into query` (SSMS),
  `Insert at caret` (web).

## Conversation turn (SSMS, in memory)

Unchanged `ChatTurnDto { Role, Content }` list `_history` plus the parallel
`_historyAgentNames`. New: a parallel `_historyAgentLabels` is **not** added — the attribution
label is rendered from the reply at the time and kept on the bubble only; copy conversation keeps
using the agent name as today.

## Conversation turn (web, IndexedDB `chatHistory`)

`ChatTurn` gains one additive field.

| Field | Type | Meaning |
|-------|------|---------|
| `AgentLabel` | string? | `{Name} · {Provider} · {model}` of the agent the answer was requested from, set when the assistant turn is created; null on user turns and on turns stored before this feature |

Rendering: assistant role label = `AgentLabel ?? "Assistant"`. Export (`ToMarkdown`) uses the
label when present.

## AiChatResponse (IPC 175, append-only)

| Key | Field | Type | Meaning |
|-----|-------|------|---------|
| 6 | `AgentName` | string? | existing |
| 7 | `AgentProvider` | string? | provider id of the agent that answered (`gemini`, `anthropic`, …); the offline provider's id when it answered |
| 8 | `AgentModel` | string? | model of the agent that answered; null when unknown (offline provider, older engine) |

Compatibility: an older shell ignores 7–8; an older engine leaves them null and the shell shows
the name only. `AiChatRequest` (75) is unchanged.

## Agent label (both editions)

`{Name} · {ProviderDisplayName} · {Model}` — parts that are null or blank are omitted together
with their separator; `ProviderDisplayName = AiProviderIds.DisplayName(providerId)`. Used by: the
SSMS picker entries, the attribution caption, the FR-034 notice, the web selector options, the web
role label, `ChatTurn.AgentLabel`.

## Composer state (both editions)

| Input | Send enabled | Send tooltip | New chat enabled | New chat tooltip |
|-------|--------------|--------------|------------------|------------------|
| no usable agent | no | `No AI agent can answer yet — add one in Options` (web: `… in Settings`) | no | `No conversation yet` |
| composer empty | no | `Type a question first` | depends on turns | — |
| answer in flight | no | `Wait for the answer to finish` | SSMS: no — `Wait for the answer to finish`; web: yes (stops the stream) | — |
| text present, idle | yes | `Send (Ctrl+Enter)` | yes when turns exist | `Start a new conversation — clears this one` |
| conversation empty, idle | — | — | no | `The conversation is already empty` |

Placeholder (both): `Ask about this database…   (Enter or Ctrl+Enter sends · Shift+Enter for a new line)`
— web wording: `Ask anything about your SQL…   (Enter or Ctrl+Enter sends · Shift+Enter for a new line)`.

## Conversation notices (SSMS; web renders the same text as a non-persisted line)

| Trigger | Text |
|---------|------|
| binding key changed (`Bound → Bound`, different `{server}.{database}`) | `Now answering about {Server}.{Database} ({DocumentName}).` |
| resolved chat agent changed by the user (FR-034) | `Now answering with {AgentLabel}.` |
| cleared assignment (existing FR-049) | unchanged |

Notices are not part of history: not sent, not copied, not exported.
