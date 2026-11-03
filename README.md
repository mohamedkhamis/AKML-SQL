# AKML SQL

SQL Prompt-class assistance for SQL Server Management Studio 22 and the browser: IntelliSense, formatting, static analysis, refactoring, SQL history and AI help, backed by one self-contained .NET 10 engine. The web edition adds PostgreSQL.

**Author** Mohamed Khamis · **License** MIT ([LICENSE.txt](LICENSE.txt)) · **Latest release** 1.26.1027.1505 (2026-10-27) on the [download page](https://akml.khamis.work/download) · **Docs** [akml.khamis.work/docs](https://akml.khamis.work/docs)

---

## Contents

- [What you get](#what-you-get)
- [Editions](#editions)
- [How it works](#how-it-works)
- [Install](#install)
- [Build from source](#build-from-source)
- [Run the tests](#run-the-tests)
- [Repository layout](#repository-layout)
- [Documentation](#documentation)
- [Status](#status)

## What you get

| Area | In short |
| --- | --- |
| **IntelliSense** | Schema-aware completion for columns, aliases, objects, keywords, snippets, variables, parameters, JOIN and JOIN … ON foreign keys, databases and GROUP BY; Quick Info and signature help; fuzzy matching; a SQL Prompt-style popup with type icons. |
| **Formatting** | A seven-stage pipeline (parse → annotate → layout → casing → emit → validate → idempotency) with `.akmlstyle` profiles, built-in styles, a style editor with live preview, and round-trip import/export of SQL Prompt `.sqlpromptstylev2` styles. Format Document / Selection / Bulk Format / Unformat and the SQL Prompt format actions (expand wildcards, qualify names, semicolons, aliases, old-style joins …). |
| **Static analysis** | 130+ rules across Performance, Best Practices, Security, Style, Design, Deprecated, Execution and Naming; inline `-- akml-disable` suppressions, per-project `.casettings`, the SSMS Error List, and 52 rules for PostgreSQL. |
| **Refactoring** | Smart Rename, Script as ALTER, Inline EXEC / stored procedure, Convert INSERT to UPDATE, Find Invalid Objects, light-bulb fixes. |
| **SQL History** | Crash-safe history of every run, grouped by query session, with versions, compare, search syntax and restore on start. |
| **Snippets** | Personal, team and built-in snippet folders, context-aware filtering, surround-with. |
| **AI assistance** | Explain, Fix, Chat, inline ghost text, Text-to-SQL, Optimize and Index Analysis, with up to 20 named agents (OpenAI, Anthropic, Gemini, Azure, Ollama, LM Studio) and schema-aware prompting. Keys stay on your machine. |
| **Tab colouring and themes** | Environment colours per server, and Light / Dark / High Contrast themes that follow the host. |

## Editions

| Edition | Where it runs | What it needs |
| --- | --- | --- |
| **SSMS 22 extension** | Inside SQL Server Management Studio 22 (.NET Framework 4.7.2, VS SDK 17.14) | The engine, installed next to it by the setup. |
| **Web edition** | Any modern browser, as a Blazor WebAssembly app served by the engine, a Windows service or IIS | A paired engine on the machine or the LAN for live schema, query execution, history and styles. Formatting and analysis also work entirely in the browser. |
| **Engine** | A separate `AkmlSql.Engine.exe` process (.NET 10, win-x64, self-contained) | Nothing else. SSMS talks to it over a named pipe; the browser over a WebSocket bridge. |
| **Command line** | `akmlsql-format` and the static analyzer, single-file executables | Scripts and CI. |

The web edition works with **SQL Server and PostgreSQL**: connect, complete, run scripts one statement at a time, edit results by key, format and analyse, with the dialect decided by the connection.

## How it works

```text
 SSMS 22 ──named pipe (MessagePack)──┐
                                     ├──► AkmlSql.Engine (.NET 10)
 Browser ──WebSocket bridge──────────┘      parser · schema cache · completion
                                            formatter · analysis · refactoring
                                            history · AI · SQL Server + PostgreSQL
```

- The shell and the browser never parse or connect to a database themselves. The engine owns the parsers (ScriptDom for T-SQL, libpg_query for PostgreSQL), the schema cache (a fast phase for names, a background phase for columns and keys, change detection by checksum), execution and history.
- The pipe is per user and per SSMS process with an owner-only ACL. The bridge is loopback by default; LAN exposure needs PIN pairing and uses bearer tokens.
- Shared libraries (`AkmlSql.IntelliSense`, `AkmlSql.Formatting`, `AkmlSql.Analysis`, `AkmlSql.AI`) are compiled into both the engine and the web app, so the browser formats and analyses with the same code the IDE uses.

[doc/architecture.md](doc/architecture.md) has the component map, startup sequence and data flows; [doc/ipc-api.md](doc/ipc-api.md) every message type.

## Install

1. Download `AKMLSQLSetup.exe` from the [download page](https://akml.khamis.work/download) and run it. It installs the SSMS 22 extension, the engine and the web edition, and clears SSMS's MEF cache for you.
2. Choose how the web edition is hosted: the built-in **service** (default), **IIS** when it is ready on the machine, or **files only**.
3. Open SSMS. The **AKML SQL** menu under *Tools* has Options, Format Styles, SQL History, Check for Updates and About. The web edition opens from the Start menu shortcut.

Silent install:

```text
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /TARGETS=ssms22 /WEB_HOST=SERVICE /WEB_EXPOSURE=LOCALHOST
```

Updates are checked daily and at sign-in; nothing installs without your click. Settings live in `%AppData%\AKML SQL\config.json`, logs under `%AppData%\AKML SQL\logs`, web machine state under `%ProgramData%\AKML SQL Web`. See [doc/deployment.md](doc/deployment.md) and [doc/configuration.md](doc/configuration.md).

## Build from source

Prerequisites: Visual Studio 2026 with the .NET 10 SDK and the Visual Studio extension development workload (for the VSSDK build tools), Inno Setup 7 for the installer, and SQL Server plus a local PostgreSQL for the live tests.

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/18/Enterprise/MSBuild/Current/Bin/MSBuild.exe"

# Whole solution (restore first)
"$MSBUILD" AKML-SQL.slnx -t:Restore -v:quiet
"$MSBUILD" AKML-SQL.slnx -t:Build -p:Configuration=Release -m -v:minimal

# Engine and updater publish (before the installer)
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64
dotnet publish src/AkmlSql.Updater/AkmlSql.Updater.csproj -c Release

# Installer
"/c/Program Files/Inno Setup 7/ISCC.exe" src/AkmlSql.Installer/AkmlSqlSetup.iss
```

Two rules worth knowing before the first build:

- The SSMS extension (`src/AkmlSql.Ssms22`) builds only with full MSBuild, never `dotnet build`: the VSSDK targets need CodeTaskFactory.
- The engine is published **without** single-file or trimming: its SQL client and the PostgreSQL parser carry native libraries.

`build.ps1` runs the whole release sequence (version stamp, publish, installer). Every gotcha we have met is written down in [CLAUDE.md › Build Gotchas](CLAUDE.md#build-gotchas).

## Run the tests

```bash
# One xunit project per library
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj
dotnet test tests/AkmlSql.PostgreSql.Tests/AkmlSql.PostgreSql.Tests.csproj
dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj       # live tests need SQL Server on localhost
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj

# Web edition JavaScript (Node 24 needs the glob, not the folder)
node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"

# Browser suites (Playwright; builds the Debug prerequisites, runs serially)
powershell -ExecutionPolicy Bypass -File scripts/run-playwright.ps1

# A disposable PostgreSQL cluster for the live PostgreSQL tests (localhost:55432)
powershell -ExecutionPolicy Bypass -File scripts/pg-test-server.ps1 -Action Start
```

Corpora keep the formatter and completion honest: `tests/format-parity` (SQL Prompt parity goldens, one per corpus script and style), `tests/completion-corpus` (1,376 autocomplete cases with a pass-rate gate), and their PostgreSQL counterparts `tests/pg-format-corpus` and `tests/pg-completion-corpus`. Regenerate goldens only on purpose: `AKML_UPDATE_PARITY_GOLDEN=1` for the formatter.

## Repository layout

```text
AKML-SQL.slnx                 Solution (.slnx)
src/
  AkmlSql.Core/               Shared contracts, IPC messages, config, text helpers (netstandard2.0 + net10.0)
  AkmlSql.Engine/             The out-of-process engine (net10.0, win-x64)
  AkmlSql.IntelliSense/       Completion providers, shared by engine and web
  AkmlSql.Formatting/         Formatter pipeline, .akmlstyle profiles, SQL Prompt layout and import/export
  AkmlSql.Analysis/           Analysis rules, shared by engine and web
  AkmlSql.PostgreSql/         PostgreSQL parser wrapper, completion, formatter, rules, reference (engine only)
  AkmlSql.AI/                 AI providers, agents and prompts, shared by engine and web
  AkmlSql.Shell.Shared/       Everything the SSMS extension does (shared project)
  AkmlSql.Ssms22/             The SSMS 22 VSIX (net472)
  AkmlSql.Web/                Blazor WebAssembly web edition
  AkmlSql.Web.Shared/         Web contracts (netstandard2.0)
  AkmlSql.Formatter/          akmlsql-format command line
  AkmlSql.Analyzer/           Static analyzer command line
  AkmlSql.Updater/            Self-contained updater
  AkmlSql.Installer/          Inno Setup 7 scripts
  AkmlSql.Site/               Product site: landing, download, docs ingested from doc/
tests/                        One xunit project per library, browser suites (Web.E2E, UiTests, Site.E2E), installer tests, corpora
doc/                          Architecture, IPC API, configuration, formatting, analysis rules, deployment, progress log, WEB/ milestone docs
docs/                         WPF theming guide and the design tokens
specs/                        Feature specifications 001–045 (Specify framework), each with its plan, tasks and evidence
scripts/                      Build, release, Playwright, PostgreSQL test cluster and site helpers
```

## Documentation

| Read this | For |
| --- | --- |
| [doc/architecture.md](doc/architecture.md) | Components, startup, data flows, design decisions |
| [doc/ipc-api.md](doc/ipc-api.md) | Every IPC and bridge message, frame format |
| [doc/configuration.md](doc/configuration.md) | `config.json`, `.casettings`, logging |
| [doc/formatting.md](doc/formatting.md) | Pipeline stages, profile options, SQL Prompt round-trip |
| [doc/analysis-rules.md](doc/analysis-rules.md) | All analysis rules with severities |
| [doc/postgresql-reference.md](doc/postgresql-reference.md) | How the PostgreSQL reference is generated and used |
| [doc/deployment.md](doc/deployment.md) | Install paths, hosting modes, cache clearing, troubleshooting |
| [doc/progress.md](doc/progress.md) | The development log, spec by spec |
| [doc/WEB/00-INDEX.md](doc/WEB/00-INDEX.md) | Web edition milestones, quickstarts, release notes and UI test reports |
| [docs/wpf-theming.md](docs/wpf-theming.md) | Theme tokens for new WPF UI |
| [CLAUDE.md](CLAUDE.md) | Conventions, build gotchas and the current state for contributors and coding agents |

## Status

Specs 001–045 are merged to `master`. The most recent work:

| Spec / change | What it brought |
| --- | --- |
| 041 · 043 | An SSMS-grade web workspace: results grid with editing, scripts run batch by batch, query tabs, one Connect to database dialog |
| 042 | PostgreSQL in the engine and the web edition |
| 044 · 045 | The product site redesign and standalone web hosting from the engine |
| UI scan 2026-11-03 ([report](doc/WEB/ui-test-report-2026-11-03.md)) | Completion, formatter, grid validation and SSMS dot-commit fixes, reviewed and merged (PR #259) |

Known open items are tracked in the spec `tasks.md` files and in [doc/progress.md](doc/progress.md): the SSMS `dbo.` completion fix still wants a live SSMS confirmation, the web explorer's folders are not yet exposed as a tree to assistive technology, and format-on-paste is not wired in SSMS 22.

## License

MIT. See [LICENSE.txt](LICENSE.txt).
