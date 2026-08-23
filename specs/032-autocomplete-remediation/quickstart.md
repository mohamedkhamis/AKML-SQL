# Quickstart — Autocomplete Campaign Remediation

How to build, test, deploy, and validate this feature. No desktop shell builds are needed (engine-only + web); commands follow `CLAUDE.md`.

## Prerequisites

- .NET 10 SDK (engine, libraries, tests, web).
- For live web verification: the dev IIS site `AkmlSqlWeb` (port 8083), the engine service `AkmlSqlWebEngine`, and the `Northwind_AutoTest` sandbox DB (kept from the campaign; `NT AUTHORITY\SYSTEM` has `db_owner`). SSMS 22 for the desktop smoke pass.

## Build

```bash
# Engine + libraries (completion/formatter fixes live here)
dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release

# Web (Blazor WASM — editor JS, profile store, status components)
dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Release
```

If the vendored CodeMirror bundle needs a new export (e.g. `acceptCompletion` missing): rebuild via `tools/codemirror` (esbuild) → outputs to `src/AkmlSql.Web/wwwroot/lib/codemirror`. Never reference a CDN.

## Test (TDD — failing test first, use the campaign repro SQL verbatim)

```bash
# Inner loop, per cluster
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Release --filter "FullyQualifiedName~TokenBasedAliasExtractorTests"   # A
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Release --filter "FullyQualifiedName~CursorContextAnalyzerTests"     # B, C1/C2, G2/G3
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Release --filter "FullyQualifiedName~CteResolverTests"               # E
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Release --filter "FullyQualifiedName~Completion"                     # providers, engine, corpus gate
dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj -c Release                                                      # J1/J2 + goldens
dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj -c Release                                                                    # J3, W state logic
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj -c Release --filter "FullyQualifiedName~CompletionItem"                     # H1 wire round-trip
```

**Corpus gate** (SC-001/002/003 locally): `--filter "FullyQualifiedName~CorpusGateTests"` — runs the in-repo campaign corpus (`tests/completion-corpus/`) against a fake `Northwind_AutoTest`-shaped cache. Excluded cases (corpus-mistake / at-cap) are reported, not failed.

**Perf gate** (before/after the scope-resolution rework): `--filter "FullyQualifiedName~PerformanceBaselineTests"` (~13 min). A failure here is usually environmental drift — compare absolute numbers against SC-011-era targets before suspecting the change; re-baseline only with `AKML_UPDATE_BASELINE=1` and justification.

**Formatter goldens**: any `FormatParityTests` diff = review the behavior change; never regenerate goldens to make a fix pass.

## Deploy to the dev environment (live verification)

```bash
# Engine — FULL publish copy, never a partial DLL swap (auto-versioned assemblies break the pipe)
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64
# stop AkmlSqlWebEngine service → copy the WHOLE publish output to "C:\Program Files (x86)\AKML SQL\Engine" → start service

# Web — publish and deploy to IIS AkmlSqlWeb (port 8083); verify the publish actually includes AkmlSql.Web
dotnet publish src/AkmlSql.Web/AkmlSql.Web.csproj -c Release
```

After deploying, hard-refresh the browser (WASM + JS caching) and confirm the engine version in the status bar matches the build.

## Validate (acceptance, maps to SC-001…SC-009)

1. **Keystroke pass** (SC-004): in the web editor against `Northwind_AutoTest` — type `SELECT o` → `.` (popup with Orders columns), `UPDATE ` / `INSERT INTO ` / `DELETE FROM ` / `EXEC ` (popups), Tab-accept with popup open, Ctrl+Enter executes. **Note**: online completion uses the debounced session doc — wait ~2 s after typing before asserting in automated tests.
2. **Battery re-run** (SC-001/002/003/007): re-run the campaign harness (Playwright + in-page CM driver, corpus now in `tests/completion-corpus/`) → overall ≥ 95%, zero-item = 0, per-family ≥ 90%, passing families not regressed; engine log zero ERR/WRN.
3. **Formatting** (SC-005): 100-case battery 100% idempotent; FMTA-006 byte-stable on first pass; web shows Khamis Style + Collapsed with Khamis Style active by default.
4. **Connection honesty** (SC-009): F5 reload with a saved connection → either auto-restored (grids + live completions work) or the pill clearly shows not-connected; saved-connection selection displays the saved DB.
5. **Desktop smoke** (SC-008): deploy the same engine publish to the desktop install, open SSMS 22, spot-check P2/P5/P8/P16 shapes from the contract matrix; run the full desktop test suites.

## Cleanup owed after acceptance (from the campaign report)

`DROP DATABASE Northwind_AutoTest` + remove the SYSTEM grant note; delete `C:\Program Files (x86)\AKML SQL\Web\test-corpus\` and `.playwright-mcp/results-*.json` + screenshots.

## Git

No `git add/commit/push` without the user's explicit approval. Each downstream task's "Commit" step is **summarize-and-ask**.
