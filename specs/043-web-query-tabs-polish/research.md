# Research: Query tabs, one Connect to database dialog, and editor polish for the web edition

**Branch**: `043-web-query-tabs-polish` | **Date**: 2026-10-21 | **Spec**: [spec.md](./spec.md)

Phase 0 output. Every decision names its alternatives. Items marked **SPIKE** are claims the
code reading did not settle; each names what proves it before the design that depends on it is
trusted. Codebase facts come from [codebase-map.md](./codebase-map.md) (three read-only maps made
on 2026-10-21) and from a look at the deployed web edition (localhost:8083, web and engine
1.26.1019.2313) in the dark theme.

## R1. The tab model: one page, N tab sessions, the existing connection service becomes the active tab's view

**Decision**: The editor page owns an ordered list of `QueryTab` objects (name, text baseline,
modified flag, session key, results, findings, running state, run start time, connection
binding) and one `QueryTabSession` per tab: the tab's own **engine session id** (persisted, R2),
its binding, connect state, kind, transaction state, generation and `StateChanged`. A small
`IQueryTabs` singleton holds the set and the active tab. `ISqlConnectionService` keeps its
public surface and becomes the **active tab's view**: every property reads from the active tab's
session, `StateChanged` also fires on a tab switch, and `ConnectAsync` / `DisconnectAsync` /
`ChangeDatabaseAsync` act on the active tab unless a session is passed. Operations that may
outlive a tab switch take the tab's session object explicitly, never the facade after an
`await`: query execution (request, cancel, apply, report current database and transaction
state), the history record, `Workspace.KeepResult`, grid Apply, the status bar's Rollback, the
format and analysis requests, completion / quick info / signature / go-to-definition from the
editor, and `SendDocumentAsync`.

**Rationale**: the map found about forty readers of the connection service (status bar, database
selector, Settings sections, History, Snippets, AI dialect context, dialect gates, schema
snapshot) that only ever want "the connection the user is looking at". Keeping the service as
that view leaves them untouched. The handful of sites that must not drift to another tab mid-flight
are listed by line in the map and are the only ones that change signature. This is the smallest
change that satisfies FR-001 to FR-006 (constitution V).

**Alternatives considered**: one scoped `ISqlConnectionService` per tab (Blazor WASM has no
natural scope for a tab; forty readers would need a tab parameter); a tab id on every call
(same churn); keep one engine session and switch its connection per tab (breaks FR-004 and
FR-014 and loses temp tables on every switch).

## R2. Engine session ids are persisted per tab and reused across reloads; a restored tab reconnects cleanly

**Decision**: a tab's engine session id is minted when the tab is created and stored in the
tab record. After a reload the same ids are used, so the engine's per-session schema cache
(keyed by session id and database, persisted to disk under that key) is found again and
completion is immediate. On a restored tab's first activation or first run the browser sends
`SessionDisconnect` for that session (ignored if the engine does not know it or is an older
build), **awaits its reply**, and only then sends `ConnectionChanged` with the tab's binding: a
connection left open by the previous page is rolled back and closed by the engine
(`SessionConnectionRegistry.CloseAsync` rolls back an open transaction), and the tab starts with
a truthful transaction state of None. The await matters under R3: a notification is no longer
ordered after an earlier request on the socket, so the browser sequences the two itself.
SQL authentication and PostgreSQL without a remembered password ask for the password once at
that point (FR-008, US1 scenario 6).

**Rationale**: minting new ids on every reload would leak session entries in the engine
(nothing removes them today, `SessionManager.RemoveSession` has no callers) and lose the
engine-side cache. Reattaching to a possibly open connection silently would keep locks the user
cannot see; SSMS also loses temp tables and open transactions when it is reopened.

**Alternatives considered**: reattach without disconnecting (fast, but the transaction segment
would lie until the first run and there is no request that asks a session for its transaction
state); new ids per reload (leaks, cold caches).

## R3. The engine's web transport answers requests concurrently: notifications inline, requests off the read loop, one lock on the socket

**Decision**: in `WebSocketTransport.cs` the read loop keeps handling **notifications**
(`RequestId == 0`: `DocumentChanged`, `ConnectionChanged`) inline and in order, and dispatches
every **request** (non-zero `RequestId`) to the thread pool, writing the reply under a send lock
(the pattern the named-pipe transport already uses for the AI message types). Ordering that
matters is preserved: a completion request is read after the `DocumentChanged` that preceded it
on the same socket, and that notification has been applied before the request is dispatched.
Two executes on the same session still serialise on that session's `SessionConnection` gate;
executes on different sessions overlap (FR-014). No new capability string: an older engine
simply answers one request at a time, which is today's behaviour.

**Rationale**: the browser bridge already correlates replies by request id, so this is the one
missing piece for FR-014, FR-003 and FR-005 (a long query in one tab must not stall the other
tabs' completion, execution or switching). Cancel of a running query may start to work as a
side effect; the spec keeps mid-run cancel out of scope and this plan promises nothing about it.
(Review of US1, 2026-10-21: it does work. An `ExecuteCancel` notification is read while that
browser's `ExecuteQuery` runs, the engine cancels the token linked into the command, the server
stops the statement and the batches after it are skipped — `RunningStatementCancelIntegrationTests`.
The toolbar's Cancel, its running note and the close-a-running-tab question now say so.)

**Alternatives considered**: per-session ordered lanes (needs the session id at dispatch time,
but the envelope carries only type, request id and an opaque payload, so it would need an
envelope change or a per-type extractor); keeping the serial loop and queuing runs (fails FR-014
and makes multi-tab unusable during a long query).

**SPIKE S1** (first engine task): audit the state that has never run concurrently on this
transport before enabling off-loop dispatch: `SessionManager` (`ConcurrentDictionary`, fine),
`RpcContext` settings cache (`EnsureSettings` / `InvalidateSettings`), the SQLite history store
(connection per call or shared?), `DatabaseProvider` and `TryClaimPopulation`,
`SchemaCacheManager` (documented lock-free reads), the send path (`socket.SendAsync` must be
serialised), and the new race between a notification and an in-flight request on the **same**
session: `ConnectionChanged` (disposes the `SessionConnection` when the string changed) or
`SessionDisconnect` arriving while that session's `ExecuteQuery` is running. The browser removes
the race at the source (a tab's connection cannot be changed or disconnected while its run is in
flight, spec edge case) and the engine must still fail safely if it happens. Prove with a test
that two executes on two sessions overlap and that `DocumentChanged` followed by `Completion` on
one socket completes against the new text.

**S1 outcome (2026-10-21)**: safe to dispatch off the loop. `SessionManager` and
`SessionConnectionRegistry` are `ConcurrentDictionary`-backed; `RpcContext.EnsureSettings` locks;
`DatabaseProvider`'s cache locks; `TryClaimPopulation` is an atomic `TryAdd`; `HistoryDatabase`
opens a `SqliteConnection` per call; `SchemaCacheManager` reads are lock-free by design. The one
unshared resource is the socket's send path, now behind a per-connection `SemaphoreSlim`. The
same-session race fails safely: a `ConnectionChanged` that changes the string disposes the
connection under a running execute, which then ends (no corruption), and the browser never sends
one while that tab runs. (Review of US1: such a run ends with a normal, cancelled reply rather
than an error, and its script may have run a `USE`; the run now moves the session's database only
when its connection is still the session's registered one — `StaleRunDatabaseMoveIntegrationTests`.
The audit also missed `CompletionEngine`, whose per-request settings two concurrent completions
overwrote; completions now take a lock around them.) Chosen branch: notifications and the handshake
inline; requests on `Task.Run` with the transport's lifetime token (not a per-connection token, so
a browser reload does not cancel a running statement, as before); replies under the send lock; a
reply to a closed socket is dropped. Proven by `WebSocketTransportConcurrencyTests` (a slow request
does not hold up a later one; a notification is applied before a request read after it; 40
overlapping 50 KB replies arrive whole; a failing handler answers its own id with an error).

## R4. The engine keeps one schema cache per session; nothing is shared between tabs

**Decision**: no change to `SchemaCacheManager`'s per-session keying. Two tabs on the same
server load the schema twice (Phase A under 500 ms, Phase B in the background) and count twice
against the ten-entry LRU; `ReloadMissing` repopulates an evicted cache from disk on the next
lookup.

**Rationale**: the SSMS shell already mints a session per editor buffer
(`DiagnosticTagger.cs:30`) and even per command (`ExecuteCurrentStatementCommand.cs:90`), so many
same-server sessions under this LRU is production behaviour today. The class documents the
per-session design on purpose, and `Rekey` / `MoveSession` / `TryClaimPopulation` are per-session
mechanics. The browser's own IndexedDB cache is already keyed by server identity and database,
so the offline completion path is shared without any work.

**Alternatives considered**: an alias map from a canonical server-plus-database key to one shared
`DatabaseCache` (touches twelve call sites and the population claim; not asked for by the spec).

## R5. Closing a tab releases its engine session

**Decision**: the `SessionDisconnect` request gains an append-only key `Release` (bool, default
false). With `Release = true` the engine, after today's disconnect (close the connection, roll
back), also removes the session entry and its document (`SessionManager.RemoveSession`) and
drops the session's schema caches and database-provider state. Closing a tab sends it; a browser
that closes without warning still leaves entries behind, exactly as today (an idle-session sweep
is recorded as a follow-up, not built here).

**Rationale**: append-only IPC (CLAUDE.md), one existing message, no capability change (an older
engine ignores the key and behaves as today).

**SPIKE S3**: confirm `SchemaCacheManager` has, or can be given, a `RemoveSession(sessionId)` that
drops every `(sessionId, database)` entry and its disk file, and that `DatabaseProvider` exposes
`InvalidateSession`.

**S3 outcome (2026-10-21)**: `SchemaCacheManager.SaveCacheAsync` / `LoadCacheAsync` have no callers,
so the engine's schema caches are memory-only and there is no disk file to delete. Added
`RemoveSession(sessionId)`, which drops every `{session}:{database}` and `pg|{session}:{database}`
entry and their refresh connection strings. `DatabaseProvider.InvalidateSession` exists and the
disconnect handler already calls it. A consequence for R2: a restored tab's persisted session id
finds its in-memory cache again only while the same engine process keeps running (a service
restart starts cold), which is the case that matters for a page reload. Proven by
`SessionDisconnectReleaseTests`.

## R6. One CodeMirror view; each tab's editor state is swapped in and out

**Decision**: the page keeps **one** `EditorComponent` (one `sql-editor` test id, one key
listener, one tooltip parent). `akml-editor.js` gains `switchTab(hostId, tabId, seedText,
dialect)`: it stores the current `view.state` under the current tab id, then `setState`s the
target tab's stored state (or a fresh state built from `seedText`), re-applies the compartments
(dialect, options), restores the stored scroll position, and does all of this with the text
callback suppressed, so `OnTextChangedFromJs` never fires for a swap and the page never routes a
swap through `SetTextAsync` (which moves the baseline and would un-modify the tab). Undo history,
selection, folds and lint diagnostics live in the state, so they come back with the tab.
`setText`, `replaceTextIfUnchanged` and `setDiagnostics` accept a tab id: for the live tab they
dispatch on the view; for a stored tab they apply the transaction to the detached state and store
the result. A Format or Analyse reply that lands after the user switched tabs is therefore
applied to the tab that asked (the `FormatRequestGate` ticket already carries the session key).
`closeTab(hostId, tabId)` drops the stored state.

**Rationale**: `akml-editor.js` already supports several instances, but mounting N editors
duplicates the fixed `sql-editor` test id used by fifteen browser suites, multiplies key
listeners and memory, and CodeMirror's documented multi-document pattern is state swapping.

**Alternatives considered**: N mounted editors hidden by CSS (test id and memory problems above);
plain `setText` on switch (loses undo, selection and diagnostics; fires change events; moves the
baseline).

**SPIKE S2**: a node test proves that a swap fires no text callback, that undo works after a
round trip, and that `replaceTextIfUnchanged` on a stored tab updates the stored state and is
visible after switching back.

## R7. One results pane per tab, mounted while hidden, with per-tab element ids

**Decision**: `Editor.razor` renders a `ResultsPaneComponent` per tab inside a keyed loop; only
the active tab's pane is visible. `ResultsPaneComponent` and `ResultSetGrid` take an `IdPrefix`
that includes the tab id (today `akml-results` and `akml-grid-{set}` are constants), the
`results-pane` and `execute-complete` test ids are rendered only on the active pane, and the
pane's running clock reads a per-tab `RunStartedAt` parameter instead of the app-wide
`IWorkspaceStatus.ElapsedMs`. `IWorkspaceStatus` becomes the active tab's view (R1): each tab
holds its own phase, outcome and kept result; the page republishes the active tab's on a switch.

**Rationale**: pending grid edits live inside `ResultSetGrid` instances and are lost when the
pane unmounts (which is why spec 041 keeps the pane mounted while hidden); per-tab panes keep
each tab's edits, sort and selection exactly as SSMS does. Lifting the grid's edit state out of
the component would be the larger refactor.

**Alternatives considered**: one pane with swapped `Result` (loses pending edits per tab);
lifting edit state (large, unasked).

## R8. Tabs persist in their own IndexedDB store; the old single record is migrated once

**Decision**: new object store `editorTabs` (DB version 4 → 5; `onupgradeneeded` only creates
missing stores, so nothing else changes). Records: one `tab:{tabId}` per tab (text, name,
modified flag, baseline marker, cursor, session key, engine session id, kind, connection
binding, autorun flag, saved-at) and one `set` record (ordered tab ids, active id,
`nextDefaultNumber`). `EditorSessionStore` is generalised to a per-key debounce (a pending map
instead of one slot) so two tabs typed within 500 ms both persist. On first load after the
upgrade the page reads the legacy `editorSession/current` record, turns it into tab 1 with its
session key and name, and clears it. `NextDefaultNumber` moves from the document record to the
set record (names are never reused across tabs, FR-002).

**Rationale**: one record per tab bounds each write to that tab's text (up to 10 MB); the set
record is tiny and written on reorder, activation and creation. Schemaless values mean no
migration beyond the one-time move.

**Alternatives considered**: one record holding the whole set (every keystroke rewrites every
tab's text); keep `editorSession` and add keys (the pending-write slot would still collapse
writers).

**SPIKE S4**: bump `DB_VERSION`, update the node pin
(`akml-indexeddb.versionchange.test.mjs:41-48`), and prove an existing browser profile upgrades
without losing the saved connections, history settings or the single document.

## R9. Keyboard: Ctrl+Alt+PageDown / Ctrl+Alt+PageUp switch tabs, Ctrl+Alt+W closes one

**Decision**: new key map entries `editor:next-tab` (Ctrl+Alt+PageDown), `editor:previous-tab`
(Ctrl+Alt+PageUp) and `editor:close-tab` (Ctrl+Alt+W), alongside the existing Ctrl+Alt+N
(`editor:new`). The tab strip itself keeps the arrow-key, Home and End behaviour of the shared
`TabStrip`; Delete on a focused tab and a middle click close it. `ShortcutCollisionTests`'
browser-reserved list grows by Ctrl+F4, Ctrl+PageDown, Ctrl+PageUp and Ctrl+Shift+W before
anything is bound, and `NativeKeyAcceptanceTests` checks the three new keys in Chromium, Edge and
Firefox.

**Rationale**: Ctrl+Tab, Ctrl+W and Ctrl+F4 are browser tab keys in every browser (Ctrl+F4
closes a browser tab in Chromium, Edge and Firefox), so SSMS's own keys cannot be captured.
Visual Studio binds Ctrl+Alt+PageDown / PageUp to next / previous document, which SSMS users may
already know, and Ctrl+Alt+W pairs with Ctrl+Alt+N.

**Alternatives considered**: Alt+1..9 (fights browser bookmarks bar and Firefox's menu
mnemonics); chords under Ctrl+K (slower for a frequent action).

## R10. The shared TabStrip grows an optional close button and a modified marker

**Decision**: `TabItem` gains `IsModified` and `Closable`; `TabStrip` gains `OnClose`. When a
strip is closable, each tab renders as a `div role="tab"` holding the label, the modified mark
(`*`, with an accessible name "modified") and a `button tabindex="-1" aria-label="Close
{name}"`; strips that are not closable keep today's `button role="tab"` markup, so the AI dock,
the results pane and the style preview are unchanged. The query tab strip adds a horizontal
scroll that keeps the active tab in view and truncates long names at 180 px with the full name
as tooltip.

**Rationale**: a button cannot contain a button, so a closable tab needs different markup; one
component with an option keeps the roving tabindex, key trap and tests in one place.

**Alternatives considered**: a separate `QueryTabStrip` component (duplicates keyboard handling).

## R11. History opens and re-runs into a new tab

**Decision**: "Open in editor" and "Re-execute" from SQL History write a new tab record (text,
name from the entry's title, kind, and for Re-execute an autorun flag) into the tab store, mark
it active in the set record, and navigate to the editor; the editor page restores the set and
runs the flagged tab once it is connected (today's `?autorun=1` wait, scoped to that tab). If the
active tab is empty and unmodified it is reused instead (FR-011's rule for Open file). (Review of
US1: the run happens only while the tab still holds exactly that query — an edit cancels it — and
only in the page that asked: a reload drops the flag, so no query runs that the user did not see start.)

**Rationale**: it removes the "Discard the changes to {name}?" prompt History shows today, which
SSMS never shows for an open-in-new-window, and it keeps the autorun race fix of spec 041.

## R12. Typography: an explicit line height token, a short font list, and the size that reaches every SQL surface

**Decision**:

- `docs/theme-tokens.json` gains `typography.editor.lineHeight = 1.25` and the generator emits
  `--akml-type-editor-line-height`; `.akml-editor .cm-scroller` sets `line-height` from it. WPF
  reports Consolas's line spacing as 1.1709 of the font size (SSMS 22's Consolas 10 pt renders
  15.6 px lines), CodeMirror's base theme uses 1.4; 1.25 is within FR-016's 10 percent of SSMS and
  stays comfortable for a browser surface. Cascadia Mono measures 1.1621, so the same value
  serves the other offered fonts.
- `EditorSettings` gains `FontFamily` (enum: Default = the token stack; `Cascadia Mono`;
  `Consolas`; `System monospace`), stored, exported and imported like `FontSize`; the editor
  sets `--akml-editor-font-family` at runtime next to `--akml-editor-font-size`, and every SQL
  surface's CSS reads `var(--akml-editor-font-family, var(--akml-type-editor-family))`.
- The completion popup, the Format styles output and the quick-info body drop their fixed
  `--akml-type-editor-size` for the runtime size variable (FR-020, US2 scenario 4).
- New CSS, token-driven, for `.cm-content` (line height inherits), `.cm-cursor` (text-primary),
  `.cm-selectionBackground`, `.cm-activeLine`, `.cm-matchingBracket` and `.cm-searchMatch`
  (FR-019); the four new editor colours are added to the JSON `Editor` group with per-theme
  values so the token gate stays green.

**Rationale**: FR-016 to FR-021; the generator already handles typography (family, size, weight)
and only lacks a line-height branch; the font size setting already shows the runtime-variable
pattern.

**Alternatives considered**: a free-text font name (unreadable editors, FR-017 wants a list);
hand-written CSS literals (fails `WebCssTokenTests`).

## R13. Syntax colours: adopt the desktop's measured fixes and gate every class with the contrast probe

**Decision**: the JSON `Syntax` values move to what the desktop palette already uses where it
diverged for contrast (light String `#15803D`, 5.0:1; dark Comment `#8A99AF`, 5.0:1), and every
class in both themes is computed against `Surface.Editor` before values are final; light Number
(`#EA580C`) and light TableColumn (`#0891C2`) are the two near the 4.5:1 line and are expected to
darken. `WorkspaceAccessibilityTests`' 18-pair probe gains the six syntax pairs per theme plus
caret, selection and active line against the editor surface. History's preview spans move from
`accent-primary` / `status-success` / `text-secondary` to the `--akml-syntax-*` variables
(FR-020: History is already highlighted, so it uses the same colours).

**Rationale**: the desktop file documents the measurements; aligning the JSON reduces the
documented drift between the two palettes rather than adding to it (FR-022).

## R14. Writing assistants: Grammarly-family data attributes on the editor content and the two SQL text boxes

**Decision**: CodeMirror 6.43.5 already sets `spellcheck="false" autocorrect="off"
autocapitalize="off" writingsuggestions="false" translate="no"` on `.cm-content`; the gap is the
assistant opt-outs. `akml-editor.js` adds
`EditorView.contentAttributes.of({ "data-gramm": "false", "data-gramm_editor": "false",
"data-enable-grammarly": "false", "data-lt-active": "false" })` (reachable from the committed
bundle without a rebuild). The snippet body and the Format styles input textareas get the same
four attributes next to their existing `spellcheck="false"`. The chat composer keeps assistance
(FR-037). The keyboard help gains one line saying an assistant that ignores these signals is
outside the product's control (FR-038).

**Rationale**: three attribute spellings cover Grammarly's renames over the years; LanguageTool
honours `data-lt-active`; none of this needs a bundle rebuild.

**Alternatives considered**: a mutation observer that removes injected assistant nodes (fragile,
fights the extension).

## R15. One "Connect to database…" command and dialog title

**Decision**: the palette keeps one command, id `sql:connect`, title "Connect to database…",
which opens the dialog on the active tab's kind (SQL Server when there is none);
`sql:connect-postgresql` is removed (superseding spec 042's `contracts/web-ui.md` row). The
dialog title is the constant "Connect to database" for both kinds. The thirteen user-visible
strings that mean "the server" (listed in [codebase-map.md](./codebase-map.md) §5) are
reworded; kind labels, authentication names and kind-specific hints keep "SQL Server" and
"PostgreSQL". The served docs `doc/deployment.md` and `doc/topics/getting-started.md` are
updated in the same change (constitution, documentation currency). Tests pinned to the old title
(`ConnectionManagerModalKindTests.cs:110,146`) are updated; the PostgreSQL-hints-contain-no-"SQL
Server" assertion (`:484`) constrains any new generic hint.

## R16. The dialog is regrouped and fits a 1280x720 window without an inner scroll

**Decision**: the form is laid out as Server (kind, display name, host or server, port beside the
host for PostgreSQL), Sign-in (authentication, user, password, remember), Database (name plus
list button) and Security (trust the certificate for SQL Server; TLS mode and CA path for
PostgreSQL), each with a one-line hint; the Security group is visible without scrolling at
1280x720 for the SQL Server form and the PostgreSQL form scrolls only when the CA path row is
shown. The saved-connection rows keep their kind and sign-in badges (FR-029).

**Rationale**: the deployed dialog scrolls inside itself even at 1610x860 and the trust checkbox
is the last row, which is the most likely reason the user believes it is missing (FR-024,
FR-026).

## R17. Dark theme: the first confirmed defect is the missing colour scheme; the walk is screenshots plus the probe

**Decision**: `:root[data-akml-theme="dark"]` declares `color-scheme: dark` (and `light` for the
light theme; high contrast is left to forced colours), outside the token gate because it is not a
colour literal. This fixes native checkboxes, select popups and scrollbars that render light in
the dark theme today (confirmed in the deployed connection dialog: the checkbox foreground was
black on a transparent box and the dialog's scrollbar was light). The dark walk is
`WorkspaceScreenshotTour` extended with the dialog and popup list from the map (connection
dialog, command palette, cell value, context menus, completion, quick info, Find panel, History
compare, notifications) in the dark theme at 1280x720 and 1920x1080, plus the contrast probe
extension from R13. The user's screenshots (clarification Q3) name the screens fixed first.

**Alternatives considered**: a generic contrast crawler over every text node (noisy, not what
SC-009 asks; pairs plus surfaces plus screenshot review is the agreed gate).

## R18. Tab limit and engine resources

**Decision**: 20 open tabs (FR-013). Each tab costs the engine a session entry, a connection
while bound, and schema caches that count against the ten-entry LRU (R4). Restored tabs connect
lazily (FR-009), and closing a tab releases everything (R5), so the steady-state cost is the
tabs the user keeps.

## R19. Status bar auto-restore of the last saved connection

**Decision**: the status bar's one-time auto-connect applies to the active tab only when that
tab has no binding (first run after install or after the legacy migration); tabs restored with
a binding reconnect through R2 instead.

## Facts checked outside the repository

- WPF `FontFamily.LineSpacing` on this machine: Consolas 1.1709, Cascadia Mono / Code 1.1621,
  Courier New 1.1328 (measured with PresentationCore on 2026-10-21); CodeMirror base theme line
  height 1.4 (`@codemirror/view` 6.43.5, vendored).
- Grammarly opt-out attributes in use across vendors: `data-gramm`, `data-gramm_editor`,
  `data-enable-grammarly` (Telerik, Oracle guidance, CodeMirror forum); LanguageTool:
  `data-lt-active`.
- Browser-reserved keys: Ctrl+W and Ctrl+F4 close a browser tab; Ctrl+Tab, Ctrl+PageDown and
  Ctrl+PageUp switch browser tabs (Chromium, Edge, Firefox).
