# AKML SQL

SQL completion, formatting, static analysis, refactoring, query history and optional AI assistance for **SQL Server Management Studio 22** and a **browser SQL workspace**, backed by one self-contained .NET 10 engine. The SSMS extension works with SQL Server; the browser workspace works with SQL Server and PostgreSQL through the engine.

**Author** Mohamed Khamis · **License** MIT ([LICENSE.txt](LICENSE.txt), [third-party notices](THIRD-PARTY-NOTICES.txt)) · **Latest release** 1.26.1103.1811 (2026-11-03) on the [download page](https://akml.khamis.work/download) · **Docs** [akml.khamis.work/docs](https://akml.khamis.work/docs)

This README describes the source on `master` on **2026-11-03** (after PR #259). A downloaded installer may contain an earlier build. Versions are stamped as `1.YY.MMDD.HHmm` (UTC+2) by [Directory.Build.props](src/Directory.Build.props) and [build.ps1](build.ps1).

---

## Contents

- [Getting started](#getting-started)
- [What you get](#what-you-get)
- [Optional AI](#optional-ai)
- [How it works](#how-it-works)
- [Repository map](#repository-map)
- [Build from source](#build-from-source)
- [Testing](#testing)
- [Current work and known gaps](#current-work-and-known-gaps)
- [Documentation](#documentation)

## Getting started

### SSMS 22

1. Install SQL Server Management Studio 22 on Windows.
2. Run the AKML SQL installer and select the detected SSMS 22 installation. Setup deploys the extension and its engine and clears SSMS's component cache; end users do not need the .NET SDK.
3. Open SSMS and use the **AKML SQL** menu under *Tools* for formatting, styles, history, refactoring, options and AI configuration.

SSMS 22 is the only desktop extension target. Visual Studio is used to build the extension, not to host it. See [Getting started](doc/topics/getting-started.md) and [deployment](doc/deployment.md).

### Browser workspace

1. Select **Web edition → Standalone Windows service (recommended)** in setup. SSMS and IIS are not required for this hosting mode.
2. Choose localhost or LAN access and open the address shown by setup. A new localhost installation defaults to **`http://localhost:47291/`**; upgrades keep a previously chosen port.
3. Wait for the engine status to show **Live**, then use **Connect to database** to select SQL Server or PostgreSQL and test the connection.

`localhost` refers to the computer running the browser. For access from another computer, select LAN hosting and use setup's HTTPS hostname, certificate trust instructions and pairing PIN. Windows authentication to SQL Server uses the engine's Windows identity, which can differ from the browser user's identity.

| Hosting choice | How the workspace is served |
| --- | --- |
| Standalone Windows service | `AkmlSqlWebEngine` serves the web files and the WebSocket bridge on one port. IIS is not required. |
| IIS | IIS serves the web files; the engine service provides database access through its bridge. Setup checks that the required IIS features and tools are available. |
| Don't host (files only) | Installs the files and the engine service for a separately managed static host. |

Silent install, for example:

```text
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /TARGETS=ssms22 /WEB_HOST=SERVICE /WEB_EXPOSURE=LOCALHOST
```

Setup records the address and diagnostics in `%ProgramData%\AKML SQL Web\INSTALL-SUMMARY.txt`; the engine configuration is in the same folder's `config.json`. Use **Repair AKML SQL Web hosting** from the Start menu if that address stops responding. Changing the scheme, hostname or port changes the browser origin: saved browser connections and tabs do not move to the new origin on their own, so export settings before switching hosts. Updates are checked daily and at sign-in; nothing installs without your click. Desktop settings live in `%AppData%\AKML SQL\config.json`, logs under `%AppData%\AKML SQL\logs`.

See [browser setup](doc/topics/browser-setup.md) for hosting, pairing and connection instructions.

## What you get

| Area | Current implementation and scope |
| --- | --- |
| **SQL completion** | Schema-aware objects and columns, aliases, keywords, variables, procedure parameters, snippets, JOIN and JOIN … ON foreign-key assistance, GROUP BY suggestions, quick info and signature help, in a SQL Prompt-style popup. Browser completion uses the connected engine, with a smaller keyword and cached-schema fallback when disconnected. |
| **Formatting and styles** | A seven-stage pipeline (parse → annotate → layout → casing → emit → validate → idempotency), T-SQL document, selection and bulk formatting, Unformat, `.akmlstyle` profiles, SQL Prompt `.sqlpromptstylev2` import and export, style editors with live preview, and the SQL Prompt format actions (expand wildcards, qualify names, semicolons, aliases, old-style joins …). Unsupported imported settings are reported; full SQL Prompt equivalence is not claimed. |
| **Static analysis** | **132 T-SQL rules** and **52 PostgreSQL rules** across Performance, Best Practices, Security, Style, Design, Deprecated, Execution and Naming; severities, per-project `.casettings` and inline `-- akml-disable` suppressions; the SSMS Error List. |
| **Refactoring and navigation** | Smart Rename, Script as ALTER, Inline EXEC, Inline Stored Procedure, Convert INSERT to UPDATE, Find Invalid Objects, light-bulb fixes, definition and reference navigation, object search. Availability differs by host and dialect; the [runtime SSMS menu](src/AkmlSql.Shell.Shared/Commands/AkmlMenuTable.cs) decides what the desktop offers (Text to SQL, AI Optimize, AI Index Analysis, Generate CRUD Procedures, Find in Results Grid and Split Table are not on it). |
| **Browser query workspace** | Query tabs with their own engine sessions and connections, a database selector, execution and cancellation, scripts run batch by batch, result grids with sorting, copy and export, editing by key with per-type validation, a schema explorer, snippets, and restoration of tabs and connections. Temporary tables and session settings survive successive runs in the same session. |
| **SQL History** | Engine-backed history of every run, grouped by query session, with search syntax, versions, compare and restore on start. |
| **Desktop tools** | Tab restore, environment-based tab colouring, command palette, snippets (personal, team, built-in; surround-with) and safety prompts for destructive statements. |
| **PostgreSQL** | Browser connections, schema browsing, completion, quick info and signatures, execution one statement at a time, formatting, analysis, snippets and grid editing. PostgreSQL language services run in the engine; they are not bundled into the browser or exposed as an SSMS PostgreSQL extension. |
| **Appearance** | Light, Dark and High Contrast themes that follow the host; shared theme tokens generate the web and product-site CSS. |
| **Command-line tools** | A formatter (files, directories or standard input) and a static analyzer (batch analysis, report and check modes), as single-file executables. |

The rule counts are the distinct implementations in [T-SQL rules](src/AkmlSql.Analysis/Rules/) and [PostgreSQL rules](src/AkmlSql.PostgreSql/Analysis/Rules/).

## Optional AI

The SQL workflow does not require AI. Implemented surfaces are chat, Explain, Fix and inline ghost text; the web action panel also offers Text to SQL, Optimize and Index Analysis. Up to 20 named agents can be configured, with per-feature assignments and a fallback order. Review the context sent to a provider before using it.

The [desktop and engine provider factory](src/AkmlSql.AI/Providers/AiProviderFactory.cs) supports Anthropic, OpenAI, Azure OpenAI, Gemini, Kimi, Ollama, LM Studio and custom OpenAI-compatible endpoints. The [browser factory](src/AkmlSql.Web/Services/IAiClientFactory.cs) keeps its own origin allow-list: browser-direct Anthropic, Gemini, Ollama and LM Studio are enabled, while OpenAI and Azure are not reachable from the browser (their APIs do not permit cross-origin calls) and are surfaced as a notice. Keys never leave your machine. See [AI assistance](doc/topics/ai-assistance.md) and [local-provider browser setup](doc/WEB/ai-local-provider-cors.md).

## How it works

```text
 SSMS 22 ──named pipe (MessagePack)──┐
                                     ├──► AkmlSql.Engine (.NET 10, win-x64)
 Browser ──WebSocket bridge──────────┘      parsers · schema cache · completion · formatter
                                            analysis · refactoring · execution · history · AI
```

| Component | Runtime and responsibilities |
| --- | --- |
| SSMS shell | .NET Framework 4.7.2, x64, inside SSMS 22; WPF UI, commands and editor integration. |
| Engine | .NET 10, `win-x64`, self-contained; parsing (ScriptDom for T-SQL, libpg_query for PostgreSQL), database connections, the schema cache (a fast phase for names, a background phase for columns and keys, change detection by checksum), execution, history and engine services. Published **without** single-file packaging or trimming, for SqlClient and the native parser. |
| Browser workspace | Blazor WebAssembly and CodeMirror 6. T-SQL formatting and analysis run in the browser through the shared libraries; execution and PostgreSQL language services need a connected engine. Browser state lives in IndexedDB. |
| Product website | A separate .NET 10 Blazor application (static server-side rendering) for product pages, downloads, documentation, feedback and administration. |

The desktop shell talks to its engine over the per-process named pipe `akmlsql-engine-{user-SID}-{shell-PID}` with owner-only access. [FrameProtocol](src/AkmlSql.Core/Ipc/FrameProtocol.cs) frames each message as a 4-byte big-endian length, a 4-byte XOR-rotate checksum and a MessagePack payload of at most 16 MiB. The browser talks over a WebSocket with the same MessagePack messages; in standalone mode `AkmlSql.Engine.exe --web --config <path>` also serves the configured `bridge.webRoot` on the bridge listener, and LAN mode uses HTTPS/WSS and pairing. The shared libraries (`AkmlSql.IntelliSense`, `AkmlSql.Formatting`, `AkmlSql.Analysis`, `AkmlSql.AI`) are compiled into both the engine and the web app, so the browser formats and analyses with the same code the IDE uses.

See [architecture](doc/architecture.md), [IPC API](doc/ipc-api.md) and [bridge security](doc/m3-security.md).

## Repository map

[AKML-SQL.slnx](AKML-SQL.slnx) contains **14 source projects, 15 test projects and one reference-generator project**. `Shell.Shared` is imported shared source; the Inno Setup installer is not a `.csproj`.

```text
src/
  AkmlSql.Core/             Shared settings, models, text helpers and IPC (netstandard2.0 + net10.0)
  AkmlSql.Engine/           Database access, RPC handlers, history and web hosting (net10.0, win-x64)
  AkmlSql.IntelliSense/     Shared T-SQL parsing and completion
  AkmlSql.Formatting/       T-SQL formatter, profiles, SQL Prompt layout and style compatibility
  AkmlSql.Analysis/         T-SQL analysis engine and rules
  AkmlSql.PostgreSql/       Engine-side PostgreSQL parser and language services
  AkmlSql.AI/               Shared AI prompts, privacy and provider adapters
  AkmlSql.Shell.Shared/     Shared WPF and editor source imported by SSMS and the shell tests
  AkmlSql.Ssms22/           SSMS 22 extension (net472, x64, VS SDK 17.14)
  AkmlSql.Web/              Blazor WebAssembly SQL workspace
  AkmlSql.Web.Shared/       Browser contracts (netstandard2.0)
  AkmlSql.Site/             Public website and administration
  AkmlSql.Formatter/        Formatting CLI
  AkmlSql.Analyzer/         Static-analysis CLI
  AkmlSql.Updater/          Windows updater
  AkmlSql.Installer/        Inno Setup 7 scripts and PowerShell hosting helpers
tests/
  AkmlSql.*.Tests/          Unit, integration, browser and installed-UI test projects
  format-parity/            T-SQL formatting inputs and regression goldens
  completion-corpus/        T-SQL completion cases and exclusions
  pg-format-corpus/         PostgreSQL formatting inputs and regression goldens
  pg-completion-corpus/     PostgreSQL completion cases
tools/pg-reference/         PostgreSQL reference generator
scripts/                    Build support, browser runner, PostgreSQL test cluster, theme generation, deployment
doc/                        User guides and engineering documentation (WEB/ holds the web milestones and UI test reports)
docs/                       Theme tokens and the WPF theming guide
specs/                      Feature requirements, task ledgers and verification evidence (001–045)
```

## Build from source

Use Windows x64, the .NET 10 SDK, Visual Studio or MSBuild with the .NET desktop build tools and the .NET Framework 4.7.2 targeting pack, Inno Setup 7 for the installer, and Node.js for the JavaScript tests. Two rules apply before the first build:

- Build with **full MSBuild**, never `dotnet build`, for anything that includes the SSMS extension: the VSSDK targets need `CodeTaskFactory`. Solution-wide builds work; the earlier VSCT cross-contamination workaround is no longer needed.
- The engine is published **without** single-file or trimming: its SQL client and the PostgreSQL parser carry native libraries.

From the repository root in PowerShell:

```powershell
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -prerelease -products * `
    -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
    Select-Object -First 1
if (-not $msbuild) {
    # Path used for the recorded builds; adjust for your installation if necessary.
    $msbuild = 'C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe'
}
if (-not (Test-Path -LiteralPath $msbuild)) { throw 'Full MSBuild was not found.' }

& $msbuild AKML-SQL.slnx -t:Restore -p:Configuration=Release -v:quiet
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
& $msbuild AKML-SQL.slnx -t:Build -p:Configuration=Release -m -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
```

Packaging:

```powershell
.\build.ps1
# Output: src\AkmlSql.Installer\Output\AKMLSQLSetup.exe
```

[build.ps1](build.ps1) checks the generated themes, refreshes documentation metadata, publishes the payloads (emptying the publish folders first), builds the SSMS extension, runs the Core, Engine, Formatting, Web and Site test projects and compiles the installer. `-SkipTests` omits the suites; `-InstallerOnly` requires existing Release payloads; `-DeploySite` performs the separate IIS publication and release-staging workflow described in [deployment](doc/deployment.md). The script discovers MSBuild through the VS 18 Enterprise path and then `vswhere`; on a machine where Visual Studio is registered as incomplete, pass the explicit path.

Local development:

```powershell
dotnet run --project src/AkmlSql.Web/AkmlSql.Web.csproj
# Launch profile: https://localhost:52118 and http://localhost:52119

dotnet run --project src/AkmlSql.Site/AkmlSql.Site.csproj --launch-profile http
# Public-site preview: http://localhost:5180
```

The web development server hosts the browser app only; start or connect an engine separately for database operations (a Debug engine can run sandboxed with `AKML_APP_DATA_ROOT`; the recipe is in [CLAUDE.md › Build Commands](CLAUDE.md#build-commands)). Publishing the web project alone does not install a Windows service. Every build gotcha we have met is written down in [CLAUDE.md › Build Gotchas](CLAUDE.md#build-gotchas).

## Testing

After a Release solution build:

```powershell
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj -c Release --no-build
dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj -c Release --no-build
dotnet test tests/AkmlSql.PostgreSql.Tests/AkmlSql.PostgreSql.Tests.csproj -c Release --no-build
dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj -c Release --no-build
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Release --no-build     # live tests need SQL Server on localhost
dotnet test tests/AkmlSql.Site.Tests/AkmlSql.Site.Tests.csproj -c Release --no-build
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj -c Release --no-build -- xUnit.ParallelizeTestCollections=false
dotnet test tests/AkmlSql.Installer.Tests/AkmlSql.Installer.Tests.csproj -c Release --no-build --filter 'Category!=InstallerSmoke'
node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
```

- [scripts/run-playwright.ps1](scripts/run-playwright.ps1) runs the browser suites: it builds the Debug prerequisites, installs the browser dependencies and runs serially. SQL and service cases have extra prerequisites, and reconnect cases can restart the installed `AkmlSqlWebEngine` service. Native keyboard and installed-SSMS checks need an unlocked interactive desktop.
- Live PostgreSQL tests use `AKML_PG_TEST_CONNECTION`; [scripts/pg-test-server.ps1](scripts/pg-test-server.ps1) starts a disposable cluster on `localhost:55432`.
- Installer install, repair and uninstall smoke matrices run only on disposable Windows VMs ([hosting task ledger](specs/045-web-standalone-hosting/tasks.md)); the helper suite excludes them.
- Corpora keep the formatter and completion honest: `tests/format-parity` (SQL Prompt parity goldens per corpus script and style), `tests/completion-corpus` (1,376 cases with a pass-rate gate) and their PostgreSQL counterparts. Regenerate goldens only on purpose (`AKML_UPDATE_PARITY_GOLDEN=1`).

### Recorded verification

| Check | Result | Date |
| --- | --- | --- |
| Core | 1,585 passed, 1 skipped | 2026-11-03 |
| Formatting | 1,676 passed | 2026-11-03 |
| PostgreSql | 2,264 passed | 2026-11-03 |
| Web | 2,093 passed; one timer-based toast test failed under machine load and passed 3 of 3 alone | 2026-11-03 |
| Shell, serial | 999 passed; four WPF AI-chat tests failed under machine load and passed 9 of 9 alone | 2026-11-03 |
| Engine | 2,710 passed, 132 skipped (live servers); the historical performance baseline fails on the build machine by timing drift, on untouched master too | 2026-11-03 |
| JavaScript | 305 passed | 2026-11-03 |
| SSMS extension | Release build with full MSBuild succeeded; pre-existing threading-analyzer warnings only | 2026-11-03 |
| Browser checks | SQL Server and PostgreSQL grid validation, formatting and explorer checks against a Debug web app and a sandboxed Debug engine; see the [UI test report](doc/WEB/ui-test-report-2026-11-03.md) | 2026-11-03 |
| Site | 917 passed | 2026-10-27 |
| Installer helpers | 129 passed, smoke tests excluded | 2026-10-27 |
| Browser suites (Playwright) | 158 distinct passes, 62 skipped, no remaining failures across the recorded runs | 2026-10-27 |
| Runtime checks | Isolated Windows-service query cycle and crash recovery, installed SSMS format and execute, installed web `SELECT 42` and reload after the hosting repair | 2026-10-27 |

Rows dated 2026-10-27 come from the [spec 045 baseline](specs/045-web-standalone-hosting/baseline.md); the 2026-11-03 rows from the UI test report's full cycle. None of them is a complete acceptance matrix.

## Current work and known gaps

Specs 001–045 are merged to `master`. The task counts are checkbox counts from the ledgers, not a claim of complete certification:

| Feature | Checked / total tasks | Remaining scope recorded by the ledger |
| --- | --- | --- |
| [041 — Web workspace UX](specs/041-web-ssms-ux-parity/tasks.md) | 216 / 216 | See its baseline for the precise manual checks and study limits. |
| [042 — PostgreSQL](specs/042-postgresql-support/tasks.md) | 181 / 182 | T129: the matching-build SSMS regression check. |
| [043 — Query tabs and polish](specs/043-web-query-tabs-polish/tasks.md) | 100 / 104 | T060, T088, T098, T104: keyboard acceptance, user screenshots, Grammarly checks and the final owner walkthrough. |
| [044 — Product website](specs/044-site-product-studio/tasks.md) | 13 / 13 | Implementation and isolated-preview verification recorded in its baseline. |
| [045 — Standalone web hosting](specs/045-web-standalone-hosting/tasks.md) | 95 / 108 | Twelve VM and owner acceptance tasks and T107's strict before/after comparison. |
| [UI scan 2026-11-03](doc/WEB/ui-test-report-2026-11-03.md) (PR #259) | merged | The SSMS `dbo.` completion fix still wants a live SSMS confirmation; the web explorer's folders are not yet exposed as a tree to assistive technology; format on paste is not wired in SSMS 22. |

For spec 045, the strict comparison found no missing tests or newly failing cases, but fails its gate because two previously passing Core visual-reference checks skip on a checkout without the ignored SQL Prompt reference pack. Detailed scope and evidence are in [progress](doc/progress.md) and each feature's task ledger and baseline.

## Documentation

| Reference | Contents |
| --- | --- |
| [Getting started](doc/topics/getting-started.md) / [Browser setup](doc/topics/browser-setup.md) | Installation and user workflows |
| [Architecture](doc/architecture.md) / [IPC API](doc/ipc-api.md) / [Bridge security](doc/m3-security.md) | Components, runtime boundaries and protocol contracts |
| [Configuration](doc/configuration.md) / [Deployment](doc/deployment.md) | Settings, hosting, packaging, upgrades and diagnostics |
| [Formatting](doc/formatting.md) / [Analysis rules](doc/analysis-rules.md) | Formatter behaviour, profiles and every rule |
| [PostgreSQL reference](doc/postgresql-reference.md) | How the PostgreSQL reference is generated and used |
| [Web engineering docs](doc/WEB/00-INDEX.md) | Web milestones, quickstarts, release notes and UI test reports |
| [WPF theming](docs/wpf-theming.md) / [Theme tokens](docs/theme-tokens.json) | Shared theme definitions and generated CSS |
| [CLAUDE.md](CLAUDE.md) / [PROJECT_MEMORY.md](PROJECT_MEMORY.md) | Contributor conventions, build gotchas and dated source orientation |
| [Progress](doc/progress.md) / [Specifications](specs/) | Implementation history, acceptance results and open work |

## License

MIT, copyright © 2026 Mohamed Khamis. See [LICENSE.txt](LICENSE.txt) (also [distributed with the installer](src/AkmlSql.Installer/LICENSE.txt)) and [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
