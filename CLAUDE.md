# AKML-SQL Development Guidelines

AI-powered SQL development assistance for SSMS 20/21/22 and Visual Studio 2019/2022/2026.
Author: Abdulrahman Khamis | License: MIT | Version: 1.0.0

## Project Structure

```text
AKML-SQL.slnx                          # Solution file (.slnx format)
src/
  AkmlSql.Core/                        # Shared library (netstandard2.0 + net10.0)
  AkmlSql.Shell.Shared/                # Shared project (.projitems) for all shell extensions
  AkmlSql.Ssms20/                      # SSMS 20 extension (net472, x86, VS SDK 15.9.3)
  AkmlSql.Ssms21/                      # SSMS 21 extension (net472, x64, VS SDK 17.14.x)
  AkmlSql.Ssms22/                      # SSMS 22 extension (net472, x64, VS SDK 17.14.x)
  AkmlSql.VS2019/                      # VS 2019 extension (net472, x86, VS SDK 16.0.208)
  AkmlSql.VS2022/                      # VS 2022 extension (net472, x64, VS SDK 17.14.x)
  AkmlSql.VS2026/                      # VS 2026 extension (net472, x64, VS SDK 17.14.x)
  AkmlSql.Updater/                     # Self-contained updater (net10.0, win-x64, trimmed)
  AkmlSql.Installer/                   # Inno Setup 7 installer scripts
tests/
  AkmlSql.Core.Tests/                  # xunit tests (net10.0)
doc/                                   # PRD documents (Phase 1, Phase 2)
specs/                                 # Specify framework feature specs
```

## Technologies

- **Shell Extensions**: C# / .NET Framework 4.7.2, LangVersion latest
- **Core Library**: netstandard2.0 (for shell) + net10.0 (for updater), dual-target
- **Updater**: .NET 10, self-contained single-file, win-x64, PublishTrimmed
- **Installer**: Inno Setup 7 Pascal Script
- **Tests**: xunit 2.x, Microsoft.NET.Test.Sdk 17.x
- **Logging**: Serilog 4.x + Serilog.Sinks.File 6.x
- **JSON**: System.Text.Json 8.x (netstandard2.0 polyfill)

## VS SDK Versions (Critical)

| Target   | VS SDK Version | VSSDK.BuildTools | Platform | Shell Assembly Version |
|----------|---------------|------------------|----------|----------------------|
| SSMS 20  | 15.9.3        | 15.*             | x86      | 15.0.0.0             |
| VS 2019  | 16.0.208      | 16.*             | x86      | 16.0.0.0             |
| SSMS 21  | 17.14.*       | 17.*             | x64      | 17.0.0.0             |
| SSMS 22  | 17.14.*       | 17.*             | x64      | 17.0.0.0             |
| VS 2022  | 17.14.*       | 17.*             | x64      | 17.0.0.0             |
| VS 2026  | 17.14.*       | 17.*             | x64      | 17.0.0.0             |

## Build Commands

Shell projects MUST be built individually with MSBuild (not `dotnet build`) to avoid VSCT cross-contamination:

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"

# Restore then build each project separately
"$MSBUILD" "src/AkmlSql.Ssms20/AkmlSql.Ssms20.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.Ssms20/AkmlSql.Ssms20.csproj" -t:Build -p:Configuration=Release -v:minimal

# Updater (uses dotnet)
dotnet publish src/AkmlSql.Updater/AkmlSql.Updater.csproj -c Release

# Installer (Inno Setup 7)
"/c/Program Files/Inno Setup 7/ISCC.exe" src/AkmlSql.Installer/AkmlSqlSetup.iss

# Tests
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
```

## Build Gotchas

- **Never use `dotnet build` for shell projects** — CodeTaskFactory in VSSDK requires full MSBuild
- **Never build shell projects via solution** — causes VSCT CTO cross-contamination (all projects look for the last project's .cto file)
- **Always clean obj/bin after SDK version changes** — stale NuGet cache causes wrong assembly version references
- **SSMS 20 uses Schema 2010 vsixmanifest** (`<Vsix>` root) — all other targets use Schema 2011 v2.0 (`<PackageManifest>`)
- **SSMS 20 = VS 2017 IsolatedShell** — Shell.15.0 assembly version must be 15.0.0.0, not 16.0.0.0

## Architecture

- **Shared Project Pattern**: `AkmlSql.Shell.Shared` (.projitems) is imported by all 6 shell extension projects — same source compiled against different VS SDK versions
- **Package GUID**: `{A1B2C3D4-1111-2222-3333-444455556666}` (shared across all targets)
- **Command Set GUID**: `{A1B2C3D4-1111-2222-3333-444455557777}`
- **Menu Commands**: About, Check for Updates, Options, Send Feedback, View Logs
- **Atomic Config Writes**: ConfigManager uses temp file + rename pattern
- **Thread-safe Logger Init**: LoggerFactory uses Interlocked.CompareExchange
- **Update Flow**: Shell extension fires updater process → updater writes result JSON → shell reads on next load

## Key Paths at Runtime

- Config: `%AppData%/AKML SQL/config.json`
- Logs: `%AppData%/AKML SQL/logs/akmlsql-*.log`
- Update result: `%AppData%/AKML SQL/cache/update-available.json`
- SSMS 20 extension install: `<SSMS20Root>/Common7/IDE/Extensions/AkmlSql/`
- SSMS 20 MEF cache: `%LocalAppData%/Microsoft/SQL Server Management Studio/20.0_IsoShell/ComponentModelCache/`

## Installer Details

- **Output**: `src/AkmlSql.Installer/Output/AKMLSQLSetup.exe`
- **Detection**: Registry + vswhere.exe + filesystem fallback (see `environment-scanner.iss`)
- **Post-install**: Clears MEF caches, writes config.json (only if absent)
- **Silent mode**: `/VERYSILENT /ACCEPTEULA /TARGETS=20,22,2022 /NOUPDATE`

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
