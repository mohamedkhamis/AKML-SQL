# SC-006 evidence — cache-backed IntelliSense while bridge is closed

Spec 021 (web edition) SC-006 acceptance is "with engine off, exercise the full
P1 surface." This folder collects the evidence as the surface lands. Items here
are screenshots captured during interactive Playwright sessions, not generated
by CI.

## What's covered today

- **cache-fallback-completion.png** — Editor at `http://127.0.0.1:5050/` with
  the bridge in `Disconnected` state (no engine paired; status bar at the
  bottom reads "Disconnected — Offline — formatter / analyser run in-browser
  only."). A demo `SchemaSnapshot` was seeded into IndexedDB (server
  `demo-server`, database `Northwind`) with Phase A bytes that decode to
  schemas `dbo` and `sales` containing `Customers`, `Orders`, `Products`,
  `Invoices`. Typing `SELECT * FROM Cust` triggers CodeMirror's autocomplete,
  which calls `EditorComponent.RequestCompletionsFromJs` → `ICompletionService.
  CompleteAsync`. The bridge state is `Disconnected`, so the service walks
  `ISchemaCacheStore.ListAsync().LastOrDefault()`, deserialises the cached
  Phase A blob, and synthesises a `CompletionResponse`. The popup renders
  `dbo.Customers` with the `o` class glyph and `dbo` as the detail row —
  proof that the offline path is live end-to-end. Format / Analyse on the
  same surface have always worked offline (they're pure in-browser code);
  this screenshot just adds the IntelliSense piece that landed at T109.

- **post-keyword-trigger-AND.png** — Same offline session as
  `cache-fallback-completion.png`. The user types
  `SELECT * FROM Customers WHERE Id = 1 AND ` with a TRAILING SPACE. CM's
  built-in `activateOnTyping` doesn't fire on whitespace, so this would
  previously have left the popup closed; the user had to press Ctrl+Space
  to surface suggestions. The fix adds an `updateListener` that detects a
  doc-change ending in a non-identifier char preceded by an SQL trigger
  keyword (WHERE, AND, OR, FROM, JOIN, ON, SET, HAVING, SELECT, GROUP BY,
  ORDER BY, BY, WHEN, THEN, ELSE, IN) and calls
  `cm.autocomplete.startCompletion(view)` manually. The screenshot shows
  the popup auto-opened with the alphabetic head of the 50-item candidate
  list (keywords + cached schemas + cached objects); a DOM inspection
  during the verification confirmed `dbo`, `dbo.Customers`, `dbo.Orders`,
  `dbo.Products`, `sales`, `sales.Invoices` were all present alongside
  the keywords.

- **post-keyword-trigger-AND-narrowed.png** — Continuing from the previous
  screenshot, the user types `Cust`. CM's `validFor: /^[\w]*$/` keeps the
  source from being re-invoked while the prefix stays valid, and CM's
  built-in fuzzy filter narrows the list to `dbo.Customers` (with class
  glyph + `dbo` detail row). This proves the full flow — post-keyword
  trigger → empty-prefix popup → CM-side filtering as the user types —
  works without any further network round-trips.

- **order-by-qualified-columns.png** — User typed:
  ```
  SELECT created_at,*
  FROM martyrs
  ORDER BY ma
  ```
  Against the un-fixed version of the engine this would produce
  `Msg 209: Ambiguous column name 'created_at'` because the SELECT pulls
  `created_at` both explicitly and via `*`. The popup now shows the
  table-qualified forms `martyrs.created_at`, `martyrs.id`, `martyrs.name`,
  `martyrs.updated_at` alongside `dbo.martyrs` itself — picking
  `martyrs.created_at` produces a disambiguated `ORDER BY` clause. The fix
  emits every column twice: bare `col.Name` (works in single-table queries)
  AND `obj.ObjectName.col.Name` (disambiguates SELECT-* / multi-table /
  GROUP BY / ORDER BY contexts). CM's fuzzy filter naturally promotes
  whichever form matches what the user is typing.

- **post-keyword-trigger-no-cache.png** — The realistic first-use state:
  bridge `Disconnected`, IndexedDB schema-entries store **empty** (never
  paired with an engine, or paired but engine has no active SQL session
  yet). The user types `where columnA = 'A' and ` (lowercase, trailing
  space — the exact pattern from the bug report). Popup auto-opens with
  the alphabetic head of the SQL keyword list (ALTER, AND, AS, BEGIN,
  BETWEEN, CASE, CREATE, …). Previous behaviour: empty popup → user
  thought autocomplete was broken. Now: keywords are always present
  offline regardless of cache state, and schema/object items layer on
  top once a snapshot exists. DOM inspection during verification
  confirmed 50 keyword items in the list.

## What's still pending

- **Format / Analyse offline screenshot** — Already demonstrated by the
  spec-021 walkthrough screenshots in the conversation history; not staged
  here because the offline behaviour for those surfaces is unchanged from
  the M2 baseline.
- **QuickInfo / SignatureHelp offline UI evidence** — The services land in
  T109's follow-up commit, but Editor.razor doesn't yet wire hover /
  signature affordances to them, so there's nothing visible to screenshot.
  Evidence captures land alongside the eventual UI wiring.

## How the demo cache snapshot was generated

A short xunit `[Fact]` (`SeedBlobEmitter`, removed after this commit) ran
`MessagePackSerializer.Serialize(payload)` and printed the base64 to the
test output stream. The test was a one-shot generator — to regenerate,
re-add the file under `tests/AkmlSql.Web.Tests/Completion/` and run
`dotnet test --filter SeedBlobEmitter --logger:"console;verbosity=detailed"`.

To seed the live browser session, the base64 was fed to a Playwright
`browser_evaluate` call that opened the `AkmlSqlWeb` IndexedDB and `put`
a `SchemaSnapshot` JSON under the composite key
`demo-server` + `U+001F` + `Northwind` (matching `SchemaSnapshot.MakeKey`).
