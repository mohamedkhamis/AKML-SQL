# AKML SQL

AI-powered SQL development assistance for SQL Server Management Studio and Visual Studio. AKML SQL replicates and extends the Redgate SQL Prompt feature set, runs in SSMS 22 and Visual Studio 2026, and ships a self-contained `.NET 10` engine for IntelliSense, formatting, refactoring, and static analysis.

**Author**: Mohamed Khamis · **License**: MIT · **Version**: 1.0.0

---

## At a glance

| Area | Highlights |
|---|---|
| **IntelliSense** | 9 completion providers (Column / Alias / Object / Keyword / Snippet / Variable / JOIN / QuickInfo / Signature), fuzzy matching, dot-trigger, schema-aware ranking, custom SQL-Prompt-style popup with type-coded icon badges + Ctrl-held semi-transparency |
| **Code formatting** | 7-stage pipeline (parse → annotate → layout → cast → emit → validate → idempotency), 21 format commands, `.akmlstyle` profile system, **SQL Prompt `.sqlpromptstylev2` XML round-trip** (import + export) |
| **Format Styles editor** | Modal three-column WPF editor with style list / settings tree / live preview, type-driven controls, schema fetched via IPC, 100 ms debounced preview, FR-023 unsupported-setting affordance |
| **Snippets** | Personal + team + built-in folders, context-aware filtering, surround-with chord |
| **Static analysis** | 130+ rules across 8 categories (Performance / BestPractices / Security / Style / Design / Deprecated / Execution / Naming), inline suppressions, per-project `.casettings`, native VS Error List integration |
| **Refactoring** | Heavyweight (Smart Rename, Parameterize Values, Convert Temp Table) + 9 lightweight ops (Expand INSERT Columns, Convert Old-Style Joins, Encapsulate BEGIN/END, etc.) |
| **SQL History** | Full crash-safe history with three-panel UI (queries / versions / preview), full-text search, star/open/closed filters, syntax-highlighted code preview |
| **Tab Coloring** | Environment-based per-server colouring (tab background + status bar tint + floating window border), wildcard pattern rules, gradient toggle. See `specs/020-sqlprompt-visual-parity/tab-coloring-audit.md` for the parity audit vs SQL Prompt §5.1 |
| **AI assistance** | Text-to-SQL, Explain, Fix, Optimize, Index Analysis, Chat, Ghost Text (inline autocomplete), schema-aware prompting |
| **Theme system** | Centralised `ThemeTokens` / `ThemeRegistry` / `HostThemeWatcher` (spec 016) with Light / Dark / HighContrast palettes — 25+ brush tokens across Surface / Text / Border / Accent / Status / Editor / Chat / IconBadge / TabColor / History families |

## Project structure

```text
AKML-SQL.slnx                          # Solution file (.slnx format)
src/
  AkmlSql.Core/                        # Shared library (netstandard2.0 + net10.0)
  AkmlSql.Engine/                      # Out-of-process IntelliSense / format / analysis engine (.NET 10, win-x64, trimmed)
  AkmlSql.Formatting/                  # Formatter pipeline + profile system (.NET 10)
  AkmlSql.Analyzer/                    # CLI SQL static analyzer (.NET 10)
  AkmlSql.Shell.Shared/                # Shared project (.projitems) for the shell extensions
  AkmlSql.Ssms22/                      # SSMS 22 extension (net472, x64, VS SDK 17.14.x)
  AkmlSql.VS2026/                      # VS 2026 extension (net472, x64, VS SDK 17.14.x)
  AkmlSql.Updater/                     # Self-contained updater (.NET 10, win-x64, trimmed)
  AkmlSql.Installer/                   # Inno Setup 7 installer scripts
tests/
  AkmlSql.Core.Tests/                  # xunit (net10.0) — Core + IPC + Tabs + Theme + Format
  AkmlSql.Engine.Tests/                # xunit (net10.0) — engine handlers + parser + analysis rules + refactoring
  AkmlSql.Formatting.Tests/            # xunit (net10.0) — pipeline + profile + SqlPrompt import/export
  format-parity/                       # SQL Prompt parity corpus + golden outputs (scaffold; population deferred to corpus PR)
doc/                                   # All project documentation — architecture, ipc-api, configuration, formatting, analysis-rules, progress, deployment
docs/                                  # WPF theming contributor guide
specs/                                 # /speckit feature specifications
```

## Architecture

AKML SQL runs as **two processes** communicating over a named pipe:

| Process | Target | Role |
|---|---|---|
| **Shell extension** | .NET Framework 4.7.2 inside SSMS / VS | UI, command handlers, editor integration |
| **Engine** | .NET 10 self-contained, win-x64, trimmed | Parsing (TSql170Parser), schema cache, completion, formatting pipeline, analysis, refactoring |

Per-shell-process named pipe (`akmlsql-engine-{user-SID}-{shell-PID}`) with owner-only ACL; MessagePack frame format (`[4-byte length][4-byte XOR CRC][MessagePack(RpcMessage)]`); 16 MB max frame.

See [doc/architecture.md](doc/architecture.md) for the full component map, startup sequence, and data flows.

## Building

Shell projects MUST be built individually with full MSBuild (not `dotnet build`) — VSSDK requires CodeTaskFactory:

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"

# Engine first
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64

# Each shell project individually (avoid VSCT cross-contamination from solution-wide builds)
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build -p:Configuration=Release -v:minimal

# Tests
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj
dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj

# Installer
"/c/Program Files/Inno Setup 7/ISCC.exe" src/AkmlSql.Installer/AkmlSqlSetup.iss
```

Full build gotchas (VSPackage.resx, vsixmanifest schema differences, MEF cache clearing, etc.) live in [CLAUDE.md](CLAUDE.md#build-gotchas).

## Documentation

| File | Content |
|---|---|
| [doc/architecture.md](doc/architecture.md) | Component map, startup sequence, data flows, design decisions |
| [doc/ipc-api.md](doc/ipc-api.md) | Every IPC message type, request/response schemas, frame format |
| [doc/configuration.md](doc/configuration.md) | `config.json` schema, `.casettings`, persistence markers, logging |
| [doc/formatting.md](doc/formatting.md) | Pipeline stages, profile schema, SQL Prompt round-trip, format actions |
| [doc/analysis-rules.md](doc/analysis-rules.md) | All 130+ analysis rules with descriptions and severities |
| [doc/deployment.md](doc/deployment.md) | Install paths per host, MEF cache clearing, troubleshooting |
| [doc/progress.md](doc/progress.md) | Per-spec development log with phase tables, clarifications, deferred follow-ups |
| [docs/wpf-theming.md](docs/wpf-theming.md) | WPF theme token contributor guide |
| [CLAUDE.md](CLAUDE.md) | Project conventions, build gotchas, WPF UI rules, code conventions |
| [specs/](specs/) | `/speckit` feature specifications (one folder per spec) |
| [doc/SQL-PROMPT/](doc/SQL-PROMPT/) | Canonical Redgate SQL Prompt visual contract — design tables, hex codes, SVG mockups |
| [doc/WEB/](doc/WEB/) | Spec 021 web edition — milestone PRDs, quickstart guides per milestone, AI-key wrapping contract |

## Web edition (spec 021)

A Blazor WASM web edition that ships alongside the IDE plugins. Browse to a local
URL, format / analyse SQL entirely in the browser, optionally pair with a local
engine for live IntelliSense, optionally bring your own AI key for Text-to-SQL /
Explain / Fix / Optimize.

| Surface | Detail |
|---------|--------|
| Editor + format + analyse | CodeMirror 6 + the same `FormatterPipeline` + `AnalysisEngine` the IDE plugins run -- in-process inside the WASM. |
| Bridge | WebSocket between browser and the local engine, MessagePack frames, capability-gated. PIN pairing on first LAN connect; bearer tokens wrapped at rest with Web Crypto. |
| Schema cache | IndexedDB store keyed by `(serverCanonicalIdentity, database)`. LRU eviction. Two DNS aliases of the same SQL Server share one entry. |
| AI | Direct fetch from the browser to OpenAI / Anthropic / Gemini / Azure / Ollama / LM Studio. Per-provider origin allow-list refuses non-listed fetches at the factory layer. API keys wrapped with non-extractable AES-GCM 256. |

See [doc/WEB/00-INDEX.md](doc/WEB/00-INDEX.md) for the full spec 021 documentation set, including per-milestone quickstart guides at [quickstart-m2.md](doc/WEB/quickstart-m2.md), [quickstart-m4.md](doc/WEB/quickstart-m4.md), [quickstart-m5.md](doc/WEB/quickstart-m5.md), and [quickstart-m6.md](doc/WEB/quickstart-m6.md).

## Status

| Spec | Status |
|---|---|
| 015 — Bug fixes & polish | Merged to master |
| 016 — WPF theme refresh | Merged to master |
| 017 / 018 — Options dialog phases 1 + 2 | Merged to master |
| 019 — Phase 10 parity-closure design docs | Merged to master |
| 020 — SQL Prompt visual parity | Merged to master (77 / 106 tasks; 29 deferred) |
| 021 — Web edition | On `021-web-edition` branch (111 / 150 tasks; 39 deferred — Playwright, IIS integration, manual audits) |

Deferred from spec 020 — see `specs/020-sqlprompt-visual-parity/tasks.md` for the full list:

- Formatter pipeline gap closures (T074–T084) — 11 layout rules
- Parity corpus + tests (T071–T073) — needs Redgate install for golden generation
- Format Styles editor menu wire + Options dialog re-skin (T044–T048, T059)
- Manual product-running audits (T098–T100, T105) — DPI, a11y, screenshot review, end-to-end smoke

## License

MIT. See `LICENSE` (if present) for terms.
