# Quickstart: AKML SQL Phase 1

**Branch**: `001-phase1-foundation-installer` | **Date**: 2026-03-16

## Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| Visual Studio 2022 | 17.x | IDE for development (with "Visual Studio extension development" workload) |
| .NET SDK | 10.0+ | Build Core library and Updater |
| .NET Framework 4.7.2 Targeting Pack | — | Build shell extension projects |
| Inno Setup 6 | 6.x | Compile the installer |
| SSMS 22 (or any target IDE) | — | Manual testing of the extension |

## Initial Setup

```bash
# Clone the repository
git clone <repo-url>
cd AKML-SQL
git checkout 001-phase1-foundation-installer

# Restore NuGet packages
dotnet restore AKML-SQL.sln
```

## Build

```bash
# Build all projects
dotnet build AKML-SQL.sln --configuration Release

# Build only the Core library
dotnet build src/AkmlSql.Core/AkmlSql.Core.csproj

# Build a specific shell project (e.g., SSMS 22)
dotnet build src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj

# Publish the Updater as self-contained single-file
dotnet publish src/AkmlSql.Updater/AkmlSql.Updater.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=true
```

## Test

```bash
# Run Core unit tests
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj

# Manual extension testing: use VS 2022 Experimental Instance
# (F5 from the AkmlSql.VS2022 project in Visual Studio)
```

## Build the Installer

```bash
# Compile the Inno Setup installer (requires iscc.exe in PATH)
iscc.exe src/AkmlSql.Installer/AkmlSqlSetup.iss
```

Output: `src/AkmlSql.Installer/Output/AKMLSQLSetup.exe`

## Test the Installer

```powershell
# Interactive install
.\AKMLSQLSetup.exe

# Silent install for SSMS 22 only
.\AKMLSQLSetup.exe /VERYSILENT /TARGETS="ssms22" /LOG="install.log"

# Verify installation
Get-ChildItem "$env:ProgramFiles\AKML SQL\"
Get-ChildItem "$env:AppData\AKML SQL\"
```

## Project Map

| Project | What it does | Key file |
|---------|-------------|----------|
| `AkmlSql.Core` | Shared logic (config, logging, models) | `src/AkmlSql.Core/AkmlSql.Core.csproj` |
| `AkmlSql.Shell.Shared` | Shared command handlers and dialogs | `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.shproj` |
| `AkmlSql.Ssms20` | VSPackage for SSMS 20 (x86) | `src/AkmlSql.Ssms20/AkmlSqlPackage.cs` |
| `AkmlSql.Ssms21` | VSPackage for SSMS 21 (x64) | `src/AkmlSql.Ssms21/AkmlSqlPackage.cs` |
| `AkmlSql.Ssms22` | VSPackage for SSMS 22 (x64) | `src/AkmlSql.Ssms22/AkmlSqlPackage.cs` |
| `AkmlSql.VS2019` | VSIX for VS 2019 (x86) | `src/AkmlSql.VS2019/AkmlSqlPackage.cs` |
| `AkmlSql.VS2022` | VSIX for VS 2022 (x64) | `src/AkmlSql.VS2022/AkmlSqlPackage.cs` |
| `AkmlSql.VS2026` | VSIX for VS 2026 (x64) | `src/AkmlSql.VS2026/AkmlSqlPackage.cs` |
| `AkmlSql.Updater` | Background update checker (.NET 10) | `src/AkmlSql.Updater/Program.cs` |
| `AkmlSql.Installer` | Inno Setup installer script | `src/AkmlSql.Installer/AkmlSqlSetup.iss` |

## Development Workflow

1. **Core changes**: Edit `AkmlSql.Core`, run `AkmlSql.Core.Tests`
2. **Shell changes**: Edit `AkmlSql.Shell.Shared`, test via F5 on `AkmlSql.VS2022` (experimental instance)
3. **Installer changes**: Edit `.iss` files, compile with `iscc.exe`, test on a VM
4. **Updater changes**: Edit `AkmlSql.Updater`, publish single-file, test manually

## Key Conventions

- **GUIDs**: All package and command GUIDs are in `AkmlSql.Shell.Shared/PackageGuids.cs`
- **Logging**: Use `Serilog.Log.Logger` from Core's `LoggerFactory.Initialize()`
- **Config**: Use `ConfigManager.Load()` / `.Save()` from Core
- **Menu commands**: Each command is a separate class in `Shell.Shared/Commands/`
- **No EULA acceptance**: MIT license shown as informational page only
