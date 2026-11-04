# Contract: the SSMS chat's binding to a query window

Satisfies **FR-001 – FR-008** (User Story 1). Shell-side; research R1–R3.

**Anchors**: `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` (`RefreshBinding`, `SendMessageAsync`,
`SetDatabaseContext`, `PollSchemaStatusAsync`) · `src/AkmlSql.Shell.Shared/Editor/ConnectionWiringHelper.cs:261`
(`OpenViews`, `Track`) · `src/AkmlSql.Shell.Shared/Editor/SsmsConnectionDetector.cs:109` (per-view
detection) · `src/AkmlSql.Shell.Shared/Ai/AiEditorSqlInserter.cs`

---

## Part 1 — The resolver

```
internal enum ChatBindingState { Bound, NotConnected, NotQueryWindow, NoDocument }

internal sealed class ChatBinding
{
    ChatBindingState State; string? SessionId; string? DocumentName; string? DocumentPath;
    string? Server; string? Database; bool IsSticky;
    string BindingKey => Bound ? $"{Server}.{Database}" : "";   // ordinal-ignore-case comparisons
}

internal static class ChatBindingResolver
{
    // UI thread. Gathers DTE.ActiveDocument.FullName, the tracked view for it, and that view's
    // connection, then calls Decide. Never throws: any host failure yields NoDocument + a Debug log.
    internal static ChatBinding Resolve(ChatBinding? previous);

    // Pure, unit-tested. `active` is null when there is no active document.
    internal static ChatBinding Decide(
        ActiveDocumentInfo? active,                       // { FullName }
        Func<string, TrackedViewInfo?> findTracked,       // path → { SessionId, Server?, Database? } or null
        ChatBinding? previous,
        Func<string, bool> isStillOpenAndConnected);      // previous.DocumentPath → bool
}
```

**Decision table** (the only behaviour `Decide` may have):

| active document | tracked view for it | caption | previous usable | result |
|---|---|---|---|---|
| present | found | server + database | — | `Bound` (not sticky) |
| present | found | none | — | `NotConnected` (SessionId set, Server/Database null) |
| present | not found | — | yes | previous with `IsSticky = true` |
| present | not found | — | no | `NotQueryWindow` |
| none | — | — | yes | previous with `IsSticky = true` |
| none | — | — | no | `NoDocument` |

"previous usable" = `previous.State == Bound` and `isStillOpenAndConnected(previous.DocumentPath)`.

`ConnectionWiringHelper.TryFindTrackedView(string filePath, out string sessionId, out IWpfTextView view)`
iterates `OpenViews`, drops closed or collected entries, compares `ITextDocument.FilePath`
ordinal-ignore-case. The resolver reads server/database through the existing
`SsmsConnectionDetector.TryDetectConnection(sp, view)`.

## Part 2 — One binding for the whole panel

- `RefreshBinding()` sets `_binding = ChatBindingResolver.Resolve(_binding)` on load, on the
  2-second tick, when the tool window is created, and at the start of `SendMessageAsync`.
- Header: `SetDatabaseContext` is fed from `_binding` only (`{Server}.{Database}` / `Not connected`;
  tooltip per data-model).
- Schema poll: `PollSchemaStatusAsync(_binding.SessionId)` only when `State == Bound`.
- Send: proceeds only when `State == Bound`; the request's `SessionId` is `_binding.SessionId`.
  No other session source remains in the panel (`TryGetActiveRealSessionId` is no longer called
  from the chat).
- Insert into query: `AiEditorSqlInserter.TryInsertAtCaret(sql, _binding.DocumentPath)` — the
  document is found by `FullName` in `DTE.Documents`; a null path keeps today's active-document
  behaviour. A failed insert flashes `⚠ No query window` on the button (as today's false return).

## Part 3 — Refusal texts (assistant bubble, verbatim)

| State | Text |
|---|---|
| `NotConnected` | `{DocumentName} is not connected to a database (or is still connecting). Connect it — Query ▸ Connection ▸ Connect — then ask again.` |
| `NotQueryWindow` | `The active window ({DocumentName}) is not a query window, and no connected query window has been used yet. Click into a connected query window, then ask again.` |
| `NoDocument` | `No query window is open. Open a query window, connect it to a database, then ask again.` |

`NoConnectionMessage` (the spec 036 constant) is removed; tests that referenced it move to the
three texts above. The engine-not-running and no-agent paths are unchanged.

## Part 4 — The log line (FR-005)

Written at the moment of a refusal, before the bubble is added:

```
Log.Information("AiChatPanel: refused to send — {Reason}; document={Document}; queryWindow={IsQueryWindow}; session={HasSession}; connected={Connected}",
    binding.State, binding.DocumentName ?? "(none)", isQueryWindow, hasSession, connected);
```

Never includes the question text, a path, a server name or a login. A successful send keeps the
existing Debug line (`response received in …`).

## Part 5 — Rebind notice (FR-007)

In `RefreshBinding`, when both the previous and the new binding are `Bound` and their
`BindingKey` differ: `AddConversationNotice($"Now answering about {Server}.{Database} ({DocumentName}).")`.
Not raised on the first binding, not raised when only the document changed, not part of history.

## Part 6 — Acceptance mapping

| Spec scenario | Covered by |
|---|---|
| US1-1 Messages pane then send | manual quickstart S1-1; `Decide` row 1 (document found regardless of pane) |
| US1-2 send right after execute | manual S1-2 |
| US1-3 header and send agree | `AiChatSessionBindingTests` (header text and send gate read one binding) |
| US1-4 second window, note | `Decide` rows + notice test; manual S1-4 |
| US1-5 Object Explorer active | `Decide` sticky rows; manual S1-5 |
| US1-6 disconnected window refusal | `Decide` row 2 + refusal text test; manual S1-6 |
| US1-7 log line | Serilog test sink assertion in `AiChatSessionBindingTests` |
