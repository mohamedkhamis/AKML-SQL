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
