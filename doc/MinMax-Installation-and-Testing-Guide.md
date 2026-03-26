# AKML SQL — Installation & Testing Guide (Phases 1–4)

> **Version:** 1.0 | **Date:** March 2026 | **Author:** Abdulrahman Khamis
> **Applies to:** Phase 1 (Foundation), Phase 2 (IntelliSense), Phase 3 (Formatter), Phase 4 (Snippet Manager)
> **Branch:** `004-snippet-manager`

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Project Structure](#2-project-structure)
3. [Build System Overview](#3-build-system-overview)
4. [Building the Core Library](#4-building-the-core-library)
5. [Building Shell Extensions](#5-building-shell-extensions)
6. [Building the IntelliSense Engine](#6-building-the-intellisense-engine)
7. [Building the Updater](#7-building-the-updater)
8. [Building the Installer](#8-building-the-installer)
9. [Running Tests](#9-running-tests)
10. [Manual Testing Procedures](#10-manual-testing-procedures)
11. [Deployment](#11-deployment)
12. [Troubleshooting](#12-troubleshooting)

---

## 1. Prerequisites

### 1.1 Hardware & OS

| Requirement | Details |
|---|---|
| **OS** | Windows 10 (21H2+), Windows 11, Windows Server 2019+ |
| **Processor** | x86-64-v3 (Intel/AMD 64-bit) |
| **Memory** | 16 GB RAM minimum (32 GB recommended for full build) |
| **Disk** | 10 GB free space |
| **Display** | 1920×1080 or higher |

### 1.2 Required Software

| Software | Version | Purpose | Download |
|---|---|---|---|
| **Visual Studio 2022 Enterprise** | 17.14.x | Primary build environment | [vs.microsoft.com](https://visualstudio.microsoft.com) |
| **.NET 10 SDK** | 10.0.xxx | Core library, engine, updater builds | [dotnet.microsoft.com](https://dotnet.microsoft.com) |
| **.NET Framework 4.7.2 SDK** | 4.7.2 | Shell extension compatibility | Included in VS 2022 |
| **MSBuild** | Via VS 2022 | Shell project builds | `C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe` |
| **Inno Setup 7** | 7.x | Installer creation | [jrsoftware.org](https://jrsoftware.org/isdl.php) |
| **Git** | 2.x | Source control | [git-scm.com](https://git-scm.com) |

### 1.3 Optional Software

| Software | Purpose |
|---|---|
| **SSMS 20** (SQL Server Management Studio) | Testing SSMS 20 extension |
| **SSMS 21** | Testing SSMS 21 extension |
| **SSMS 22** | Testing SSMS 22 extension |
| **Visual Studio 2019** | Testing VS 2019 extension |
| **Visual Studio 2022** | Testing VS 2022 extension |
| **Visual Studio 2026** | Testing VS 2026 extension |
| **SQL Server 2016–2025** | Testing IntelliSense against real databases |
| **AdventureWorks** sample database | Standard test database |

### 1.4 Environment Variables

Add these to your system PATH:

```powershell
# MSBuild
C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin

# .NET CLI
C:\Program Files\dotnet\

# Inno Setup 7
C:\Program Files\Inno Setup 7
```

---

## 2. Project Structure

```
AKML-SQL/
├── AKML-SQL.slnx                     # Solution file (.slnx format)
├── CLAUDE.md                          # Development guidelines
├── src/
│   ├── AkmlSql.Core/                  # Shared business logic (netstandard2.0 + net10.0)
│   ├── AkmlSql.Shell.Shared/          # Shared project (.projitems) for all shell extensions
│   ├── AkmlSql.Ssms20/                # SSMS 20 extension (net472, x86)
│   ├── AkmlSql.Ssms21/                # SSMS 21 extension (net472, x64)
│   ├── AkmlSql.Ssms22/                # SSMS 22 extension (net472, x64)
│   ├── AkmlSql.VS2019/                # VS 2019 extension (net472, x86)
│   ├── AkmlSql.VS2022/                # VS 2022 extension (net472, x64)
│   ├── AkmlSql.VS2026/                # VS 2026 extension (net472, x64)
│   ├── AkmlSql.Engine/                # IntelliSense engine (net10.0, out-of-proc)
│   ├── AkmlSql.Formatting/            # SQL formatting library (net10.0)
│   ├── AkmlSql.Formatter/             # Formatter host (console app, net10.0)
│   ├── AkmlSql.Updater/               # Self-contained updater (net10.0)
│   └── AkmlSql.Installer/             # Inno Setup installer scripts
├── tests/
│   └── AkmlSql.Core.Tests/            # xunit tests (net10.0)
├── doc/                               # PRD documents and guides
└── specs/                             # Specify framework specs
```

### 2.1 Key Project Dependencies

```
AkmlSql.Core
    └── AkmlSql.Shell.Shared (imported by all shell projects)
    └── AkmlSql.Engine
    └── AkmlSql.Formatting
    └── AkmlSql.Updater

AkmlSql.Shell.Shared
    └── AkmlSql.Core
    └── AkmlSql.Ssms20 (net472, x86)
    └── AkmlSql.Ssms21 (net472, x64)
    └── AkmlSql.Ssms22 (net472, x64)
    └── AkmlSql.VS2019 (net472, x86)
    └── AkmlSql.VS2022 (net472, x64)
    └── AkmlSql.VS2026 (net472, x64)

AkmlSql.Formatting
    └── AkmlSql.Core

AkmlSql.Formatter
    └── AkmlSql.Formatting
    └── AkmlSql.Core
```

---

## 3. Build System Overview

### 3.1 Build Commands Reference

| Component | Build Tool | Command |
|---|---|---|
| **Core Library** | dotnet | `dotnet build src/AkmlSql.Core/AkmlSql.Core.csproj -c Release` |
| **Shell Extensions** | MSBuild | `MSBuild.exe src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Build -c Release` |
| **Engine** | dotnet | `dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64` |
| **Formatter** | dotnet | `dotnet publish src/AkmlSql.Formatter/AkmlSql.Formatter.csproj -c Release -r win-x64` |
| **Updater** | dotnet | `dotnet publish src/AkmlSql.Updater/AkmlSql.Updater.csproj -c Release` |
| **Installer** | Inno Setup | `"C:\Program Files\Inno Setup 7\ISCC.exe" src/AkmlSql.Installer/AkmlSqlSetup.iss` |
| **Tests** | dotnet | `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj` |

### 3.2 Critical Build Rules

> **WARNING: Never use `dotnet build` for shell projects.** CodeTaskFactory in VSSDK requires full MSBuild. Using `dotnet build` on shell projects will fail.

> **WARNING: Never build shell projects via the solution file.** This causes VSCT CTO cross-contamination where all projects look for the last project's `.cto` file.

### 3.3 MSBuild Path

```bash
# Standard path (adjust for your VS edition)
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
```

---

## 4. Building the Core Library

### 4.1 AkmlSql.Core

The Core library is the foundation for all other components. It provides:
- Configuration management (`ConfigManager`)
- Logging (`LoggerFactory`)
- Update models (`UpdateAvailable`)
- Shared interfaces and utilities

**Build:**

```bash
dotnet build src/AkmlSql.Core/AkmlSql.Core.csproj -c Release
```

**Output:** `src/AkmlSql.Core/bin/Release/netstandard2.0/` and `src/AkmlSql.Core/bin/Release/net10.0/`

**Key DLLs:**
- `AkmlSql.Core.dll` (netstandard2.0 — for shell extensions)
- `AkmlSql.Core.dll` (net10.0 — for engine/updater)

**What it contains:**
- `AkmlSql.Core.Config/` — Configuration management
- `AkmlSql.Core.Logging/` — Serilog-based logging
- `AkmlSql.Core.Updater/` — Update check models
- `AkmlSql.Core/Constants.cs` — Shared GUIDs, paths, version info

### 4.2 AkmlSql.Formatting

The formatting library provides the SQL formatting engine used by both the formatter console app and the IntelliSense engine.

**Build:**

```bash
dotnet build src/AkmlSql.Formatting/AkmlSql.Formatting.csproj -c Release
```

**Output:** `src/AkmlSql.Formatting/bin/Release/net10.0/`

**What it contains:**
- T-SQL parsing via `Microsoft.SqlServer.TransactSql.ScriptDom`
- Layout engine with 250+ formatting options
- Profile management (`FormattingProfile`, `ProfileManager`)
- Text emission with proper indentation

### 4.3 Build Order

```
1. AkmlSql.Core (netstandard2.0 + net10.0)
2. AkmlSql.Formatting (net10.0) — depends on Core
3. AkmlSql.Shell.Shared (no build — shared project)
```

---

## 5. Building Shell Extensions

### 5.1 Overview

Shell extensions are built for 6 different IDE versions:

| Project | Target | Platform | VS SDK |
|---|---|---|---|
| `AkmlSql.Ssms20` | SSMS 20 | x86 | 15.9.3 |
| `AkmlSql.Ssms21` | SSMS 21 | x64 | 17.14.x |
| `AkmlSql.Ssms22` | SSMS 22 | x64 | 17.14.x |
| `AkmlSql.VS2019` | VS 2019 | x86 | 16.0.208 |
| `AkmlSql.VS2022` | VS 2022 | x64 | 17.14.x |
| `AkmlSql.VS2026` | VS 2026 | x64 | 17.14.x |

All shell projects import `AkmlSql.Shell.Shared` (.projitems) and depend on `AkmlSql.Core`.

### 5.2 Build Each Shell Project Individually

**Step 1: Restore NuGet packages**

```bash
# Run for each project
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.Ssms21/AkmlSql.Ssms21.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.Ssms20/AkmlSql.Ssms20.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.VS2022/AkmlSql.VS2022.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.VS2026/AkmlSql.VS2026.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.VS2019/AkmlSql.VS2019.csproj" -t:Restore -p:Configuration=Release -v:quiet
```

**Step 2: Build each project**

```bash
# Always build individually — never via solution file
"$MSBUILD" "src/AkmlSql.Ssms20/AkmlSql.Ssms20.csproj" -t:Build -p:Configuration=Release -v:minimal
"$MSBUILD" "src/AkmlSql.Ssms21/AkmlSql.Ssms21.csproj" -t:Build -p:Configuration=Release -v:minimal
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build -p:Configuration=Release -v:minimal
"$MSBUILD" "src/AkmlSql.VS2019/AkmlSql.VS2019.csproj" -t:Build -p:Configuration=Release -v:minimal
"$MSBUILD" "src/AkmlSql.VS2022/AkmlSql.VS2022.csproj" -t:Build -p:Configuration=Release -v:minimal
"$MSBUILD" "src/AkmlSql.VS2026/AkmlSql.VS2026.csproj" -t:Build -p:Configuration=Release -v:minimal
```

### 5.3 SSMS 20 Specific Notes

SSMS 20 uses a different VS SDK (15.9.3) and requires:
- Shell assembly version `15.0.0.0` (not 16.0.0.0)
- Schema 2010 `<Vsix>` vsixmanifest (not Schema 2011)
- Synchronous `Package` class (not `AsyncPackage`)
- `AllowsBackgroundLoad=dword:00000000` in pkgdef

**Build output for SSMS 20:**
`src/AkmlSql.Ssms20/bin/Release/net472/`

### 5.4 SSMS 21/22 Specific Notes

SSMS 21 and 22 share the same VS SDK (17.14.x) but:
- Extension path is under `Release/` subdirectory
- Auto-load context is `{B7B07F42-6013-4C67-A504-C771CBC7625A}` (UICONTEXT_SSMS)
- Menu placed under Tools via `CommandPlacement` to `IDG_VS_TOOLS_EXT_TOOLS`

**Build outputs:**
- SSMS 21: `src/AkmlSql.Ssms21/bin/Release/net472/`
- SSMS 22: `src/AkmlSql.Ssms22/bin/Release/net472/`

### 5.5 VS 2019 Specific Notes

VS 2019 uses VS SDK 16.0.208 and platform x86:
- Shell assembly version `16.0.0.0`
- Auto-load context `{e8fbc700-a1bd-11d0-a67c-00a0c9110051}` (ShellInitialized)

**Build output:** `src/AkmlSql.VS2019/bin/Release/net472/`

### 5.6 VS 2022/2026 Specific Notes

VS 2022 and 2026 use VS SDK 17.14.x and platform x64:
- Shell assembly version `17.0.0.0`
- Auto-load context `{e8fbc700-a1bd-11d0-a67c-00a0c9110051}` (ShellInitialized)

**Build outputs:**
- VS 2022: `src/AkmlSql.VS2022/bin/Release/net472/`
- VS 2026: `src/AkmlSql.VS2026/bin/Release/net472/`

### 5.7 Shared Components (AkmlSql.Shell.Shared)

The `AkmlSql.Shell.Shared` project is a shared project (.projitems) — it is NOT built directly. Instead, it is imported into each shell project at compile time. Source files are compiled against each target's VS SDK version.

**What's shared:**
- `AkmlSqlPackage.cs` — Main package class
- `Commands/` — All 5 menu commands (About, Check for Updates, Options, Send Feedback, View Logs)
- `Dialogs/` — Dialog windows
- `StatusBar/` — Status bar indicator
- `LoadValidator/` — Extension load validation
- `UpdateLauncher/` — Launches updater process
- `AkmlSql.Shell.Shared.projitems` — Master include file

### 5.8 Key Files Per Shell Project

Each shell project contains:

| File | Purpose |
|---|---|
| `AkmlSql.{Target}.csproj` | Project file with VS SDK references and shared project import |
| `AkmlSql.{Target}.PkgDef` | Registry entries for package registration |
| `source.extension.vsixmanifest` | Extension manifest |
| `VSPackage.resx` | Resource file with `MergeWithCTO=true` for CTO embedding |
| `AkmlSql.{Target}.vsct` | Command definitions (menus, buttons, icons) |

### 5.9 Verify Shell Extension Build

After building, verify the output contains:

```bash
# For SSMS 22 example
ls src/AkmlSql.Ssms22/bin/Release/net472/
```

Expected files:
- `AkmlSql.Ssms22.dll` — Main extension assembly
- `AkmlSql.Ssms22.pkgdef` — Package registration
- `AkmlSql.Core.dll` — Core library dependency
- `*.dll` — All transitive dependencies

---

## 6. Building the IntelliSense Engine

### 6.1 AkmlSql.Engine

The IntelliSense engine runs out-of-process (.NET 10) and communicates with shell extensions via named pipes.

**Build:**

```bash
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64 --self-contained
```

**Output:** `src/AkmlSql.Engine/bin/Release/net10.0/win-x64/publish/`

**What it contains:**
- T-SQL parser (Microsoft.SqlServer.TransactSql.ScriptDom)
- Schema metadata service (reads sys catalog)
- Completion providers (keyword, object, column, JOIN assist, etc.)
- Named pipe server for communication with shell
- Snippet engine (Phase 4)
- Formatting pipeline (Phase 3)

**Key DLLs:**
- `AkmlSql.Engine.exe` — Main executable
- `AkmlSql.Core.dll` — Core library
- `AkmlSql.Formatting.dll` — Formatter library
- `Microsoft.SqlServer.TransactSql.ScriptDom.dll` — T-SQL parser

### 6.2 Named Pipe Protocol

The engine listens on a named pipe for requests from the shell extension:

| Message | Direction | Purpose |
|---|---|---|
| `ConnectionChanged` | Shell → Engine | New database connection |
| `DocumentChanged` | Shell → Engine | Editor content changed |
| `RequestCompletion` | Shell → Engine | User triggered completion |
| `CompletionResult` | Engine → Shell | Ranked suggestions |
| `FormatRequest` | Shell → Engine | Format SQL |
| `FormatResult` | Engine → Shell | Formatted text |

---

## 7. Building the Updater

### 7.1 AkmlSql.Updater

Self-contained updater process that checks for updates and downloads new versions.

**Build:**

```bash
dotnet publish src/AkmlSql.Updater/AkmlSql.Updater.csproj -c Release
```

**Output:** `src/AkmlSql.Updater/bin/Release/net10.0/publish/`

**What it does:**
1. Checks version manifest at `https://updates.akmlsql.com/manifest.json`
2. Writes update available flag to `%AppData%/AKML SQL/cache/update-available.json`
3. Optionally downloads and extracts update packages

**Key behavior:**
- Fire-and-forget (non-blocking)
- Runs silently in background
- No UI — just file writes

---

## 8. Building the Installer

### 8.1 AkmlSql.Installer

Inno Setup 7 installer that creates the final `AKMLSQLSetup.exe`.

**Prerequisites:**
- All shell extensions built
- Engine published
- Updater published
- Inno Setup 7 installed at `C:\Program Files\Inno Setup 7\`

**Build:**

```bash
"/c/Program Files/Inno Setup 7/ISCC.exe" src/AkmlSql.Installer/AkmlSqlSetup.iss
```

**Output:** `src/AkmlSql.Installer/Output/AKMLSQLSetup.exe`

### 8.2 What the Installer Does

1. **Environment Scan** — Detects SSMS 20/21/22 and VS 2019/2022/2026 installations
2. **Component Selection** — User selects which IDEs to install
3. **Copy Files** — Copies extension DLLs to each IDE's Extensions folder
4. **Write PkgDef** — Registers package with IDE
5. **Clear Caches** — Clears MEF ComponentModelCache
6. **Create Shortcuts** — Start menu and desktop shortcuts
7. **Write Config** — Creates `%AppData%/AKML SQL/config.json`

### 8.3 Silent Installation

```bash
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /TARGETS="ssms22,vs2022" /NOUPDATE
```

| Switch | Description |
|---|---|
| `/VERYSILENT` | No UI |
| `/ACCEPTEULA` | Accept license |
| `/TARGETS` | Comma-separated: `ssms20,ssms21,ssms22,vs2019,vs2022,vs2026` |
| `/NOUPDATE` | Skip auto-update check |
| `/DIR` | Override installation directory |

---

## 9. Running Tests

### 9.1 Core Tests (AkmlSql.Core.Tests)

Uses xunit 2.x with Microsoft.NET.Test.Sdk 17.x.

**Run all tests:**

```bash
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
```

**Run with coverage:**

```bash
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj --collect:"XPlat Code Coverage"
```

**Run specific test class:**

```bash
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj --filter "FullyQualifiedName~ConfigManagerTests"
```

**Run in debug mode:**

```bash
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj --configuration Debug
```

### 9.2 Expected Test Areas (Phases 1–4)

| Phase | Test Area | Coverage |
|---|---|---|
| Phase 1 | ConfigManager atomic writes | Temp file + rename pattern |
| Phase 1 | LoggerFactory thread-safe init | Interlocked.CompareExchange |
| Phase 1 | Update model serialization | JSON round-trip |
| Phase 2 | T-SQL parser | Statement detection, alias resolution |
| Phase 3 | Formatting options | All 250+ options produce valid output |
| Phase 3 | Semantic preservation | AST equivalence before/after format |
| Phase 4 | Snippet loader | JSON parsing, multi-source resolution |
| Phase 4 | Variable expansion | Built-in + custom variables |

### 9.3 Test Output

```
Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed! - Failed: 0, Passed: 47, Skipped: 0, Total: 47
```

---

## 10. Manual Testing Procedures

### 10.1 Phase 1 — Extension Loading

**Objective:** Verify extension loads in each IDE without errors.

**Test for SSMS 22:**

1. Deploy extension:
   ```powershell
   $dest = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql"
   Copy-Item "src\AkmlSql.Ssms22\bin\Release\net472\*.dll" $dest
   Copy-Item "src\AkmlSql.Ssms22\AkmlSql.Ssms22.pkgdef" $dest
   Copy-Item "src\AkmlSql.Ssms22\source.extension.vsixmanifest" "$dest\extension.vsixmanifest"
   ```

2. Clear caches:
   ```powershell
   Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_*\ComponentModelCache"
   Remove-Item -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_*\privateregistry.bin*"
   ```

3. Launch SSMS with logging:
   ```powershell
   & "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Ssms.exe" /log
   ```

4. Check activity log:
   ```powershell
   Get-Content "$env:APPDATA\Microsoft\SSMS\22.0_*\ActivityLog.xml" -Encoding Unicode | Select-String "Akml"
   ```

5. **Expected:** "AKML SQL" menu appears under **Tools** menu. All 5 commands functional.

**Test for SSMS 20:**

1. Deploy extension to SSMS 20 path:
   ```powershell
   $dest = "<SSMS20Root>\Common7\IDE\Extensions\AkmlSql"
   # Copy DLLs, pkgdef, vsixmanifest
   ```

2. Clear cache:
   ```powershell
   Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SQL Server Management Studio\20.0_IsoShell\ComponentModelCache"
   ```

3. Launch SSMS 20 with logging:
   ```powershell
   & "<SSMS20Path>\Common7\IDE\Ssms.exe" /log
   ```

4. **Expected:** "AKML SQL" menu visible. Commands respond to clicks.

### 10.2 Phase 2 — IntelliSense

**Objective:** Verify schema-aware autocomplete works.

**Prerequisites:**
- Engine must be running (`AkmlSql.Engine.exe`)
- Connected to a SQL Server with test database (e.g., AdventureWorks)

**Test cases:**

| # | Action | Expected Result |
|---|---|---|
| 1 | Type `SELECT * FROM dbo.` | Completion popup shows tables/views in dbo schema |
| 2 | Select a table, type `.` | Completion popup shows columns with data types |
| 3 | Type `FROM dbo.Orders o JOIN ` | Suggestion shows tables with FK to Orders |
| 4 | Type `SEL` | Suggestion shows `SELECT` keyword |
| 5 | Press Ctrl+Space | Full completion list appears |

### 10.3 Phase 3 — SQL Formatter

**Objective:** Verify SQL formatting works correctly.

**Test cases:**

| # | Action | Expected Result |
|---|---|---|
| 1 | Open messy SQL, press Ctrl+K, Y | SQL reformatted with active profile |
| 2 | Select fragment, press Ctrl+K, F | Only selected text formatted |
| 3 | Change profile via dropdown | Status bar shows new profile name |
| 4 | Create custom profile | New profile appears in dropdown |
| 5 | Type `--noformat` region | Content inside preserved as-is |

**CLI formatter test:**

```bash
# Format a file
dotnet src/AkmlSql.Formatter/bin/Release/net10.0/win-x64/publish/AkmlSql.Formatter.dll --file "test.sql" --profile "Default"

# Check mode (exit code 0 = formatted correctly)
dotnet AkmlSql.Formatter.dll --file "test.sql" --check
```

### 10.4 Phase 4 — Snippet Manager

**Objective:** Verify snippet expansion and management.

**Test cases:**

| # | Action | Expected Result |
|---|---|---|
| 1 | Type `ssf` + Tab | Expands to `SELECT * FROM ` |
| 2 | Type `ct` + Tab | Expands to CREATE TABLE template |
| 3 | Navigate snippet | Tab/Shift+Tab moves between placeholders |
| 4 | Open Snippet Manager | Dialog shows all snippets by category |
| 5 | Create custom snippet | Appears in Personal category |
| 6 | Type `$DATE$` in snippet | Expands to current date |
| 7 | Surround-with: select text, run `stc` | TRY/CATCH wraps selection |

---

## 11. Deployment

### 11.1 Development Deployment

**SSMS 22 (most common test target):**

```powershell
$src = "src\AkmlSql.Ssms22\bin\Release\net472"
$dest = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql"

# Create destination if needed
New-Item -ItemType Directory -Force -Path $dest | Out-Null

# Copy all DLLs (not just changed ones to avoid stale references)
Copy-Item "$src\*.dll" $dest -Force
Copy-Item "$src\AkmlSql.Ssms22.pkgdef" $dest -Force
Copy-Item "$src\source.extension.vsixmanifest" "$dest\extension.vsixmanifest" -Force

# Clear SSMS caches
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_*\ComponentModelCache" -ErrorAction SilentlyContinue
Remove-Item -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_*\privateregistry.bin*" -ErrorAction SilentlyContinue

# Launch
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Ssms.exe"
```

**SSMS 20:**

```powershell
$src = "src\AkmlSql.Ssms20\bin\Release\net472"
$dest = "<SSMS20_ROOT>\Common7\IDE\Extensions\AkmlSql"
# Copy files
# Clear cache
& "<SSMS20_ROOT>\Common7\IDE\Ssms.exe" /log
```

### 11.2 Engine Deployment

The engine runs out-of-process. Deploy alongside the extension:

```powershell
$engine = "src\AkmlSql.Engine\bin\Release\net10.0\win-x64\publish"
$dest = "C:\Program Files\AKML SQL\Engine"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item "$engine\*" $dest -Recurse -Force
```

### 11.3 Production Build (Full)

```bash
#!/bin/bash
# Full production build script

set -e

MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
ISCC="/c/Program Files/Inno Setup 7/ISCC.exe"

echo "=== Building Core ==="
dotnet build src/AkmlSql.Core/AkmlSql.Core.csproj -c Release

echo "=== Building Formatting ==="
dotnet build src/AkmlSql.Formatting/AkmlSql.Formatting.csproj -c Release

echo "=== Building Engine ==="
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64 --self-contained

echo "=== Building Formatter ==="
dotnet publish src/AkmlSql.Formatter/AkmlSql.Formatter.csproj -c Release -r win-x64 --self-contained

echo "=== Building Updater ==="
dotnet publish src/AkmlSql.Updater/AkmlSql.Updater.csproj -c Release

echo "=== Building Shell Extensions ==="
for target in Ssms20 Ssms21 Ssms22 VS2019 VS2022 VS2026; do
    echo "Building AkmlSql.$target..."
    "$MSBUILD" "src/AkmlSql.$target/AkmlSql.$target.csproj" -t:Restore -c Release -v:quiet
    "$MSBUILD" "src/AkmlSql.$target/AkmlSql.$target.csproj" -t:Build -c Release -v:minimal
done

echo "=== Running Tests ==="
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj -c Release

echo "=== Building Installer ==="
"$ISCC" src/AkmlSql.Installer/AkmlSqlSetup.iss

echo "=== Build Complete ==="
echo "Output: src/AkmlSql.Installer/Output/AKMLSQLSetup.exe"
```

---

## 12. Troubleshooting

### 12.1 Extension Not Loading

**Symptom:** No AKML SQL menu appears in IDE.

**Check activity log:**
```powershell
Get-Content "$env:APPDATA\Microsoft\SSMS\22.0_*\ActivityLog.xml" -Encoding Unicode | Select-String "Akml"
```

**Common causes:**

| Cause | Fix |
|---|---|
| Missing CTO resource | Ensure `VSPackage.resx` has `MergeWithCTO=true` |
| Wrong pkgdef path | Verify extension path matches IDE's `PkgDefSearchPath` |
| Stale privateregistry.bin | Delete `privateregistry.bin*` in `%LOCALAPPDATA%\Microsoft\SSMS\22.0_*\` |
| Wrong AutoLoad GUID | SSMS 21/22 must use `{B7B07F42-6013-4C67-A504-C771CBC7625A}` |
| Init order failure | LoggerFactory/LoadValidator must be called AFTER command registration |

### 12.2 Commands Don't Respond

**Symptom:** Menu visible but clicking does nothing.

**Root cause:** `InitializeAsync()` calls `LoggerFactory.Initialize()` or `LoadValidator.Validate()` BEFORE registering commands. If either throws, commands are never wired up.

**Fix:** Commands must be registered in a try-catch block that runs FIRST. Non-critical init (logging, validation) runs AFTER.

```csharp
// WRONG order (commands fail silently):
protected override async InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress) {
    await LoggerFactory.Initialize(); // If this throws, commands never register
    await LoadValidator.Validate();    // If this throws, commands never register
    RegisterCommands();               // Too late — may already have failed
}

// CORRECT order:
protected override async InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress) {
    try { RegisterCommands(); }
    catch { /* Cannot recover */ throw; }
    // Commands are now registered. Non-critical init below.
    try { await LoggerFactory.Initialize(); } catch { /* Log only */ }
    try { await LoadValidator.Validate(); } catch { /* Log only */ }
}
```

### 12.3 Missing DLLs in Extension Folder

**Symptom:** Activity log shows `FileNotFoundException` for `System.Text.Json` etc.

**Cause:** Transitive NuGet dependencies not copied to extension folder.

**Fix:** Inno Setup installer uses `*.dll` wildcard pattern to deploy ALL DLLs, not just the primary assembly.

### 12.4 VSCT CTO Cross-Contamination

**Symptom:** Build error — all projects look for `AkmlSqlVS2026.cto` regardless of which project is building.

**Cause:** Building via solution file causes VSCT to use the last project's CTO output path.

**Fix:** Build each shell project individually with MSBuild, never via `dotnet build` or solution-level build.

### 12.5 Cache Clearing Reference

**SSMS 20:**
```powershell
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SQL Server Management Studio\20.0_IsoShell\ComponentModelCache"
```

**SSMS 22:**
```powershell
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_05e71b86\ComponentModelCache"
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_05e71b86\MEFCacheBackup"
Remove-Item -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_05e71b86\privateregistry.bin*"
# Then from PowerShell (NOT Git Bash):
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\SSMS.exe" /updateconfiguration
```

### 12.6 Build Clean After SDK Change

When changing VS SDK versions, always clean:

```bash
# For each shell project
"$MSBUILD" "src/AkmlSql.Ssms20/AkmlSql.Ssms20.csproj" -t:Clean -p:Configuration=Release -v:quiet
Remove-Item -Recurse -Force "src/AkmlSql.Ssms20/bin"
Remove-Item -Recurse -Force "src/AkmlSql.Ssms20/obj"
```

### 12.7 Activity Log Encoding

SSMS activity logs are UTF-16LE encoded. Reading from Git Bash requires iconv:

```bash
iconv -f UTF-16LE -t UTF-8 "$env:APPDATA/Microsoft/SSMS/22.0_05e71b86/ActivityLog.xml" | grep -i "akml"
```

From PowerShell:

```powershell
Get-Content "$env:APPDATA\Microsoft\SSMS\22.0_05e71b86\ActivityLog.xml" -Encoding Unicode | Select-String "akml"
```

---

## Appendix A: File Locations at Runtime

| Component | Path |
|---|---|
| **Config** | `%AppData%/AKML SQL/config.json` |
| **Logs** | `%AppData%/AKML SQL/logs/akmlsql-*.log` |
| **Update result** | `%AppData%/AKML SQL/cache/update-available.json` |
| **Personal snippets** | `%AppData%/AKML SQL/snippets/` |
| **Formatting profiles** | `%AppData%/AKML SQL/profiles/` |
| **Engine cache** | `%LocalAppData%/AKML SQL/cache/` |

## Appendix B: Extension Paths by IDE

| IDE | Extension Path |
|---|---|
| SSMS 20 | `<Root>/Common7/IDE/Extensions/AkmlSql/` |
| SSMS 21 | `<Root>/Release/Common7/IDE/Extensions/AkmlSql/` |
| SSMS 22 | `<Root>/Release/Common7/IDE/Extensions/AkmlSql/` |
| VS 2019 | `<VS2019Root>/Common7/IDE/Extensions/AkmlSql/` |
| VS 2022 | `<VS2022Root>/Common7/IDE/Extensions/AkmlSql/` |
| VS 2026 | `<VS2026Root>/Common7/IDE/Extensions/AkmlSql/` |

## Appendix C: VS SDK Versions Reference

| Target | VS SDK Version | VSSDK.BuildTools | Shell Assembly Version |
|---|---|---|---|
| SSMS 20 | 15.9.3 | 15.* | 15.0.0.0 |
| VS 2019 | 16.0.208 | 16.* | 16.0.0.0 |
| SSMS 21 | 17.14.* | 17.* | 17.0.0.0 |
| SSMS 22 | 17.14.* | 17.* | 17.0.0.0 |
| VS 2022 | 17.14.* | 17.* | 17.0.0.0 |
| VS 2026 | 17.14.* | 17.* | 17.0.0.0 |

---

*End of Installation & Testing Guide — AKML SQL Phases 1–4*
