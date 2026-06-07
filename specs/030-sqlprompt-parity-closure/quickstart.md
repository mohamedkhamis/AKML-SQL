# Quickstart — SQL Prompt Parity Gap Closure

How to build, test, and validate this feature. Commands follow `CLAUDE.md` (shell projects need **full MSBuild**, never `dotnet build`, never via the solution).

## Prerequisites

- Visual Studio 2022 MSBuild (for the net472 shell), .NET 10 SDK (engine/libraries/tests).
- SSMS 22 and/or Visual Studio 2026 installed for live verification.
- A SQL Server connection (Windows auth, or SQL auth via spec 029) with a populated schema, for schema-dependent features.

## Build

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"

# Engine + libraries (net10) — formatter/analysis/intellisense changes live here
dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release

# Shell, per host (NEVER dotnet build, NEVER the solution)
"$MSBUILD" src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Build    -p:Configuration=Release -v:minimal
"$MSBUILD" src/AkmlSql.VS2026/AkmlSql.VS2026.csproj -t:Restore  -p:Configuration=Release -v:quiet
"$MSBUILD" src/AkmlSql.VS2026/AkmlSql.VS2026.csproj -t:Build    -p:Configuration=Release -v:minimal

# Engine publish (required before installer / live test — partial DLL swaps break the pipe)
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64
```

## Test (TDD — write the failing test first)

```bash
# Per area (fast inner loop)
dotnet test tests/AkmlSql.Formatting.Tests          # R1/R2 — rules pass + action dispatch
dotnet test tests/AkmlSql.Analysis.Tests            # R3 — .casettings live threading
dotnet test tests/AkmlSql.IntelliSense.Tests        # R5/R6 — temp tables + honored settings
dotnet test tests/AkmlSql.Engine.Tests              # handlers, refactors (R8), snippets (R7)
dotnet test tests/AkmlSql.Shell.Shared.Tests        # shell .projitems sources (wiring)
dotnet test tests/AkmlSql.Core.Tests                # new message round-trips, config
```

UI-bound paths (DTE, pipe, editor margins, popups) have no unit test — verify live (below).

## Per-phase validation (acceptance)

- **P1 — Formatting**: pick a built-in style, enable GROUP-BY-per-line + leading commas + a CASE/CTE/CREATE-TABLE option + a max line width; Format SQL on a representative script; confirm every option shows. Run each standalone action; confirm isolated effect. Confirm a syntax-error query is preserved with a message. *(Gate: the pipeline idempotency + semantic-equivalence tests pass; Format SQL < 200 ms typical — SC-011.)*
- **P2 — IntelliSense**: hover a table/view/proc/column/function → metadata tooltip; type a function `(` → signature help tracks the parameter; declare `#t` then `#t.` → its columns; toggle "enable suggestions"/"auto-trigger" off → suggestions stop; open the column picker → multi-insert. *(Gate: completion p95 < 100 ms — SC-011.)*
- **P2 — Snippets**: type a built-in shortcode in SSMS **and** VS → it expands with caret honored; import a `.sqlpromptsnippet`; create-from-selection; surround a selection.
- **P3 — Analysis**: add a `.casettings` disabling a rule under a folder; open a file there → no squiggle for that rule (matches CLI); manage a rule in the dialog; toggle analysis off/on.
- **P3 — Refactoring**: Smart Rename a column referenced by procs/views → reviewable DB-wide script updates all; Find Invalid Objects lists broken objects; Inline a proc; INSERT→UPDATE.
- **P3 — Tabs/History**: a database→environment rule colors a tab on any server; "remove older than"; retention keeps the latest version + executions; disable auto-trim in Options.
- **P3 — Options/Platform**: every in-scope setting has a control (no config-only); Command Palette finds a DB object; Bulk Format opens.

## Measure done (SC-010)

Re-run the audit lens over `doc/_Prompt-Gap/` for the rows this feature targets and confirm they move 🟡/❌ → ✅. The feature is complete when its in-scope targeted rows reach parity (AI + licensing remain out of scope).

## Live install (per CLAUDE.md)

Deploy the built shell to `…/Extensions/AkmlSql/` for the host (SSMS 22 path is under `Release/Common7/IDE/Extensions/AkmlSql/`), clear the MEF/component cache for that host, and restart it. Copy the **whole** engine publish output (never a partial DLL swap — `AkmlSql.*` is auto-versioned per build).

## Git

No `git add/commit/push` without the user's explicit approval. Each downstream task's "Commit" step is **summarize-and-ask**.
