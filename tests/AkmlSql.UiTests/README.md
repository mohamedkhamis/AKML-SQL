# UI automation for SSMS 22 — the desktop equivalent of Playwright

Playwright cannot drive SSMS: it speaks CDP/WebDriver to browser engines, and SSMS is a native
WPF/Win32 application. The equivalent layer on Windows is **UI Automation (UIA)**, Microsoft's
accessibility API, driven here through **FlaUI** (a maintained .NET wrapper over UIA3).

The good news, established by probing a live SSMS 22.9.12120.119, is that the analogy holds
closely. SSMS 22 is the Visual Studio 17.x shell, so its UI is WPF and it exposes a genuinely rich
automation tree with stable ids. Selectors are real selectors, not screen coordinates.

| Playwright | Here |
|---|---|
| `chromium.launch()` | `SsmsApp.Launch(sqlFile)` |
| `browser.newPage()` | `app.MainWindow()` |
| `page.locator("#id")` | `window.Editor()`, `window.TopLevelMenu("AKML SQL")` |
| auto-waiting locators | `Retry.WhileNull` inside every locator; `WaitUntilReady` |
| `page.fill()` / `type()` | `window.TypeInEditor()` (real synthetic input) |
| `textContent()` | `window.EditorText()` via the UIA **TextPattern** |
| `page.on("dialog")` | `window.DismissKnownPrompts()` |
| `page.screenshot()` | `Shot.Screen()` / `Shot.Element(window.Raw, …)` |
| `locator.screenshot()` | `Shot.Element(window.Editor(), …)` |

## Running

```bash
dotnet test tests/AkmlSql.UiTests/AkmlSql.UiTests.csproj
```

Screenshots land in `bin/Debug/net10.0-windows/screenshots/`. Point `Shot.ArtifactDirectory`
somewhere else to generate documentation images.

This project is **deliberately not in `AKML-SQL.slnx`**. It needs a real desktop, it is slow, and it
cannot run on a headless agent — keeping it out means the solution build and the normal test sweep
are unaffected.

## What the tree actually exposes

Verified against a running SSMS 22:

| Surface | Selector | Notes |
|---|---|---|
| SQL editor | `AutomationId=WpfTextView` | Implements **TextPattern** — read the document directly |
| Glyph margin | `AutomationId=WpfEditorUIGlyphMarginGrid` | Where AKML draws analysis glyphs |
| Line numbers | `AutomationId=WpfEditorUILineNumberMargin` | |
| Menu bar | `AutomationId=MenuBar` | Children are the top-level menus; `ExpandCollapse` + `Invoke` work |
| Tool windows | `AutomationId=ST:0:0:{guid}` | VS window GUIDs, e.g. Object Explorer `{d114938f-…}` |
| Error List | `AutomationId=Tracking List View` | Columns: Severity, Code, Description, Project, File, Line |

Prefer **TextPattern over pixels** for anything textual. It reports what the document contains
regardless of scroll position, theme, font, or whether the window is even visible.

## Findings that cost real time — read before debugging

### SSMS 22 changed its command line; `-E` is gone

Passing `-E` does not degrade gracefully. It raises a modal usage dialog, the shell never loads,
and the failure surfaces two minutes later as "the editor never appeared". The accepted switches
are:

```
SSMS.exe [file_name[,file_name]*] [-S server] [-d database] [-U user] [-A method]
         [-C] [-N Optional|Mandatory|Strict] [-i hostname] [-dn name] [-nosplash] [-log file]
```

Windows authentication is now the default when no `-U`/`-A` is given. `-N` defaults to
**Mandatory**, so a local instance with a self-signed certificate is refused unless you pass `-C`.
`SsmsApp.Launch` passes `-C` by default and `SsmsApp.ThrowIfStartupDialog` turns any such dialog
into an immediate, quotable error rather than a timeout.

### `-S` does not connect — it *asks*

SSMS raises a modal **"Connect to the following server?"** prompt. Until it is answered the query
window stays disconnected, and three things silently do not happen: the **Query** menu never
appears, the **Error List** stays empty, and — because package autoload is gated on the SSMS UI
context — **the `AKML SQL` menu never appears either**. Everything looks healthy from the
automation side: the window is up, the document is loaded.

`DismissKnownPrompts()` answers it. It only answers dialogs it recognises; anything else is
recorded in `LastUnhandledPrompt` and surfaced in the timeout message, because blindly clicking the
default button is how automation quietly discards data-loss warnings.

### Minimised windows automate fine but photograph as nothing

A minimised window still answers every UIA query — the tree is live, text reads correctly — but it
reports its position as `(-32000,-32000)`. Screenshots then capture empty space while the
automation half looks perfectly healthy. `BringToFront()` is mandatory before any capture.

### Maximised windows report a rectangle larger than the screen

A maximised window is inset by the invisible resize border: `1936x1056 at (-8,-8)` on a 1920x1080
desktop. Cropping that region throws a bare GDI+ *"Out of memory"* that says nothing about the real
cause. `Shot.Capture` intersects every rectangle with the virtual screen.

### A disconnected RDP session captures black

Processes keep running, but the session loses its rendering surface, so every capture comes back
uniformly black and hit-testing misbehaves. `Preconditions.RequireInteractiveDesktop()` fails fast
on this. Either stay connected for the run, or redirect the session to the console first:

```
tscon.exe %SESSIONNAME% /dest:console
```

### Screen capture, not `PrintWindow`

`PrintWindow` with `PW_RENDERFULLCONTENT` does capture the VS shell's client area, but completion
lists, lightbulb menus and the glyph context menu are **separate top-level windows** layered over
the frame — they are simply absent from a window-scoped grab. Reading the composited screen gets
what a person would actually see. The cost is that the window must genuinely be on top.

### A stale deployment is the most likely cause of a surprising failure

These tests exercise the DLLs in
`…\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql`, not the
working tree. `Preconditions.CheckExtension()` reports the deployed build's age, and the suite logs
a loud warning when it is more than a day old — a UI suite passing against a months-old build
reports confidence it has not earned.

## Known limitation: AKML's own WPF controls are invisible to UIA

The extension's custom surfaces — the editor toolbar (Format / History / Outline / Search /
Analysis / AI Chat / Settings), the warning glyphs, the glyph context menu — render correctly and
are visible in screenshots, but expose **no automation peers**, so they cannot be located by name.
A full-tree enumeration of a loaded SSMS returned 194 descendants with none of them present.

That makes the four disable-rule scopes (spec: line / script / session / everywhere) reachable only
by clicking screen coordinates, which is exactly the brittleness this harness exists to avoid.

The fix is small and is also an accessibility improvement: set `AutomationProperties.AutomationId`
and `AutomationProperties.Name` on the controls built in
`src/AkmlSql.Shell.Shared/Analysis/WarningGlyph.cs`, `WarningGlyphMenu.cs` and the editor toolbar.
Until then, assert analysis behaviour through channels that *are* automatable:

1. **Editor text** (TextPattern) — proves a directive was inserted, e.g. `-- akml-disable-line PE003`.
2. **The Error List** (`Tracking List View`) — Code and Line columns carry the rule id and location.
3. **The engine's own logs** at `%AppData%/AKML SQL/logs/`.

## Regenerating the product-site screenshots

The images on the site are produced by two tours rather than captured by hand, so they can be
refreshed whenever the UI moves instead of quietly going stale.

| Tour | Produces | Command |
|---|---|---|
| `SsmsScreenshotTour` (here) | SSMS 22 with the extension loaded | `dotnet test tests/AkmlSql.UiTests --filter SsmsScreenshotTour` |
| `SiteScreenshotTour` (in `AkmlSql.Web.E2E.Tests`) | web edition: editor, connection dialog | `dotnet test tests/AkmlSql.Web.E2E.Tests --filter SiteScreenshotTour` |

**Everything is shot against Northwind, and that is not a stylistic preference.** The images these
replaced were captured against a live working database and published to a public site, showing real
personal data — names, dates, and links — in the result grid. Both tours assert that the captured
content contains "Northwind" and contains none of the real database names, so a screenshot taken
against the wrong connection fails the run instead of reaching the site.

Install Northwind with the Microsoft sample script, and note the two traps:

```bash
curl -sLo instnwnd.sql \
  https://raw.githubusercontent.com/microsoft/sql-server-samples/master/samples/databases/northwind-pubs/instnwnd.sql

sqlcmd -S "(local)" -E -C -d master -Q "IF DB_ID('Northwind') IS NULL CREATE DATABASE Northwind;"
sqlcmd -S "(local)" -E -C -d Northwind -f 65001 -i instnwnd.sql
```

- The script says so in its own header, but it is easy to miss: **it does not create a database.**
  Run it without `-d Northwind` and all 36 objects land in `master`.
- The file is UTF-8 and `sqlcmd` defaults to the ANSI codepage, so **without `-f 65001`** the
  accented customer names store double-encoded and appear in screenshots as `MÃ¨re Paillarde`.

### What the web tour needs

The web edition reaches SQL Server through a paired engine, not directly, so the tour pairs one
first. Three things about that deployment are worth knowing:

- `IEngineBridge` builds `ws://` when a connection is marked *Localhost* and `wss://` otherwise. The
  installer here provisioned the bridge in **LAN mode with TLS**, so the localhost path — the one
  that waives the PIN — is reset by the TLS listener. The tour therefore pairs as a LAN connection.
- The certificate's SAN list is the machine name and public IP, **not `127.0.0.1`**, so the host
  must be the machine name, and Chromium needs `--ignore-certificate-errors` for a self-signed cert.
- A pairing PIN is single-use and expires. The engine publishes the current one to
  `%ProgramData%\AKML SQL Web\pairing-pin.txt`; the tour reads it from there. If pairing fails with
  `Failed`, restart `AkmlSqlWebEngine` to mint a fresh PIN.

The engine service runs as `LocalSystem`, so it authenticates to SQL Server as
`NT AUTHORITY\SYSTEM`. That login needs read access to Northwind or the connection dialog can only
offer `master`, `msdb` and `tempdb`:

```sql
USE Northwind;
CREATE USER [NT AUTHORITY\SYSTEM] FOR LOGIN [NT AUTHORITY\SYSTEM];
ALTER ROLE db_datareader ADD MEMBER [NT AUTHORITY\SYSTEM];
```

## Suggested next steps

- Add automation ids to the extension's WPF controls (above), then drive the glyph menu directly.
- A screenshot tour for the user guides, which closes the long-standing DOC-002 gap: the docs
  describe SSMS and VS but currently ship only web-edition images.
- A regression test for the disable-rule scopes: type a violation, apply each scope, assert via
  editor text and the Error List.
