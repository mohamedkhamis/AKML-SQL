# Quickstart: SQL Prompt Visual Parity + Format Gap Closure

**Feature**: `020-sqlprompt-visual-parity`
**Date**: 2026-05-13

A developer's first-hour walkthrough — clone, build, install, exercise the imported SQL Prompt style and verify a P1 deliverable end-to-end.

---

## 0. Prerequisites

- Windows 10 / 11.
- Visual Studio 2022 Enterprise (the MSBuild that ships with it).
- SSMS 22 installed (fastest feedback loop; install paths under `Release/`).
- .NET 10 SDK on `PATH`.
- Inno Setup 7 (only needed to build the installer; skip for the quickstart).
- Branch `020-sqlprompt-visual-parity` checked out.

---

## 1. Build the engine and the SSMS 22 shell

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"

# Engine first (must be published before the installer / shell loads it)
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64

# Shell — SSMS 22 (fastest feedback for visual parity work)
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build -p:Configuration=Release -v:minimal
```

Notes:
- Do not use `dotnet build` for shell projects (CodeTaskFactory needs full MSBuild).
- Build shells **one at a time** — solution-wide builds cause VSCT cross-contamination.

---

## 2. Install in SSMS 22

The extension is XCOPY-deployable. SSMS 22's extension path lives under `Release/`:

```text
C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql\
```

Copy the build output (`bin/Release/net472/`) into that directory. Clear the MEF cache before launching:

```text
%LocalAppData%\Microsoft\SSMS\22.0_*\ComponentModelCache\
```

Launch SSMS 22. Tools → AKML SQL menu should appear.

---

## 3. Import a `.sqlpromptstyle` file (US2 — primary P1 deliverable)

1. Tools → AKML SQL → Options…
2. Navigate to **Format → Styles** in the tree.
3. Click **Edit Formatting Styles…** to open `FormatStylesEditorWindow`.
4. In the left panel, click **Import…**.
5. Pick a `.sqlpromptstyle` from `tests/format-parity/styles/` (the corpus folder once populated — until then, any real-world SQL Prompt style file works).
6. The style appears in the list with the name from `metadata.name` and a `📥 imported` badge.
7. Select the imported style. The settings tree (middle panel) and controls (right panel) populate with the imported values.
8. If the file referenced settings AKML does not yet support, scroll to **Settings not yet supported** at the bottom of the right panel — those keys are listed verbatim from the source JSON.

### Verify the import is round-trip safe

1. Right-click the imported style → **Export…**, save anywhere.
2. Open the exported file in a diff tool against the source. Expected: identical for every mapped key; identical at unknown keys (preserved via `PassthroughUnknownKeys`); whitespace formatting may differ.

### Verify the active style actually formats SQL

1. Set the imported style as **Active** in the style list (radio / star icon).
2. Close the editor and the Options dialog.
3. Open a new query window, paste a 200-line SQL document.
4. Press `Ctrl+K, Y`. AKML's formatter (running in the engine) applies the active style.
5. Compare output against SQL Prompt's output for the same input + same style (use a side-by-side diff tool).

---

## 4. Verify visual parity (US1 — primary P1 deliverable)

### Toggle theme and watch every chrome surface react

1. In SSMS 22 settings, switch the host theme from Dark → Light → Dark within a minute.
2. Watch the AKML-SQL Options dialog (if open) and the Format Styles editor — they re-theme within 1 second per surface, no restart needed.
3. Open a query window; type to summon the suggestion popup; toggle theme again — popup re-themes between key strokes.

### Confirm no hardcoded chrome hex

```bash
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj --filter "FullyQualifiedName~HardcodedHexScanner"
```

A green test means every `.cs`/`.xaml` file under `src/AkmlSql.Shell.Shared/` reads chrome colours from `ThemeTokens` (semantic colours — error red / warning amber / success green / info blue — are the only allow-listed literals).

### Side-by-side screenshot check

For any surface listed in FR-005..FR-014:

1. Take an AKML-SQL screenshot in Dark theme.
2. Open the corresponding `doc/SQL-PROMPT/.../*.svg` (e.g. `01_suggestion_popup.svg` for FR-005).
3. Compare. Acceptable: dimensions within 8 px, colours within one tonal step, fonts visually identical.

---

## 5. Live preview latency check (SC-009)

1. Open Format Styles editor with the imported style active.
2. In the right preview panel, watch a 200-line SQL sample render.
3. Toggle any setting in the controls panel (e.g. flip `lists.placeCommasBeforeItems`).
4. The preview re-renders. From toggle to repaint should feel instant — formally ≤ 250 ms p95.
5. Hold a number-spinner's up-arrow on a threshold setting (e.g. `dml.collapseStatementsShorterThan`). The 100 ms debounce should coalesce rapid changes into single preview refreshes; the engine should never queue 20 in-flight requests.

---

## 6. DPI scaling check (SC-005)

1. Right-click desktop → Display Settings → set Scale to 125 %.
2. Open Options dialog; verify no clipping, no overflow, no layout breakage.
3. Repeat at 150 % and 200 %.
4. Repeat for: suggestion popup, column picker, Format Styles editor, SQL History window (in SSMS), AI window (if enabled).

---

## 7. Run the test suites

```bash
# Core (engine-side + theme) unit tests
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj

# Format parity corpus check (once populated)
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj --filter "FullyQualifiedName~FormatParity"
```

Expected once P1/P2 land: importer / exporter / `SqlPromptKeyMap` / hardcoded-hex scanner suites all green; parity-corpus suite at ≥ 95 % match.

---

## 8. Common gotchas

| Symptom | Cause | Fix |
|---|---|---|
| AKML menu does not appear after install | Stale MEF cache | Clear `%LocalAppData%\Microsoft\SSMS\22.0_*\ComponentModelCache\`, relaunch |
| Theme switch doesn't propagate to a surface | Surface still uses legacy `ThemeManager` (T044 not finished for that surface) | Migrate the surface to `ThemeTokens` with `SetResourceReference` |
| `.sqlpromptstyle` import "succeeds" but every setting is default | Importer can't find `metadata.name` or the JSON is one level deeper than expected (some Redgate exports nest under a wrapper) | Confirm the file's root is the settings object, not a wrapper; check `SqlPromptStyleImporterTests` for the canonical shape |
| Preview takes > 1 s | Engine cold-started; named pipe just initialised | Toggle a setting twice — second iteration is on the warm path |
| Hardcoded-hex test fails after editing a surface | New literal hex slipped in | Replace with a `ThemeTokens.*` reference and `SetResourceReference`. Allow-list only `#FF0000` / `#FFA500` / `#00C853` / `#0078D4` (status semantic colours) |
| Imported style format output differs from SQL Prompt | Mapped setting marked `GapToImplement` — formatter pipeline doesn't yet honour it | Check `SqlPromptKeyMap` for the setting's `Status`. Gap closure is a P2 task in `tasks.md`. |

---

## 9. What "done" looks like for P1

- A SQL Prompt user can install AKML-SQL, point it at their team's `.sqlpromptstyle`, and start formatting without changing anything.
- Every chrome surface they open reads from `ThemeTokens` — no jarring hardcoded colours.
- The Format Styles editor opens, shows the imported style, surfaces unsupported settings clearly, and lets them re-export the file lossless.
- The 12-target test matrix (`SqlPromptKeyMapTests` × `SqlPromptStyleImporterTests` × `SqlPromptStyleExporterTests` × `HardcodedHexScannerTests`) is green.

P2 then closes the formatter-pipeline gaps in `R5` so the parity corpus crosses 95 %; P3 finishes Tab Coloring, History, Code Analysis, AI window, and editor-margin chrome.
