# AKML-SQL Installation & Testing Guide

**Version:** 1.0.0 | **Phases Covered:** 1–4 | **Last Updated:** 2026-03-20
**Author:** Abdulrahman Khamis

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Repository Setup](#2-repository-setup)
3. [Build Order & Commands](#3-build-order--commands)
4. [Phase 1 — Foundation & Installer](#4-phase-1--foundation--installer)
5. [Phase 2 — Core IntelliSense Engine](#5-phase-2--core-intellisense-engine)
6. [Phase 3 — SQL Formatter](#6-phase-3--sql-formatter)
7. [Phase 4 — Snippet Manager](#7-phase-4--snippet-manager)
8. [Installer Build & Testing](#8-installer-build--testing)
9. [Troubleshooting & Cache Clearing](#9-troubleshooting--cache-clearing)
10. [Quick Reference](#10-quick-reference)

---

## 1. Prerequisites

### Required Software

| Software | Version | Purpose |
|----------|---------|---------|
| **Visual Studio 2022 Enterprise** | 17.x | MSBuild for shell extension projects |
| **.NET 10 SDK** | 10.0.x | Engine, Formatter, Updater, Core, Tests |
| **Inno Setup 7** | 7.x | Installer compilation |
| **Git** | 2.x+ | Source control |
| **Git Bash** | (bundled with Git) | Shell for build commands |

### Optional (for testing extension loading)

| Software | Purpose |
|----------|---------|
| **SSMS 20** | Test SSMS 20 extension (x86, IsolatedShell) |
| **SSMS 21** | Test SSMS 21 extension (x64) |
| **SSMS 22** | Test SSMS 22 extension (x64) |
| **Visual Studio 2019** | Test VS 2019 extension (x86) |
| **Visual Studio 2022** | Test VS 2022 extension (x64) |
| **Visual Studio 2026** | Test VS 2026 extension (x64) |
| **SQL Server (any edition)** | Test IntelliSense, formatting, snippets against a real database |

### Verify Prerequisites

```bash
# Check .NET SDK
dotnet --list-sdks
# Should show 10.0.x

# Check MSBuild
"/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe" -version

# Check Inno Setup
"/c/Program Files/Inno Setup 7/ISCC.exe" /?
```

---

## 2. Repository Setup

```bash
# Clone and switch to the current development branch
git clone <repo-url> AKML-SQL
cd AKML-SQL
git checkout 004-snippet-manager

# Verify solution structure
ls src/
# Should list: AkmlSql.Core, AkmlSql.Engine, AkmlSql.Formatting, AkmlSql.Formatter,
#   AkmlSql.Shell.Shared, AkmlSql.Ssms20, AkmlSql.Ssms21, AkmlSql.Ssms22,
#   AkmlSql.VS2019, AkmlSql.VS2022, AkmlSql.VS2026, AkmlSql.Updater, AkmlSql.Installer

ls tests/
# Should list: AkmlSql.Core.Tests, AkmlSql.Engine.Tests, AkmlSql.Formatting.Tests
```

---

## 3. Build Order & Commands

### Critical Build Rules

1. **Never use `dotnet build` for shell extension projects** — they require full MSBuild for VSCT code generation
2. **Never build shell projects via the solution file** — causes VSCT CTO cross-contamination
3. **Always build each shell project individually**
4. **Always clean `bin/obj` after SDK version changes**

### Recommended Build Order

```
1. AkmlSql.Core            (dotnet build — shared library, must build first)
2. AkmlSql.Formatting      (dotnet build — formatter library)
3. AkmlSql.Engine           (dotnet publish — out-of-process engine)
4. AkmlSql.Updater          (dotnet publish — updater)
5. AkmlSql.Formatter        (dotnet publish — CLI formatter tool)
6. Shell Extensions × 6     (MSBuild — one at a time)
7. AkmlSql.Installer        (Inno Setup — after all above are built)
```

### Full Build Script

```bash
# Define MSBuild path
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"

# ── Step 1: Core Library ──
dotnet build src/AkmlSql.Core/AkmlSql.Core.csproj -c Release

# ── Step 2: Formatting Library ──
dotnet build src/AkmlSql.Formatting/AkmlSql.Formatting.csproj -c Release

# ── Step 3: Engine (publish for self-contained deployment) ──
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64

# ── Step 4: Updater ──
dotnet publish src/AkmlSql.Updater/AkmlSql.Updater.csproj -c Release

# ── Step 5: CLI Formatter ──
dotnet publish src/AkmlSql.Formatter/AkmlSql.Formatter.csproj -c Release

# ── Step 6: Shell Extensions (one at a time!) ──
# SSMS 20
"$MSBUILD" "src/AkmlSql.Ssms20/AkmlSql.Ssms20.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.Ssms20/AkmlSql.Ssms20.csproj" -t:Build -p:Configuration=Release -v:minimal

# SSMS 21
"$MSBUILD" "src/AkmlSql.Ssms21/AkmlSql.Ssms21.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.Ssms21/AkmlSql.Ssms21.csproj" -t:Build -p:Configuration=Release -v:minimal

# SSMS 22
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build -p:Configuration=Release -v:minimal

# VS 2019
"$MSBUILD" "src/AkmlSql.VS2019/AkmlSql.VS2019.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.VS2019/AkmlSql.VS2019.csproj" -t:Build -p:Configuration=Release -v:minimal

# VS 2022
"$MSBUILD" "src/AkmlSql.VS2022/AkmlSql.VS2022.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.VS2022/AkmlSql.VS2022.csproj" -t:Build -p:Configuration=Release -v:minimal

# VS 2026
"$MSBUILD" "src/AkmlSql.VS2026/AkmlSql.VS2026.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.VS2026/AkmlSql.VS2026.csproj" -t:Build -p:Configuration=Release -v:minimal

# ── Step 7: Run Tests ──
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj
dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj

# ── Step 8: Installer ──
"/c/Program Files/Inno Setup 7/ISCC.exe" src/AkmlSql.Installer/AkmlSqlSetup.iss
```

---

## 4. Phase 1 — Foundation & Installer

### What Phase 1 Delivers

- 6 shell extension VSPackages (SSMS 20/21/22, VS 2019/2022/2026)
- Shared project pattern (`AkmlSql.Shell.Shared`)
- Menu commands: About, Check for Updates, Options, Send Feedback, View Logs
- Configuration management (`%AppData%\AKML SQL\config.json`)
- Logging via Serilog (`%AppData%\AKML SQL\logs\`)
- Status bar integration
- Load validator
- Inno Setup installer with environment detection

### 4.1 Build the Core Library

```bash
dotnet build src/AkmlSql.Core/AkmlSql.Core.csproj -c Release
```

**Verify:** No errors. Output in `src/AkmlSql.Core/bin/Release/`.

### 4.2 Build the Updater

```bash
dotnet publish src/AkmlSql.Updater/AkmlSql.Updater.csproj -c Release
```

**Verify:** Self-contained exe produced in `src/AkmlSql.Updater/bin/Release/net10.0/win-x64/publish/`.

### 4.3 Build a Shell Extension (example: SSMS 22)

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"

"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build -p:Configuration=Release -v:minimal
```

**Verify:**
- VSIX file created: `src/AkmlSql.Ssms22/bin/Release/AkmlSql.Ssms22.vsix`
- No CTO or resource errors in build output

### 4.4 Test: Unit Tests

```bash
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
```

**Expected:** All tests pass. Tests cover:
- ConfigManager read/write/atomic operations
- LoggerFactory thread-safe initialization
- IPC frame protocol serialization/deserialization
- Update manifest parsing

### 4.5 Test: Manual Extension Loading (SSMS 22 Example)

#### Install the Extension

1. Copy the built extension files to the SSMS 22 extension directory:
   ```
   C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql\
   ```
   Files to copy from `src/AkmlSql.Ssms22/bin/Release/`:
   - `AkmlSql.Ssms22.dll`
   - `AkmlSql.Ssms22.pkgdef`
   - `AkmlSql.Core.dll`
   - All dependency DLLs (System.Text.Json, Serilog, MessagePack, etc.)
   - `extension.vsixmanifest`

2. Clear SSMS 22 caches (run in PowerShell as Admin):
   ```powershell
   Remove-Item -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_*\privateregistry.bin*"
   Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_*\ComponentModelCache"
   Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_*\MEFCacheBackup"
   ```

3. Run SSMS 22 with configuration update:
   ```powershell
   & "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\SSMS.exe" /updateconfiguration
   ```

4. Launch SSMS 22 normally.

#### Verify Phase 1 Features

| Test | Steps | Expected Result |
|------|-------|-----------------|
| **Package loads** | Open SSMS 22, check Activity Log | No package load errors for `{A1B2C3D4-1111-2222-3333-444455556666}` |
| **Menu appears** | Tools menu | "AKML SQL" submenu visible |
| **About dialog** | Tools → AKML SQL → About | Dialog shows version 1.0.0, author info |
| **Options dialog** | Tools → AKML SQL → Options | Settings dialog opens |
| **View Logs** | Tools → AKML SQL → View Logs | Opens log file location in Explorer |
| **Status bar** | Open any SQL file | Status bar shows AKML SQL status |
| **Config file** | Check `%AppData%\AKML SQL\config.json` | File exists with default settings |
| **Log file** | Check `%AppData%\AKML SQL\logs\` | Log file created with initialization messages |
| **Check for Updates** | Tools → AKML SQL → Check for Updates | Update check runs (may show "up to date" or error if no server) |
| **Send Feedback** | Tools → AKML SQL → Send Feedback | Opens feedback URL in browser |

#### SSMS 20 Differences

- Extension path: `<SSMS20Root>\Common7\IDE\Extensions\AkmlSql\`
- Uses synchronous `Package` (not `AsyncPackage`)
- MEF cache: `%LocalAppData%\Microsoft\SQL Server Management Studio\20.0_IsoShell\ComponentModelCache\`
- Clear cache:
  ```powershell
  Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SQL Server Management Studio\20.0_IsoShell\ComponentModelCache"
  ```

#### VS 2022 Differences

- Extension path: auto-installed via VSIX double-click
- Can also install via: `vsixinstaller.exe AkmlSql.VS2022.vsix`
- Alternatively, place files in `%LocalAppData%\Microsoft\VisualStudio\17.0_*\Extensions\AkmlSql\`

---

## 5. Phase 2 — Core IntelliSense Engine

### What Phase 2 Delivers

- Out-of-process IntelliSense engine (`AkmlSql.Engine`)
- Named-pipe RPC communication
- SQL parsing via `Microsoft.SqlServer.TransactSql.ScriptDom`
- Schema cache from live database connections
- Completion providers: Keywords, Objects (tables/views/procs), Columns
- Quick Info tooltips
- Signature Help for functions/procedures
- VS editor integration (MEF completion source, command handler)

### 5.1 Build the Engine

```bash
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64
```

**Verify:**
- Self-contained exe in `src/AkmlSql.Engine/bin/Release/net10.0/win-x64/publish/`
- Single-file output (trimmed)

### 5.2 Test: Unit Tests

```bash
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj
```

**Expected:** All tests pass. Tests cover:
- Keyword completion provider
- Object completion provider
- Column completion provider
- SQL parser/cursor context detection
- Schema cache operations
- RPC message routing

### 5.3 Test: Engine Standalone (without host)

You can test the engine process directly:

```bash
# Run the engine (it will wait for a named-pipe connection)
cd src/AkmlSql.Engine/bin/Release/net10.0/win-x64/publish/
./AkmlSql.Engine.exe
# The engine logs to stdout and %AppData%\AKML SQL\logs\
# Press Ctrl+C to stop
```

**Verify:**
- Engine starts without errors
- Named pipe created (check logs for pipe name)
- Engine shuts down cleanly on Ctrl+C

### 5.4 Test: IntelliSense in SSMS/VS

**Prerequisites:** Extension installed (Phase 1), Engine built and deployed.

1. Deploy engine exe alongside the extension (installer handles this normally)
2. Open SSMS 22 and connect to a SQL Server instance
3. Open a new query window

| Test | Steps | Expected Result |
|------|-------|-----------------|
| **Engine starts** | Open a SQL query window | Engine process starts (check Task Manager for `AkmlSql.Engine.exe`) |
| **Keyword completion** | Type `SEL` and trigger completion (Ctrl+Space) | Shows `SELECT`, `SET`, etc. |
| **Table completion** | Type `SELECT * FROM ` and trigger completion | Shows database tables/views |
| **Column completion** | Type `SELECT ` after `FROM dbo.TableName` | Shows columns for `TableName` |
| **Schema-qualified** | Type `dbo.` | Shows objects in `dbo` schema |
| **Quick Info** | Hover over a table name | Tooltip shows table info (columns, types) |
| **Signature Help** | Type a function name + `(` | Shows parameter info |
| **Proc completion** | Type `EXEC ` | Shows stored procedures |
| **Multi-database** | Switch databases in SSMS dropdown | Completion refreshes for new database |
| **Engine recovery** | Kill `AkmlSql.Engine.exe` in Task Manager | Engine restarts automatically on next completion request |

#### Troubleshooting IntelliSense

- **No completions:** Check `%AppData%\AKML SQL\logs\` for engine errors
- **Engine won't start:** Ensure engine exe is in the correct path relative to extension DLL
- **Wrong database objects:** Engine may need a schema refresh — disconnect and reconnect, or use Refresh Schema if available
- **Pipe connection errors:** Check if another engine instance is running (port/pipe conflict)

---

## 6. Phase 3 — SQL Formatter

### What Phase 3 Delivers

- SQL formatting engine (`AkmlSql.Formatting`) using ScriptDom
- Format Document command
- Format Selection command
- Format on Save (auto-format)
- Expand Wildcards (`SELECT *` → explicit columns)
- Qualify Names (add schema prefixes)
- Toggle Case (upper/lower keywords)
- Bulk Format Wizard (format multiple files)
- Formatting profiles (customizable rules)
- CLI formatter tool (`akmlsql-format`)

### 6.1 Build the Formatting Library

```bash
dotnet build src/AkmlSql.Formatting/AkmlSql.Formatting.csproj -c Release
```

### 6.2 Build the CLI Formatter

```bash
dotnet publish src/AkmlSql.Formatter/AkmlSql.Formatter.csproj -c Release
```

**Verify:** `akmlsql-format.exe` in publish output directory.

### 6.3 Test: Unit Tests

```bash
dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj
```

**Expected:** All tests pass. Tests cover:
- Keyword casing rules (UPPER, lower, PascalCase)
- Indentation rules
- Line break rules
- Comma placement (before/after)
- Alignment rules
- SELECT formatting
- JOIN formatting
- WHERE clause formatting
- Subquery formatting
- CTE formatting
- INSERT/UPDATE/DELETE formatting
- CREATE TABLE/VIEW/PROC formatting
- Profile application
- Selection-based formatting
- Edge cases (empty input, comments, string literals)

### 6.4 Test: CLI Formatter

```bash
# Format a single file
./akmlsql-format format input.sql -o output.sql

# Format with a specific profile
./akmlsql-format format input.sql --profile compact

# Format from stdin
echo "select * from dbo.mytable where id=1" | ./akmlsql-format format -

# Check formatting (dry-run)
./akmlsql-format check input.sql
```

**Verify:**
- Output SQL is properly formatted
- Keywords uppercased (default profile)
- Indentation applied
- Line breaks at clause boundaries

### 6.5 Test: Formatting in SSMS/VS

**Prerequisites:** Extension and Engine installed and working.

| Test | Steps | Expected Result |
|------|-------|-----------------|
| **Format Document** | Open a messy SQL file → Tools → AKML SQL → Format Document (or Ctrl+K, Ctrl+D) | Entire document formatted |
| **Format Selection** | Select a portion of SQL → Format Selection (or Ctrl+K, Ctrl+F) | Only selection formatted |
| **Format on Save** | Enable in Options → Edit SQL → Save (Ctrl+S) | SQL auto-formatted on save |
| **Expand Wildcards** | Place cursor on `SELECT *` → Expand Wildcards command | `*` replaced with explicit column list |
| **Qualify Names** | Place cursor on unqualified table → Qualify Names command | Schema prefix added (e.g., `dbo.`) |
| **Toggle Case** | Select keywords → Toggle Case command | Keywords toggle between UPPER/lower |
| **Bulk Format** | Tools → AKML SQL → Bulk Format | Wizard opens, select files, format all |
| **Profile switching** | Options → Formatting → Change profile | Formatting style changes per profile |

#### Formatting Profile Settings to Verify

| Setting | Default | What to Test |
|---------|---------|--------------|
| Keyword case | UPPER | `select` → `SELECT` |
| Indent style | Spaces (4) | Consistent indentation |
| Comma position | Before | Commas at start of line in SELECT |
| Max line width | 120 | Long lines wrapped |
| JOIN format | Aligned | JOIN keywords aligned |
| AND/OR position | Start of line | Boolean operators at line start |
| Parentheses | Aligned | Opening/closing parens aligned |

---

## 7. Phase 4 — Snippet Manager

### What Phase 4 Delivers (Foundation — T001-T020)

- Snippet data models (Snippet, SnippetMetadata, SnippetVariable, SnippetSource)
- Snippet file format (`.akmlsnippet` JSON)
- Multi-source snippet loading (built-in, personal, team, community)
- In-memory snippet index (lookup by shortcode, category, ID)
- Placeholder parser (`$VARIABLE$` syntax)
- Built-in variable resolver (`$DATE$`, `$TIME$`, `$USER$`, `$DATABASE$`, etc.)
- Snippet completion provider (IntelliSense integration)
- IPC message contracts (list, expand, save, import)
- RPC request handler

### 7.1 Build Engine with Snippet Support

```bash
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64
```

### 7.2 Test: Unit Tests

```bash
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj
```

**Look for snippet-related tests covering:**
- SnippetLoader — loading from multiple directories
- SnippetIndex — shortcode lookups, category filtering
- PlaceholderParser — `$VAR$` parsing, nested variables, edge cases
- BuiltInVariableResolver — `$DATE$`, `$USER$`, `$DATABASE$` expansion
- SnippetProvider — completion items from snippets
- SnippetRequestHandler — RPC message routing

### 7.3 Test: Snippet File Format

Create a test snippet file at `%AppData%\AKML SQL\snippets\test-snippet.akmlsnippet`:

```json
{
  "metadata": {
    "id": "test-select-top",
    "shortcode": "seltop",
    "name": "SELECT TOP N",
    "description": "Select top N rows from a table",
    "author": "Test User",
    "version": "1.0.0",
    "category": "DML",
    "tags": ["select", "top", "query"],
    "context": "Query"
  },
  "variables": [
    {
      "name": "COUNT",
      "default": "100",
      "tooltip": "Number of rows to return"
    },
    {
      "name": "TABLE",
      "default": "dbo.MyTable",
      "tooltip": "Table name",
      "schemaAware": "Table"
    },
    {
      "name": "DATABASE",
      "default": "",
      "tooltip": "Current database (auto-filled)"
    }
  ],
  "body": [
    "-- Query from $DATABASE$",
    "SELECT TOP ($COUNT$)",
    "    *",
    "FROM $TABLE$",
    "ORDER BY 1;"
  ]
}
```

**Verify:**
- Engine loads the snippet on startup (check logs)
- Snippet appears in index under shortcode `seltop` and category `DML`

### 7.4 Test: Placeholder Parsing

The placeholder parser should handle:

| Input | Parsed Variables |
|-------|-----------------|
| `$TABLE$` | `TABLE` |
| `$SCHEMA$.$TABLE$` | `SCHEMA`, `TABLE` |
| `SELECT $COLUMNS$ FROM $TABLE$` | `COLUMNS`, `TABLE` |
| `$DATE$ -- $USER$` | `DATE`, `USER` |
| `$$escaped$$` | (no variables — escaped dollar signs) |
| `Text without variables` | (empty list) |

### 7.5 Test: Built-in Variable Resolution

| Variable | Expected Value |
|----------|---------------|
| `$DATE$` | Current date (e.g., `2026-03-20`) |
| `$TIME$` | Current time (e.g., `14:30:00`) |
| `$DATETIME$` | Current date+time |
| `$USER$` | Windows username |
| `$MACHINE$` | Computer name |
| `$DATABASE$` | Connected database name |
| `$SERVER$` | Connected server name |
| `$GUID$` | New GUID |

### 7.6 Test: Snippet Completion (when UI is connected)

| Test | Steps | Expected Result |
|------|-------|-----------------|
| **Shortcode trigger** | Type `seltop` + trigger completion | Snippet "SELECT TOP N" appears in completion list |
| **Category filter** | Request DML category snippets | Only DML snippets returned |
| **Snippet expand** | Select snippet from completion | Snippet body inserted with placeholders |
| **Variable resolution** | Expand snippet with `$DATE$` | Date variable replaced with current date |
| **Tab navigation** | After expansion, press Tab | Cursor moves between placeholder positions |

### 7.7 Snippet Storage Paths

| Source | Path | Purpose |
|--------|------|---------|
| **Built-in** | `<install-dir>\snippets\` | Ships with installer, read-only |
| **Personal** | `%AppData%\AKML SQL\snippets\` | User-created snippets |
| **Team** | Configurable via `config.json` | Shared team snippets |
| **Community** | Future (online repository) | Community-contributed |

---

## 8. Installer Build & Testing

### 8.1 Build the Installer

**Prerequisites:** All projects built (Steps 1–6 from Section 3).

```bash
"/c/Program Files/Inno Setup 7/ISCC.exe" src/AkmlSql.Installer/AkmlSqlSetup.iss
```

**Output:** `src/AkmlSql.Installer/Output/AKMLSQLSetup.exe`

### 8.2 Test: GUI Installation

1. Run `AKMLSQLSetup.exe`
2. Accept the license agreement
3. The installer auto-detects installed targets:

| Detection | Method |
|-----------|--------|
| SSMS 20 | Registry + filesystem scan |
| SSMS 21 | Registry + filesystem scan |
| SSMS 22 | Registry + filesystem scan |
| VS 2019 | vswhere.exe + registry |
| VS 2022 | vswhere.exe + registry |
| VS 2026 | vswhere.exe + registry |

4. Select which targets to install
5. Complete installation

**Verify after GUI install:**

| Check | Expected |
|-------|----------|
| Extension DLLs deployed | Files exist in each target's extension directory |
| Engine deployed | `AkmlSql.Engine.exe` in install directory |
| Updater deployed | Updater exe in install directory |
| Config created | `%AppData%\AKML SQL\config.json` exists (if first install) |
| MEF caches cleared | Component model caches deleted for each target |
| Start menu shortcuts | AKML SQL shortcuts created (if configured) |

### 8.3 Test: Silent Installation

```bash
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /TARGETS=22,2022 /NOUPDATE
```

| Parameter | Meaning |
|-----------|---------|
| `/VERYSILENT` | No UI, no progress bar |
| `/ACCEPTEULA` | Auto-accept license |
| `/TARGETS=22,2022` | Install only for SSMS 22 and VS 2022 |
| `/NOUPDATE` | Disable auto-update check |

### 8.4 Test: Uninstallation

1. Run uninstaller from Control Panel or `unins000.exe`
2. **Verify:**
   - Extension files removed from all target directories
   - Engine and updater removed
   - Config and logs preserved (not deleted — user data)
   - MEF caches cleared

---

## 9. Troubleshooting & Cache Clearing

### 9.1 SSMS 20 Cache Clear

```powershell
# PowerShell (run as Admin if needed)
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SQL Server Management Studio\20.0_IsoShell\ComponentModelCache"
```

Then restart SSMS 20.

### 9.2 SSMS 22 Full Cache Clear

```powershell
# PowerShell (run as Admin)

# 1. Close SSMS 22 completely

# 2. Delete all caches
Remove-Item -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_05e71b86\privateregistry.bin*"
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_05e71b86\ComponentModelCache"
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_05e71b86\MEFCacheBackup"
Remove-Item -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_05e71b86\1033\SSMS.CTM*"

# 3. Rebuild configuration (run from PowerShell, NOT Git Bash)
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\SSMS.exe" /updateconfiguration
```

### 9.3 VS 2022 Cache Clear

```powershell
# Close VS 2022, then:
# Clear MEF cache
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\VisualStudio\17.0_*\ComponentModelCache"

# Reset experimental instance (if using debug)
# From Developer Command Prompt:
devenv /resetuserdata /rootsuffix Exp
```

### 9.4 Common Issues

| Issue | Cause | Fix |
|-------|-------|-----|
| **Package failed to load** | Missing dependencies or wrong assembly versions | Rebuild with correct SDK, redeploy all DLLs |
| **No menu items** | CTO not embedded or VSCT error | Ensure `VSPackage.resx` has `MergeWithCTO=true`, rebuild |
| **Menu visible but commands fail** | Initialization order issue | Commands must register before logging/validation init |
| **IntelliSense not working** | Engine not starting or pipe connection failure | Check logs at `%AppData%\AKML SQL\logs\` |
| **Wrong completions** | Stale schema cache | Disconnect and reconnect to refresh schema |
| **Formatting does nothing** | Engine not running or formatting disabled | Check engine process, check Options → Formatting enabled |
| **Snippet not loading** | Invalid JSON or wrong file path | Validate JSON, check snippet directory path in config |
| **VSCT CTO cross-contamination** | Built multiple shell projects together | Clean all `obj/bin` folders, rebuild one at a time |
| **Assembly version mismatch** | Wrong VS SDK version for target | Check SDK version table in CLAUDE.md |
| **`HrLoadNativeUILibrary` error** | Missing `VSPackage.resx` | Add resource file with `MergeWithCTO=true` |

### 9.5 Checking Logs

```bash
# View latest log
ls -la "$APPDATA/AKML SQL/logs/"

# Tail the log
tail -f "$APPDATA/AKML SQL/logs/akmlsql-$(date +%Y%m%d).log"
```

### 9.6 Checking Activity Logs

| Host | Activity Log Path |
|------|-------------------|
| SSMS 20 | `%AppData%\Microsoft\SQL Server Management Studio\20.0_IsoShell\ActivityLog.xml` |
| SSMS 22 | `%AppData%\Microsoft\SSMS\22.0_*\ActivityLog.xml` |
| VS 2022 | Launch with `/log`, then check `%AppData%\Microsoft\VisualStudio\17.0_*\ActivityLog.xml` |

To enable activity logging in VS/SSMS:
```bash
# VS 2022
devenv.exe /log

# SSMS 22
SSMS.exe /log
```

---

## 10. Quick Reference

### Build Commands Cheat Sheet

| Component | Command |
|-----------|---------|
| **Core** | `dotnet build src/AkmlSql.Core/AkmlSql.Core.csproj -c Release` |
| **Formatting** | `dotnet build src/AkmlSql.Formatting/AkmlSql.Formatting.csproj -c Release` |
| **Engine** | `dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64` |
| **Updater** | `dotnet publish src/AkmlSql.Updater/AkmlSql.Updater.csproj -c Release` |
| **CLI Formatter** | `dotnet publish src/AkmlSql.Formatter/AkmlSql.Formatter.csproj -c Release` |
| **SSMS 20** | `"$MSBUILD" src/AkmlSql.Ssms20/AkmlSql.Ssms20.csproj -t:Restore,Build -p:Configuration=Release` |
| **SSMS 21** | `"$MSBUILD" src/AkmlSql.Ssms21/AkmlSql.Ssms21.csproj -t:Restore,Build -p:Configuration=Release` |
| **SSMS 22** | `"$MSBUILD" src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Restore,Build -p:Configuration=Release` |
| **VS 2019** | `"$MSBUILD" src/AkmlSql.VS2019/AkmlSql.VS2019.csproj -t:Restore,Build -p:Configuration=Release` |
| **VS 2022** | `"$MSBUILD" src/AkmlSql.VS2022/AkmlSql.VS2022.csproj -t:Restore,Build -p:Configuration=Release` |
| **VS 2026** | `"$MSBUILD" src/AkmlSql.VS2026/AkmlSql.VS2026.csproj -t:Restore,Build -p:Configuration=Release` |
| **Tests** | `dotnet test tests/AkmlSql.Core.Tests && dotnet test tests/AkmlSql.Engine.Tests && dotnet test tests/AkmlSql.Formatting.Tests` |
| **Installer** | `"/c/Program Files/Inno Setup 7/ISCC.exe" src/AkmlSql.Installer/AkmlSqlSetup.iss` |

### Runtime Paths

| Path | Purpose |
|------|---------|
| `%AppData%\AKML SQL\config.json` | Configuration |
| `%AppData%\AKML SQL\logs\` | Log files |
| `%AppData%\AKML SQL\cache\` | Update cache |
| `%AppData%\AKML SQL\snippets\` | Personal snippets |

### GUIDs

| GUID | Purpose |
|------|---------|
| `{A1B2C3D4-1111-2222-3333-444455556666}` | Package GUID (all targets) |
| `{A1B2C3D4-1111-2222-3333-444455557777}` | Command Set GUID |
| `{e8fbc700-a1bd-11d0-a67c-00a0c9110051}` | ShellInitialized (SSMS 20, VS) |
| `{B7B07F42-6013-4C67-A504-C771CBC7625A}` | UICONTEXT_SSMS (SSMS 21/22) |

### VS SDK Versions

| Target | VS SDK | Platform |
|--------|--------|----------|
| SSMS 20 | 15.9.3 | x86 |
| VS 2019 | 16.0.208 | x86 |
| SSMS 21/22, VS 2022/2026 | 17.14.* | x64 |

---

*End of guide. For detailed development history and issue resolution, see `doc/progress.md`.*
