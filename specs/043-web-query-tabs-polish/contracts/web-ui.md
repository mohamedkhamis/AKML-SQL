# Contract: web edition UI

**Branch**: `043-web-query-tabs-polish` | **Date**: 2026-10-21

Existing test ids keep their meaning for the **active** tab so the fifteen browser suites that
use `sql-editor`, the four that use `doc-*` and the nine that use `results-pane` /
`execute-complete` keep passing. New ids are listed so browser tests can be written before the
components. This file supersedes the `sql:connect-postgresql` row of
`specs/042-postgresql-support/contracts/web-ui.md`.

## 1. Query tab strip (`Pages/Editor.razor`, centre cell)

Replaces the `.akml-docbar` row above the editor. Built from the shared `TabStrip` with
`Closable` (R10), `IdPrefix="doc"`, `Name="doc"`.

| Element | Test id / attribute | Behaviour |
| --- | --- | --- |
| Strip | `tabstrip-doc` (existing `TabStrip` naming) | `role="tablist"`, `aria-label="Query tabs"`; horizontal scroll; the active tab is scrolled into view on activation |
| Tab | `doc-tab` + `data-tab-id="{id}"` + `data-active` ("true" on the active tab, "false" otherwise) | `div role="tab"`, label truncated at 180 px with the full name as `title`; arrow keys, Home, End as the shared strip; Delete closes the focused tab; middle click closes |
| Active tab label | `doc-name` (kept) | the active tab's name (the E2E `DocumentFlowTests` read it) |
| Modified mark | `doc-modified` (kept, active tab) and `data-modified="true"` on every modified tab | `*`, accessible name "modified" |
| Close button | `doc-tab-close` inside each tab | `button tabindex="-1" aria-label="Close {name}"` |
| New tab | `doc-new` (kept) | opens a new tab (FR-002); disabled with `title="You have 20 query tabs open; close one to open another"` at the cap (FR-013) |
| Open / Save / Save as | `doc-open`, `doc-open-input`, `doc-save`, `doc-saveas` (kept) | act on the active tab; Open goes to a new tab unless the active one is empty and unmodified (FR-011) |
| Close confirmation | `doc-close-dialog`, buttons `doc-close-save`, `doc-close-discard`, `doc-close-cancel` | "Save changes to {name}?" (FR-007); Cancel is the default and Escape; an open or aborted transaction shows the existing Disconnect warning first |

Page title: the active tab's name, modified mark and connection (today's `TitleFor`).

## 2. Keys and palette commands

| Command id | Title | Key | Group |
| --- | --- | --- | --- |
| `editor:new` | New query | Ctrl+Alt+N (unchanged) | File |
| `editor:next-tab` | Next query tab | Ctrl+Alt+PageDown | View |
| `editor:previous-tab` | Previous query tab | Ctrl+Alt+PageUp | View |
| `editor:close-tab` | Close query tab | Ctrl+Alt+W | File |

`ShortcutCollisionTests`' browser-reserved list gains Ctrl+F4, Ctrl+PageDown, Ctrl+PageUp and
Ctrl+Shift+W; the three new keys appear in the keyboard help and in `NativeKeyAcceptanceTests`
(Chromium, Edge, Firefox).

## 3. Results panes

| Element | Contract |
| --- | --- |
| One `ResultsPaneComponent` per tab, mounted while hidden (`hidden` attribute + class), inside a keyed loop | pending grid edits, sort and selection survive a tab switch |
| `IdPrefix` | `akml-results-{tabId}`; grids `akml-grid-{tabId}-{set}` (no duplicate element ids) |
| `results-pane`, `execute-complete`, `results-tab-*` test ids | rendered **only on the active tab's pane**; inactive panes carry `results-pane-inactive` |
| Running clock | from the pane's `RunStartedAt` parameter, not the app-wide status |
| Results / Messages / Problems selection | layout-wide (unchanged `workspaceLayout.ResultsTab`) |
| Status bar outcome and rows segments | the active tab's; switching tabs switches them within one second (FR-005) |

## 4. Connection dialog (`Shared/ConnectionManagerModal.razor`)

| Element | Test id | Contract |
| --- | --- | --- |
| Title | `conn-title` | always "Connect to database" (FR-024) |
| Kind switch | `conn-kind-sqlserver`, `conn-kind-postgresql` (kept) | first control; same semantics as spec 042 |
| Groups | `conn-group-server`, `conn-group-signin`, `conn-group-database`, `conn-group-security` | Server (kind, display name, server / host, PostgreSQL port beside the host), Sign-in (authentication, login, password, remember, forget), Database (name + list button), Security (trust the certificate for SQL Server; TLS mode and CA path for PostgreSQL); one-line hint per group that needs one |
| Fit | | the SQL Server form shows every group without an inner scroll at 1280x720; the PostgreSQL form scrolls only when the CA path row is shown |
| Trust the certificate | `conn-trust-cert`, `conn-trust-cert-help` (kept) | saved, restored and applied on connect and on a restored tab's reattach (FR-026); the help says a server on the engine's machine is never validated (FR-028); disabled with that hint when the server is loopback |
| Saved rows | `conn-row-kind` (kept) | kind and sign-in badges (FR-029) |
| Connect | `conn-connect-btn` (kept) | binds the **active tab** (or the tab that opened the dialog when a run asked for a connection, US1 scenario 7) |
| Themes | | same layout in light, dark, high contrast; keyboard order top to bottom (FR-030) |

## 5. Palette and wording (FR-023, FR-025)

| Surface | Before | After |
| --- | --- | --- |
| Command palette | "Connect to SQL Server…" (`sql:connect`) and "Connect to PostgreSQL…" (`sql:connect-postgresql`) | one command `sql:connect` "Connect to database…", opens the dialog on the active tab's kind (SQL Server when none); `sql:connect-postgresql` removed |
| Dialog title | per kind | "Connect to database" |
| Connection picker help (`ConnectionPickerComponent.razor:38`) | "Connect to SQL Server" | "Connect to database" |
| Engine diagnosis (`EngineConnectDiagnosis.cs:53`) | "use Connect to SQL Server…" | "use Connect to database…" |
| Status bar (`StatusBar.razor:555`) | "Not connected to SQL Server — click to connect" | "Not connected — click to connect to a database" |
| Status bar (`StatusBar.razor:574`) | "Engine connected — connect to SQL Server for schema-aware IntelliSense." | "Engine connected — connect to a database for schema-aware IntelliSense." |
| Execute tooltip (`Editor.razor:70`) | "Execute needs a SQL Server connection" | "Execute needs a database connection" |
| Schema refresh (`SchemaTreeComponent.razor:483`) | "Refresh needs a SQL Server connection" | "Refresh needs a database connection" |
| Go to definition (`EditorComponent.razor:445`) | "Connect to a SQL Server to view object definitions." | "Connect to a database to view object definitions." |
| Settings › Connections (`ConnectionsSection.razor:24, 38, 42`) | "…the SQL Server connection", "…then connect to SQL Server.", "Not connected to SQL Server." | "…the database connection", "…then connect to a database.", "Not connected to a database." |
| Service message (`ISqlConnectionService.cs:561`) | "…then connect to SQL Server." | "…then connect to a database." |
| Served docs `doc/deployment.md:316`, `doc/topics/getting-started.md:47` | "Connect to SQL Server" | "Connect to database" |

Kept as they are: kind labels and badges, "SQL Server authentication", "Windows authentication
works for a SQL Server on the engine's own computer", the guard messages, the AI system prompt,
the History and Snippets kind filters. `ConnectionManagerModalKindTests.cs:110,146` are updated
to the constant title; `:484` (no "SQL Server" in PostgreSQL hints) must still pass.

## 6. SQL History

| Action | Contract |
| --- | --- |
| Open in editor | writes a new tab record (text, name = entry title or next default, kind) and makes it active, then navigates to `/`; reuses the active tab when it is empty and unmodified; no "Discard the changes" prompt |
| Re-execute | as above with `AutorunPending=true`; the editor runs it once the tab is connected (the spec 041 wait, scoped to the tab); the `?autorun=1&kind=` query string is no longer used (`Editor.AutorunKind` and its test `HistoryPageKindTests.cs:313-314` go with it) |
| Grouping | one history session per tab (`SessionKey`); `TabTitle` = tab name unless default |

## 7. Editor surface attributes (FR-036 to FR-038)

| Element | Attributes |
| --- | --- |
| `.cm-content` (via `EditorView.contentAttributes`) | `data-gramm="false" data-gramm_editor="false" data-enable-grammarly="false" data-lt-active="false"` in addition to CodeMirror's `spellcheck="false" autocorrect="off" autocapitalize="off" writingsuggestions="false" translate="no"` |
| Snippet body textarea, Format styles input textarea | the same four `data-*` attributes next to their `spellcheck="false"` |
| Chat composer, search boxes, name fields | unchanged (assistance stays) |
| Keyboard help | one line: "Writing assistants are asked to stay out of the SQL editor; an assistant that ignores that request is outside this product's control." |

## 8. Dark theme

| Item | Contract |
| --- | --- |
| `:root[data-akml-theme="dark"]` | `color-scheme: dark`; light theme `color-scheme: light`; high contrast untouched (forced colours) |
| Walk | `WorkspaceScreenshotTour` adds, in the dark theme at 1280x720 and 1920x1080: the connection dialog (both kinds), the command palette, the cell value dialog, the schema context menu, the grid tooltip, the editor completion, quick info and Find panel, the History compare dialog, the Snippets listbox, a notification; output under `specs/043-web-query-tabs-polish/baseline/dark/` |
| Probe | `WorkspaceAccessibilityTests` gains the six syntax pairs, caret, selection and active line against `surface-editor` per theme |
| User screenshots | the screens they show are checked first and recorded under Clarifications |
