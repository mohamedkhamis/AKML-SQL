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

## Suggested next steps

- Add automation ids to the extension's WPF controls (above), then drive the glyph menu directly.
- A screenshot tour for the user guides, which closes the long-standing DOC-002 gap: the docs
  describe SSMS and VS but currently ship only web-edition images.
- A regression test for the disable-rule scopes: type a violation, apply each scope, assert via
  editor text and the Error List.
