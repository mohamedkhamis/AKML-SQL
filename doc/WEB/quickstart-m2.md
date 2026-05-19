# Quickstart — Web edition M2 (User Story 1)

This walks a developer through running the M2 surface locally: the in-browser editor + formatter + analyser, with no engine and no installer.

## Prerequisites

- .NET SDK 10.0 preview 3 or later (`dotnet --version` should show `11.0.100-preview.*`).
- A modern Chromium / Firefox / Safari with IndexedDB and `prefers-color-scheme` support.

## Run

```bash
cd D:\Repo\01-Khamis-Projects\AKML-SQL
dotnet run --project src/AkmlSql.Web/AkmlSql.Web.csproj
```

The dev server prints a URL (defaults to `http://localhost:5001`). Open it in a browser.

## What you should see

1. The page boots into the **Editor** route with an empty CodeMirror 6 editor.
2. The top toolbar exposes a **Profile** picker (defaults to "AKML Default"), a **Format** button, an **Analyse** button, and a **Save** button.
3. The right side panel is the **Problems** list (empty initially).
4. The footer status bar shows the scaffold status.

## Try the flow

1. Paste an unformatted snippet into the editor:
   ```sql
   select   id ,name from dbo.Customers where active=1 order by name
   ```
2. Press **Ctrl+K, Ctrl+F** (or click *Format*). The editor reflows the SQL using the active profile.
3. Press **Ctrl+K, Ctrl+L** (or click *Analyse*). The Problems list populates with any findings.
4. Click a row in the Problems list — the editor jumps to that line.
5. Open **Settings** in the top nav. Switch the theme to **Dark** and back to **System** — the editor and chrome reflow in real time.
6. Reload the page. The editor restores the document text, caret position, and active profile (T051 session-restore).
7. Open **Diagnostics** in the top nav. Confirm the recent format / analyse actions are logged. Click **Export diagnostics** — a `akmlsql-diagnostics.zip` downloads.

## What is *not* in M2

- **No engine connection.** The web edition runs format + analyse entirely in-browser. M3 wires the WebSocket bridge for live IntelliSense (completion / signature help / quick info / goto definition).
- **No schema cache.** The editor's completion source is keywords + snippets only. M5 wires the IndexedDB cache.
- **No AI panel.** M6 adds Text-to-SQL / Explain / Fix / Optimize.

## Bundle-size audit (deferred to T054)

`dotnet publish -c Release` against `src/AkmlSql.Web/` produces `bin/Release/net10.0/wwwroot/_framework/`. The audit is recorded in `specs/021-web-edition/M2-BUNDLE-SIZE.md` once the production build is run on a Windows box with Inno Setup + IIS available.

## Known caveats

- **CodeMirror is loaded from `esm.sh` in dev.** The release build vendors a local copy under `wwwroot/lib/codemirror/` (T054 picks a pinned version).
- **First load is slow.** Blazor WASM cold-load + Mono runtime initialisation is ~5–10 seconds on a fresh cache. Subsequent loads are near-instant.
- **Theme switches between Light/Dark/HighContrast happen via a `<link>` href swap.** A brief flash of the previous theme can occur — we leave it for now because it only affects the chrome, not the editor body.

## Where to look in the code

| Concern | Path |
|---------|------|
| Page entry | `src/AkmlSql.Web/Pages/Editor.razor` |
| Editor wrapper | `src/AkmlSql.Web/Shared/EditorComponent.razor` |
| CodeMirror shim | `src/AkmlSql.Web/wwwroot/js/akml-editor.js` |
| Theme bootstrap | `src/AkmlSql.Web/wwwroot/js/akml-theme.js` |
| IndexedDB shim | `src/AkmlSql.Web/wwwroot/js/akml-indexeddb.js` |
| Theme service | `src/AkmlSql.Web/Services/IThemeService.cs` |
| Profile store | `src/AkmlSql.Web/Services/IProfileStore.cs` |
| Session store | `src/AkmlSql.Web/Services/IEditorSessionStore.cs` |
| Ring buffer | `src/AkmlSql.Web/Services/IDiagnosticsRingBuffer.cs` |
| Tests | `tests/AkmlSql.Web.Tests/` (49 tests, all green) |
