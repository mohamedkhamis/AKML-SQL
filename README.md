# AKML SQL

AKML SQL provides SQL completion, formatting, analysis, query history and optional AI assistance in **SQL Server Management Studio 22** and a **browser SQL workspace**. The SSMS extension works with SQL Server. The browser workspace supports SQL Server and PostgreSQL through an AKML engine.

**Author:** Mohamed Khamis · **License:** [MIT](src/AkmlSql.Installer/LICENSE.txt) · **Downloads:** [AKML SQL website](https://akml.khamis.work/download)

This README describes the source checked on **2026-10-27**, at commit `e420f866` on `045-web-standalone-hosting`. Implementation and verification status refer to this checkout; a downloaded installer may contain an earlier build. Versions are stamped as `1.YY.MMDD.HHmm` using a fixed UTC+2 offset by [Directory.Build.props](src/Directory.Build.props) and [build.ps1](build.ps1).

## Getting started

### SSMS 22

1. Install SQL Server Management Studio 22 on Windows.
2. Run the AKML SQL installer and select the detected SSMS 22 installation. Setup deploys the extension and its engine; end users do not need the .NET SDK.
3. Open SSMS and use the **AKML SQL** menu for formatting, styles, history, refactoring, options and AI configuration.

SSMS 22 is the only desktop extension target in the current source. Visual Studio is used to build the extension, but is not a supported AKML extension host. See [Getting started](doc/topics/getting-started.md) and [deployment](doc/deployment.md).

### Browser workspace

1. Select **Web edition → Standalone Windows service (recommended)** in setup. SSMS and IIS are not required for this hosting mode.
2. Choose localhost or LAN access and open the address shown by setup. A new localhost installation defaults to **`http://localhost:47291/`**. Upgrades can retain a different port; the server checked on 2026-10-27 uses **`http://localhost:59417/`**.
3. Wait for the engine status to show **Live**, then use **Connect to database** to select SQL Server or PostgreSQL and test the connection.

`localhost` refers to the computer running the browser. For access from another computer, select LAN hosting and use setup's HTTPS hostname, certificate trust instructions and pairing PIN. Windows authentication to SQL Server uses the engine's Windows identity, which can differ from the browser user's identity.

| Hosting choice | How the workspace is served |
| --- | --- |
| Standalone Windows service | `AkmlSqlWebEngine` serves the web files and WebSocket bridge on one port. IIS is not required. |
| IIS | IIS serves the web files; the engine service provides database access through its bridge. Setup checks that the required IIS features and tools are already available. |
| Don't host (files only) | Installs the files and engine service for a separately managed static host. |

Setup records the address and diagnostics in `%ProgramData%\AKML SQL Web\INSTALL-SUMMARY.txt`; the engine configuration is in the same folder's `config.json`. Use **Repair AKML SQL Web hosting** from the Start menu if that address stops responding. A running service alone does not establish that its HTTP endpoint works.

Changing the scheme, hostname or port changes the browser origin. Saved browser connections and tabs do not automatically move to the new origin; export settings before switching hosts. The current source fixes an IIS-to-service upgrade failure in which Windows PowerShell could leave the old bridge configuration active. The repair and remaining installer acceptance work are recorded in the [hosting baseline](specs/045-web-standalone-hosting/baseline.md).

See [browser setup](doc/topics/browser-setup.md) for hosting, pairing and connection instructions. The public AKML website provides downloads and documentation; the SQL workspace has its own installation address.

## Implemented capabilities

| Area | Current implementation and scope |
| --- | --- |
| SQL completion | Schema-aware objects and columns, aliases, keywords, variables, procedure parameters, snippets, JOIN/FK assistance, GROUP BY suggestions, quick info and signature help. Browser completion uses the connected engine, with a more limited keyword/cached-schema fallback when disconnected. |
| Formatting and styles | T-SQL document/selection formatting, unformatting, bulk formatting in SSMS, `.akmlstyle` profiles, SQL Prompt style import/export, and style editors with previews. Unsupported imported settings are reported; full SQL Prompt equivalence is not claimed. |
| Static analysis | **132 T-SQL rules** and **52 PostgreSQL rules**, each set spanning Performance, Best Practices, Security, Style, Design, Deprecated, Execution and Naming. Rule settings, severities and suppressions are configurable. |
| Refactoring and navigation | SSMS menu includes Smart Rename, Script as ALTER, Inline EXEC, Inline Stored Procedure, Convert INSERT to UPDATE, Find Invalid Objects, definition/reference navigation and object search. Availability differs by host and SQL dialect. |
| Browser query workspace | Multiple query tabs with separate engine sessions, database selection, execution/cancellation, result grids, export, eligible result editing, schema explorer, snippets and query/connection restoration. SQL Server temporary tables and session settings survive successive executions in the same live session. |
| History and desktop tools | Engine-backed query history with search and versions; SSMS tab restore, tab colouring, command palette, snippets and safety prompts. |
| PostgreSQL | Browser connections, schema browsing, completion, quick info, signatures, execution, formatting, analysis, snippets and eligible grid editing. PostgreSQL language services run in the engine; they are not bundled into browser WASM or exposed as an SSMS PostgreSQL extension. |
| Appearance | Light, Dark and High Contrast themes; shared theme tokens generate the web and product-site theme CSS. |
| Command-line tools | Separate formatter and analyzer projects. The formatter accepts files, directories or standard input; the analyzer supports batch analysis and report/check modes. |

The rule counts are the distinct implementations in [T-SQL rules](src/AkmlSql.Analysis/Rules/) and [PostgreSQL rules](src/AkmlSql.PostgreSql/Analysis/Rules/), checked on the date above. The [runtime SSMS menu](src/AkmlSql.Shell.Shared/Commands/AkmlMenuTable.cs) determines desktop command availability. Text to SQL, AI Optimize, AI Index Analysis, Generate CRUD Procedures, Find in Results Grid and Split Table are currently absent from that menu.

### Optional AI

The SQL workflow does not require AI. Implemented AI surfaces include chat, Explain, Fix and ghost text; the web action panel also offers Text to SQL, Optimize and Index Analysis. Configure credentials and review the context sent to a provider before using it.

The [desktop/engine provider factory](src/AkmlSql.AI/Providers/AiProviderFactory.cs) supports Anthropic, OpenAI, Azure OpenAI, Gemini, Kimi, Ollama, LM Studio and custom OpenAI-compatible endpoints. The [browser factory](src/AkmlSql.Web/Services/IAiClientFactory.cs) has a separate allowlist: this checkout enables browser-direct Anthropic, Gemini, Ollama and LM Studio, and disables browser-direct OpenAI/Azure. Browser and local-provider CORS configuration still applies. This describes the application's capability policy; providers were not tested live during this README audit.

See [AI assistance](doc/topics/ai-assistance.md) and [local-provider browser setup](doc/WEB/ai-local-provider-cors.md).

## Architecture

| Component | Runtime and responsibilities |
| --- | --- |
| SSMS shell | .NET Framework 4.7.2, x64, inside SSMS 22; WPF UI, commands and editor integration. |
| Engine | .NET 10, `win-x64`, self-contained; parsing, database connections, schema, execution, history and engine services. Publishing explicitly disables single-file packaging and trimming for SqlClient/native interoperability. |
| Browser workspace | Blazor WebAssembly and CodeMirror 6. T-SQL formatting and analysis run locally through shared libraries. Database execution and PostgreSQL language services require a connected engine. Browser state is stored in IndexedDB. |
| Product website | Separate .NET 10 Blazor application using static server-side rendering for product pages, downloads, selected documentation, feedback and administration. |

The desktop shell communicates with its engine through the per-process named pipe `akmlsql-engine-{user-SID}-{shell-PID}`. [FrameProtocol](src/AkmlSql.Core/Ipc/FrameProtocol.cs) uses a 4-byte big-endian length, a 4-byte XOR-rotate checksum and a MessagePack payload, with a 16 MiB payload limit and owner-scoped pipe access.

The browser communicates over a WebSocket using MessagePack messages. In standalone mode, `AkmlSql.Engine.exe --web --config <path>` serves an explicitly configured `bridge.webRoot` on the bridge listener; LAN mode uses HTTPS/WSS and pairing. The desktop engine does not serve the static workspace. See [architecture](doc/architecture.md), [IPC API](doc/ipc-api.md) and [bridge security](doc/m3-security.md).

## Repository map

[AKML-SQL.slnx](AKML-SQL.slnx) contains **14 source projects, 15 test projects and one reference-generator project**. `Shell.Shared` is imported shared source; the Inno Setup installer is not a `.csproj`. The interactive UI test project is included in the solution.

```text
src/
  AkmlSql.Core/             Shared settings, models and IPC (netstandard2.0 + net10.0)
  AkmlSql.Engine/           Database access, RPC handlers, history and web hosting
  AkmlSql.IntelliSense/     Shared T-SQL parsing and completion
  AkmlSql.Formatting/      T-SQL formatter, profiles and style compatibility
  AkmlSql.Analysis/         T-SQL analysis engine and rules
  AkmlSql.PostgreSql/       Engine-side PostgreSQL parser and language services
  AkmlSql.AI/               Shared AI prompts, privacy and provider adapters
  AkmlSql.Shell.Shared/     Shared WPF/editor source imported by SSMS and shell tests
  AkmlSql.Ssms22/           SSMS 22 extension (net472, x64)
  AkmlSql.Web/              Blazor WebAssembly SQL workspace
  AkmlSql.Web.Shared/       Browser contracts (netstandard2.0)
  AkmlSql.Site/             Public website and administration
  AkmlSql.Formatter/        Formatting CLI
  AkmlSql.Analyzer/         Static-analysis CLI
  AkmlSql.Updater/          Windows updater
  AkmlSql.Installer/        Inno Setup 7 and PowerShell hosting helpers
tests/
  AkmlSql.*.Tests/          Unit, integration, browser and installed-UI test projects
  format-parity/           T-SQL formatting inputs and regression goldens
  completion-corpus/       T-SQL completion cases and exclusions
  pg-format-corpus/        PostgreSQL formatting inputs and regression goldens
  pg-completion-corpus/    PostgreSQL completion cases
tools/pg-reference/         PostgreSQL reference generator
scripts/                   Build support, browser runner, theme generation and deployment
doc/                       User guides and engineering documentation
docs/                      Theme tokens and WPF theming guide
specs/                     Feature requirements, task ledgers and verification evidence
```

## Build from source

Use Windows x64, the .NET 10 SDK, Visual Studio/MSBuild with .NET desktop build tools and the .NET Framework 4.7.2 targeting pack. The verified solution build used **Visual Studio 18 Insiders MSBuild**. Inno Setup 7 is required to compile the installer; Node.js is used for JavaScript tests and editor bundle development.

Build the solution with **full MSBuild**, because the SSMS VSSDK targets require `CodeTaskFactory`. Solution-wide builds are supported; the earlier VSCT cross-contamination workaround is no longer necessary.

Run from the repository root in PowerShell:

```powershell
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -prerelease -products * `
    -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
    Select-Object -First 1
if (-not $msbuild) {
    # Path used for the recorded build; adjust for your installation if necessary.
    $msbuild = 'C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe'
}
if (-not (Test-Path -LiteralPath $msbuild)) { throw 'Full MSBuild was not found.' }

& $msbuild AKML-SQL.slnx -t:Restore -p:Configuration=Release -v:quiet
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
& $msbuild AKML-SQL.slnx -t:Build -p:Configuration=Release -m -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
```

For the packaging workflow:

```powershell
.\build.ps1
# Output: src\AkmlSql.Installer\Output\AKMLSQLSetup.exe
```

[build.ps1](build.ps1) checks generated themes, refreshes documentation metadata, publishes the payloads, builds the SSMS extension, runs the Core/Engine/Formatting/Web/Site test projects and compiles the installer. Those five suites do not constitute the full acceptance matrix. `-SkipTests` omits them; `-InstallerOnly` requires existing Release payloads. `-DeploySite` performs a separate IIS publication and release-staging workflow; see [deployment](doc/deployment.md) for its Git and server effects.

The packaging script has its own MSBuild discovery: the VS 18 Enterprise path, then `vswhere`. On the audited server, Visual Studio is registered as incomplete, so that discovery returns no result even though the explicit Insiders MSBuild path above exists. The recorded solution build used that explicit path; `build.ps1` needs working tool discovery before it can run here.

For local development:

```powershell
dotnet run --project src/AkmlSql.Web/AkmlSql.Web.csproj
# Launch profile: https://localhost:52118 and http://localhost:52119

dotnet run --project src/AkmlSql.Site/AkmlSql.Site.csproj --launch-profile http
# Public-site development preview: http://localhost:5180
```

The web development server hosts the browser app; start or connect an engine separately for database operations. Publishing the web project alone does not install a Windows service. Build conventions and troubleshooting are in [CLAUDE.md](CLAUDE.md).

## Testing

After the Release solution build, examples of separate suites are:

```powershell
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj -c Release --no-build
dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj -c Release --no-build
dotnet test tests/AkmlSql.PostgreSql.Tests/AkmlSql.PostgreSql.Tests.csproj -c Release --no-build
dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj -c Release --no-build
dotnet test tests/AkmlSql.Site.Tests/AkmlSql.Site.Tests.csproj -c Release --no-build
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj -c Release --no-build -- xUnit.ParallelizeTestCollections=false
dotnet test tests/AkmlSql.Installer.Tests/AkmlSql.Installer.Tests.csproj -c Release --no-build --filter 'Category!=InstallerSmoke'
node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
```

Use [scripts/run-playwright.ps1](scripts/run-playwright.ps1) for the web browser suites. It builds Debug prerequisites, installs its browser dependencies and runs serially. SQL/service cases have additional prerequisites, and reconnect cases can restart the installed `AkmlSqlWebEngine` service. Native keyboard and installed-SSMS checks need an unlocked interactive desktop. Live PostgreSQL tests use `AKML_PG_TEST_CONNECTION`; see [deployment](doc/deployment.md) for the private test-cluster helper.

Installer installation/repair/uninstall smoke matrices must run on disposable Windows VMs, as required by the [hosting task ledger](specs/045-web-standalone-hosting/tasks.md). The helper suite above excludes those tests. Keep existing golden/corpus baselines unchanged unless an intentional rebaseline is approved.

### Recorded verification

The following are the **2026-10-26–27 recorded results**, checked against the local result files and [spec 045 baseline](specs/045-web-standalone-hosting/baseline.md). They are not a new full-suite run performed for this README edit. Raw logs and TRX files referenced by that baseline are local ignored artifacts.

| Check | Recorded result |
| --- | --- |
| Full Release solution restore/build | Passed with full MSBuild, including after the configuration replacement fix. |
| Web unit suite | 2,017 passed; no failures or skips. |
| Core suite | 1,473 passed, 3 skipped; no failures. |
| Site suite | 917 passed; no failures or skips. |
| Shell suite, serial | 987 passed; no failures or skips. An earlier parallel run had an active-profile isolation failure. |
| Engine | 2,706 functional tests passed, 132 skipped; the separately run historical performance test passed. |
| JavaScript | 298 passed. |
| Installer helpers after the repair | 129 passed; no failures or skips; installer smoke tests excluded. |
| Browser behavior | Full run: 154 passed, 1 failed, 63 skipped. The formatting failure was fixed and its rebuilt follow-up passed 10/10. Final distinct outcomes across runs: 158 passed, 62 skipped, no remaining failures; this was not a second full-suite run. |
| Runtime checks | Isolated Windows-service query cycle and crash recovery passed. Installed SSMS format/execute check passed. After repairing the installed web configuration, HTTP 200, automatic engine connection, `SELECT 42` and query/connection reload passed. |

The installed-service repair used web/engine build `1.26.1027.1312`; the earlier installed-SSMS cycle used extension build `1.26.1022.701`. These checks do not establish every installer upgrade or LAN scenario.

## Current work and known gaps

Task counts below are checkbox counts from the current ledgers, not a claim of complete product certification or deployment:

| Feature | Checked / total tasks | Remaining scope recorded by the ledger |
| --- | --- | --- |
| [041 — Web workspace UX](specs/041-web-ssms-ux-parity/tasks.md) | 216 / 216 | See its baseline for the precise manual checks and study limits. |
| [042 — PostgreSQL](specs/042-postgresql-support/tasks.md) | 181 / 182 | T129: complete the matching-build SSMS regression check. |
| [043 — Query tabs and polish](specs/043-web-query-tabs-polish/tasks.md) | 100 / 104 | T060, T088, T098, T104: keyboard acceptance, user screenshots, Grammarly checks and the final owner walkthrough. |
| [044 — Product website](specs/044-site-product-studio/tasks.md) | 13 / 13 | Implementation and isolated-preview verification recorded in its baseline. |
| [045 — Standalone web hosting](specs/045-web-standalone-hosting/tasks.md) | 95 / 108 | Twelve VM/owner acceptance tasks and T107's strict before/after comparison remain open. |

For spec 045, the strict comparison found no missing tests or newly failing previously passing cases, but fails its gate because two previously passing Core visual-reference checks now skip: this checkout lacks the ignored SQL Prompt reference pack. The installed-server repair does not close the remaining VM matrices. Detailed scope and evidence are in [progress](doc/progress.md) and each feature's task ledger/baseline.

## Documentation

| Reference | Contents |
| --- | --- |
| [Getting started](doc/topics/getting-started.md) / [Browser setup](doc/topics/browser-setup.md) | Installation and user workflows. |
| [Architecture](doc/architecture.md) / [IPC API](doc/ipc-api.md) | Components, runtime boundaries and protocol contracts. |
| [Configuration](doc/configuration.md) / [Deployment](doc/deployment.md) | Settings, hosting, packaging, upgrades and diagnostics. |
| [Formatting](doc/formatting.md) / [Analysis rules](doc/analysis-rules.md) | Formatter behavior, profiles and rule documentation. |
| [PostgreSQL reference](doc/postgresql-reference.md) | PostgreSQL language reference and support. |
| [Web engineering docs](doc/WEB/00-INDEX.md) | Web milestones and technical quickstarts; later specs supersede some original milestones. |
| [WPF theming](docs/wpf-theming.md) / [Theme tokens](docs/theme-tokens.json) | Shared theme definitions and generated CSS. |
| [CLAUDE.md](CLAUDE.md) / [PROJECT_MEMORY.md](PROJECT_MEMORY.md) | Contributor workflow and dated source orientation. |
| [Progress](doc/progress.md) / [Specifications](specs/) | Implementation history, acceptance results and open work. |

## License

MIT, copyright © 2026 Mohamed Khamis. See the [license distributed with the installer](src/AkmlSql.Installer/LICENSE.txt) and [third-party notices](THIRD-PARTY-NOTICES.txt).
