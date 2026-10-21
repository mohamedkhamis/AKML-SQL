# Data Model: Query tabs, one Connect to database dialog, and editor polish for the web edition

**Branch**: `043-web-query-tabs-polish` | **Date**: 2026-10-21 | **Spec**: [spec.md](./spec.md)

Phase 1 output. Entities from the spec with their concrete shape, where they live, validation
rules and state transitions. Wire contracts are in [contracts/ipc.md](./contracts/ipc.md); UI
contracts in [contracts/web-ui.md](./contracts/web-ui.md); tokens and settings in
[contracts/theme-and-settings.md](./contracts/theme-and-settings.md).

## 1. Query tab (runtime, web page)

`QueryTab` — one open query in the editor page. Lives in the page's `IQueryTabs` set (R1).

| Field | Type | Notes |
| --- | --- | --- |
| Id | string (guid "N") | stable for the tab's life; the IndexedDB key suffix; the results pane id prefix |
| Name | string | `SQLQuery{n}` or the saved / opened file name (`EditorDocument.SanitizeFileName`) |
| IsDefaultName | bool (derived) | `EditorDocument.IsDefaultName(Name)` |
| IsModified | bool | text ≠ baseline; a programmatic set-text moves the baseline first (spec 041 rule) |
| Baseline | string? | null right after a restore when only the persisted flag is known (today's rule) |
| SessionKey | string (guid "N") | SQL History grouping key; minted with the tab; a new one when a file is opened into a reused empty tab |
| Session | `QueryTabSession` | §3 |
| Kind | `DatabaseKind` (derived) | `Session.Kind`; drives the editor dialect compartment |
| Result | `QueryResult?` | the last outcome shown in this tab's pane |
| ResultGeneration | int | the connection generation the result was read under (results are never edited under another) |
| Findings | `IReadOnlyList<...>` | analysis findings for this tab |
| IsExecuting | bool | set before any await of a run |
| ActiveQueryId | string? | for cancel |
| LastExecutedSql | string? | for the re-run after Apply |
| RunStartedAt | DateTimeOffset? | the pane's running clock |
| ResultReceivedAt | DateTimeOffset? | as today |
| CursorOffset | int | persisted; restored into the editor state |
| AutorunPending | bool | set by History's Re-execute; consumed once, after the tab is connected |

Invariants: names given by the editor are never reused (the set record's `NextDefaultNumber` only
grows); at most 20 tabs; a tab's text is at most 10 MB (chars and UTF-8 bytes, as today).

## 2. Tab set (runtime + persisted)

| Field | Type | Notes |
| --- | --- | --- |
| Order | list of tab ids | the strip order |
| ActiveId | string | the active tab; always one of Order when Order is non-empty |
| NextDefaultNumber | int | moved here from the document record; starts at 1 |
| Version | int | 1 |

Persisted as the `set` record of the `editorTabs` store (§5).

## 3. Query tab session (runtime, web)

`QueryTabSession` — what today's `SqlConnectionService` holds, once per tab (R1, R2).

| Field | Type | Notes |
| --- | --- | --- |
| EngineSessionId | string (guid "N") | persisted with the tab; reused across reloads |
| Binding | `ConnectionBinding?` | §4; null = no connection |
| IsConnected | bool | |
| Server, Database, Login, WindowsAuth, Kind, ServerVersionText, TransactionState | as today | per tab |
| SchemaIdentity | string? (derived) | as today, from the binding and the engine URL |
| ConnectionGeneration | int | per tab; bumped on connect, database change, forget, and bridge URL change (applied to every tab) |
| NeedsReattach | bool | true for a restored tab until its first activation or run has re-sent the connection |
| StateChanged | event | per tab; the facade re-raises the active tab's |

State machine (one per tab):

```text
Unbound ──Connect (dialog / inherit)──▶ Connected
Unbound ◀──Disconnect / Forget─────── Connected
Restored(NeedsReattach) ──activate or run──▶ Reattaching ──SessionDisconnect, ConnectionChanged──▶ Connected
                                                   │ password needed and not remembered ──▶ PasswordPrompt ──▶ Reattaching
                                                   │ engine refuses ──▶ Unbound (binding kept; status shows the error)
Connected ──bridge lost──▶ Disconnected(keeps binding) ──next run or activation──▶ Reattaching
Any ──tab closed──▶ Released (SessionDisconnect{Release=true}; object dropped)
```

Rules: a restored tab never opens its connection at page load (FR-009); `Reattaching` always
sends `SessionDisconnect`, **awaits its reply**, and only then sends `ConnectionChanged`, so an
open transaction left by the previous page is rolled back before the new connection exists (R2;
the engine no longer orders a notification after an earlier request, contracts/ipc.md §2); a
tab's connection cannot be changed or disconnected while that tab's run is in flight (the
controls are disabled, as SSMS greys Change Connection during execution); the facade's
`StateChanged` fires whenever the active tab changes or the active tab's own state changes.

## 4. Connection binding (persisted with the tab)

What a tab remembers about its server. A snapshot of the dialog's target without secrets; the
saved-connection id is a reference only.

| Field | Type | Default | Notes |
| --- | --- | --- | --- |
| SavedConnectionId | string? | null | the saved entry it came from; may dangle after a delete (the binding stays usable) |
| Kind | DatabaseKind | SqlServer | |
| Server | string | | SQL Server `host`, `host\instance`, `host,port`; PostgreSQL host |
| Database | string | | |
| WindowsAuth | bool | true | SQL Server only |
| Login | string? | null | SQL auth login or PostgreSQL user |
| Port | int? | null | PostgreSQL; null = 5432 |
| SslMode | string? | null | PostgreSQL; null = prefer |
| RootCertificatePath | string? | null | PostgreSQL verifying modes; a path on the engine machine |
| TrustServerCertificate | bool | false | SQL Server (FR-026) |
| RememberPassword | bool | false | PostgreSQL opt-in (stored under the JSON name `RememberOnEngine`, as the saved record does) |

Never a password. Validation is the existing `ValidateTarget` / `ParsePostgresTarget` at connect
time; a bad restored binding fails to connect and the tab shows the error. The store's tripwire
test (no "password" in any persisted record) extends to this record.

## 5. Persisted records (IndexedDB, database `AkmlSqlWeb`, version 4 → 5)

New object store `editorTabs` (added to the JS `STORES` list and `StoreNames`).

`tab:{tabId}` → `EditorTabRecord`

| Field | Type | Default | Notes |
| --- | --- | --- | --- |
| Id | string | | |
| DocumentText | string | "" | up to 10 MB |
| CursorOffset | int | 0 | |
| ActiveProfileId | string? | null | as the old record |
| SessionKey | string | | history key |
| EngineSessionId | string | | R2 |
| DocumentName | string | `SQLQuery1` | |
| IsModified | bool | false | |
| Kind | int | 0 | DatabaseKind |
| Binding | ConnectionBinding? | null | §4 |
| AutorunPending | bool | false | History Re-execute |
| SavedAt | DateTimeOffset | | |

`set` → `EditorTabSetRecord` (§2).

Write rules: a tab record is written by the per-key debounce (500 ms, one pending slot **per
key**) on text change, name change, modified change, cursor change and binding change; the set
record is written at once on create, close, reorder and activation. `FlushAsync` flushes every
pending key on page dispose. `RestoreAsync` for a key returns its pending record when one is
waiting (today's rule, per key).

Migration (first load after the upgrade, in C#): if `editorTabs/set` is absent and
`editorSession/current` exists, create tab 1 from it (text, cursor, profile, session key, name,
modified flag, `NextDefaultNumber`), mint an engine session id, write the set, then clear
`editorSession/current`. Settings › Editor's "reset session" clears every `editorTabs` record and
the legacy record.

Unchanged stores: `savedSqlConnections` (already carries every binding field),
`workspaceLayout` (`ResultsTab` and `ResultsHeight` stay layout-wide), `analysisSettings`
(`editorSettings` gains a field, §7), history (SQLite, engine side; `SessionKey` and `TabTitle`
now come from the tab).

## 6. Engine session (engine, `SessionState`)

Unchanged shape. New behaviour: `SessionDisconnect{SessionId, Release=true}` removes the entry
and its document, drops the session's schema caches (every `(sessionId, database)` entry and its
disk file) and its database-provider state (R5). `Release=false` or absent behaves as today.

## 7. Editor appearance

`EditorSettings` (IndexedDB `analysisSettings/editorSettings`):

| Field | Type | Default | Notes |
| --- | --- | --- | --- |
| FontSize | int | 13 | unchanged (9..32) |
| WordWrap | bool | false | unchanged |
| TabSize | int | 4 | unchanged (1..8) |
| FontFamily | enum `EditorFontFamily` { Default, CascadiaMono, Consolas, SystemMonospace } | Default | new; unknown values read as Default |

Export / import: `EditorDto.FontFamily` (string, nullable; missing = keep current). Reset text
keeps "13 px" (a test pins it) and adds the font.

Theme tokens (`docs/theme-tokens.json`), generated into the three theme CSS files and the site
copies:

| Token | Kind | Value | CSS variable |
| --- | --- | --- | --- |
| `typography.editor.lineHeight` | number | 1.25 | `--akml-type-editor-line-height` |
| `Editor.Selection` | colour per theme | measured (R12) | `--akml-editor-selection` |
| `Editor.ActiveLine` | colour per theme | measured | `--akml-editor-activeline` |
| `Editor.MatchingBracket` | colour per theme | measured | `--akml-editor-matchingbracket` |
| `Editor.SearchMatch` | colour per theme | measured | `--akml-editor-searchmatch` |
| `Syntax.String` light | colour | `#15803D` (was `#16A34A`) | existing variable |
| `Syntax.Comment` dark | colour | `#8A99AF` (was `#64748B`) | existing variable |
| `Syntax.Number` light, `Syntax.TableColumn` light | colour | re-measured against `Surface.Editor` ≥ 4.5:1 | existing variables |

Runtime variables set by the editor host: `--akml-editor-font-size` (exists),
`--akml-editor-font-family` (new; the enum's stack or unset for Default).

## 8. Saved connection

`SavedSqlConnection` is unchanged (it already holds Kind, Port, SslMode, RootCertificatePath,
TrustServerCertificate, RememberOnEngine). A tab's binding is built from it on Connect and keeps
`SavedConnectionId`.

## 9. History record

`HistoryRecordRequest` is unchanged; `SessionKey` is the tab's key and `TabTitle` the tab's name
unless it is a default one (today's rule, per tab).
