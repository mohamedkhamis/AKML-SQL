# AKML SQL — Development Progress Log

## Overview

This document tracks the development progress, complete feature inventory, issues encountered, root causes identified, and solutions applied during the AKML SQL extension development. It serves as institutional knowledge for future sessions.

---

## Complete Feature Inventory (as of 2026-04-02, updated with spec 011 features)

### 1. IntelliSense & Code Completion
- Custom dark-themed completion popup (replicates SQL Prompt design)
- **Ctrl transparency**: Hold Ctrl to make popup semi-transparent (see code behind)
- Auto-trigger on typing with configurable debounce delay
- Dot-trigger for table.column completion
- Fuzzy matching (substring + prefix)
- Column provider (CTEs, temp tables, derived tables, aliases)
- Alias provider with alias-qualified column resolution
- Object provider (schema-qualified tables, views, procedures, functions)
- Keyword provider with configurable casing (UPPER/lower/PascalCase/AsIs)
- Snippet integration in completion list
- Variable provider (@variables in scope)
- Quick Info tooltips (column types, nullability, PK/FK badges)
- Function signature help with parameter types
- Wildcard expansion popup (SELECT * to explicit column list)
- Object Definition Panel (Summary/Script tabs alongside completion popup)
- Auto alias suggestion on table completion
- JOIN assist (FK-based condition suggestions)
- Schema status indicator (cache load progress)

### 2. Code Formatting (20 commands)
- Format Document (Ctrl+K, Y)
- Format Selection
- Format on Save / Format on Paste / Format on Delimiter (semicolons/GO)
- Casing Only (apply keyword casing without structural changes)
- Expand Wildcards (SELECT * to column list)
- Expand Insert Columns (with **metadata comments**: type, nullability, defaults as inline comments) / Expand Update Columns / Expand Exec Parameters
- **Convert sp_executesql to Static SQL** (substitutes parameter values into template)
- Add GROUP BY Columns
- Insert Semicolons / Remove Semicolons
- Toggle Brackets (add/remove square brackets)
- Toggle AS Keywords
- Qualify Object Names (add dbo. schema prefix)
- Convert Old-Style JOINs to ANSI syntax
- Replace Deprecated Syntax
- Encapsulate in BEGIN/END
- Formatting profiles (.akmlstyle JSON files)
- SQL Prompt profile importer

### 3. Code Analysis (130+ rules across 8 categories)
- Real-time AST-based static analysis
- Diagnostic squiggles (wavy underlines)
- Error List integration
- Lightbulb quick-fix suggestions
- Analysis suppression (NOANALYZE comments)
- Rule categories:
  - **Best Practices (BP)**: 28 rules (@@IDENTITY, TRY/CATCH, NULL comparison, etc.)
  - **Performance (PE)**: 31 rules (SELECT *, NOCOUNT, leading wildcard LIKE, etc.)
  - **Security (SE)**: 20 rules (SQL injection, hard-coded passwords, xp_cmdshell, etc.)
  - **Style (ST)**: 24 rules (keyword casing, alias format, semicolons, etc.)
  - **Deprecated (DEP)**: 8 rules (old data types, old JOIN syntax, RAISERROR, etc.)
  - **Design (DE)**: 7 rules (missing PK, FLOAT for money, sql_variant, etc.)
  - **Execution (EX)**: 6 rules (division by zero, data truncation, unreachable code)
  - **Naming (NM)**: 6 rules (reserved words, sp_ prefix, Hungarian notation)

### 4. Code Refactoring (15 operations)
- **Heavyweight** (with preview dialog):
  - Extract to CTE
  - Extract to Derived Table
  - Extract to Stored Procedure (with parameter inference)
  - Safe Rename (cross-script, generates ALTER scripts)
  - Parameterize Values
  - Encapsulate as View
  - Convert Temp Table to Table Variable (or reverse)
- **Lightweight** (instant):
  - Remove Semicolons, Expand Insert/Update/Exec Columns
  - Add GROUP BY Columns, Encapsulate BEGIN/END
  - Replace Deprecated Syntax, Convert Old-Style JOINs
- Refactoring Preview Dialog with diff tree view
- Rename Script Generator (SQL script output, no direct DB execution)

### 5. Execution Safety Guard
- Intercepts query execution (F5) via DTE command hook
- DELETE without WHERE warning
- UPDATE without WHERE warning
- DROP TABLE/DATABASE confirmation
- TRUNCATE TABLE confirmation
- Environment-aware severity:
  - Production: type server name to confirm (case-sensitive)
  - Non-Production: simple Yes/No dialog
  - Configurable per environment
- Transaction Reminder (uncommitted transaction detection)
- Structured audit logging (server, environment, statement type, outcome)

### 6. Snippet Manager
- WPF Snippet Manager dialog (search, CRUD, import/export)
- Personal + Team + Built-in snippet sources
- Snippet variables ($CURSOR$, $SELECTEDTEXT$, $DATE$, $DBNAME$, etc.)
- Context filtering (global, after_select, after_from, etc.)
- Surround-with snippets (wraps selected text)
- Format on expand (optional)
- Usage tracking for ranking

### 7. SQL History
- SQLite-backed execution history recording
- History Tool Window with search and filtering
- History diff view (side-by-side comparison)
- Encryption at rest (optional)
- Configurable retention period and max entries
- Record failures (optional)
- Deduplication

### 8. Tab Management & Session Recovery
- Tab coloring by environment (Production=red, Staging=orange, Dev=green) with **optional gradient** (lighter top, base bottom)
- Custom window title template ({server} - {database})
- Restore Closed Tab (Ctrl+Shift+T)
- Pin Tab / Duplicate Tab / Close All Unmodified
- Session auto-save with configurable interval
- Session recovery on startup (always/prompt/never)

### 9. Results Grid Enhancements (15 features)
- Aggregate statistics (SUM, AVG, COUNT, MIN, MAX) in status bar
- Column statistics popup on header right-click
- NULL value highlighting
- Row numbers column
- Column sorting (3-click cycle: Asc/Desc/None)
- Column filtering (right-click popup with text filter)
- Grid Find bar
- Export to CSV, JSON, XML, SQL, Markdown, Excel (with **15+ digit precision** option — numbers exported as text to prevent rounding)
- Copy as JSON, XML, SQL INSERT, SQL VALUES
- Script Generator (INSERT/UPDATE/DELETE from rows)
- Cell Edit dialog (Ctrl+DoubleClick)
- Transpose Results view (rows as columns)
- Freeze column headers

### 10. Navigation & Bookmarks
- Go to Definition (F12)
- Peek Definition (Alt+F12) with inline popup
- Find All References (Shift+F12)
- Object Search (Ctrl+T) with fuzzy matching
- Navigate Matching Pair (Ctrl+]) — BEGIN/END, parentheses, TRY/CATCH
- Navigate Next/Previous Statement
- Bookmark Toggle (Ctrl+K, Ctrl+K) with blue margin glyphs
- Bookmark Next/Previous (Ctrl+K, Ctrl+N / Ctrl+K, Ctrl+P)
- Document Outline tool window (procedures, functions, CTEs, temp tables)

### 11. Editor Productivity
- Execute Current Statement (Alt+Enter)
- Execute to Cursor
- Highlight Occurrences of selected identifier
- Bracket Matching
- Named Regions (--region / --endregion code folding)
- Sticky Scroll (parent scope header pinning)
- Code Minimap

### 12. Settings System
- WPF Settings dialog with 15 category pages
- Dark/Light theme support
- Per-category Reset This Page / Reset All
- Export All Settings / Import Settings (JSON)
- 50+ configurable options across all feature areas

### 13. AI Assistance
- Multi-provider support (OpenAI, Anthropic, Gemini, Ollama, LM Studio, Custom)
- Text to SQL (natural language to T-SQL generation)
- AI Explain (query explanation in plain English)
- AI Fix (error correction suggestions)
- AI Optimize (performance optimization suggestions)
- AI Index Analysis (missing index suggestions)
- AI Chat Panel (multi-turn conversation with database context)
- Ghost Text Completion (inline AI suggestions, experimental)
- Privacy modes (schemaOnly, full, anonymous, offline, disabled)
- Privacy transformer (redacts sensitive data before sending)

### 14. Command Palette
- Fuzzy-search command launcher (Ctrl+Shift+P)
- 32+ registered commands
- Usage-based ranking

### 15. Schema Cache
- In-memory cache of database objects
- Phase A (fast): tables, views, procedures, row counts (<500ms)
- Phase B (background): columns, FKs, parameters
- Change detection via CHECKSUM_AGG polling
- DDL-triggered refresh
- LRU eviction for multiple databases
- Persistent cache to disk (optional)

### 16. Infrastructure
- Shared Project (.projitems) compiled into 6 shell targets
- Out-of-process Engine (.NET 10, self-contained)
- MessagePack IPC over named pipes (30+ message types)
- Serilog structured logging
- Atomic config writes (temp file + rename)
- Self-contained updater (.NET 10)
- Inno Setup 7 installer with environment scanner

### Test Coverage
- 451 unit tests (xunit, .NET 10)
- SafetyCheckHandler: 15 tests
- SnippetImport: 6 tests
- SafeRenameOperation: 6 tests
- DocumentOutline: 15 tests

---

## Development History

## Phase 1: Foundation and Installer

### Milestone 1: Project Scaffolding

**Status**: Complete

- Created solution with 6 shell extension projects (SSMS 20/21/22, VS 2019/2022/2026)
- Created shared project (`AkmlSql.Shell.Shared`) with menu commands, dialogs, status bar, update launcher, and load validator
- Created core library (`AkmlSql.Core`) with config manager, logging, update models
- Created self-contained updater (`AkmlSql.Updater`)
- Created Inno Setup 7 installer with environment scanner (registry + vswhere + filesystem fallback)
- Created Specify framework specs under `specs/001-phase1-foundation-installer/`

### Milestone 2: SSMS 20 Extension Loading

**Status**: Complete — verified working

#### Issue 1: Wrong Shell Assembly Version

- **Symptom**: Extension fails to load; activity log shows assembly binding failure
- **Root Cause**: VS SDK 16.0.208 references `Shell.15.0` version `16.0.0.0`, but SSMS 20 (VS 2017 IsolatedShell) ships `15.0.0.0`
- **Fix**: Downgraded VS SDK from `16.0.208` to `15.9.3` and VSSDK.BuildTools from `16.*` to `15.*`
- **Lesson**: Always clean `bin/obj` folders after SDK version changes — stale NuGet cache causes wrong assembly references

#### Issue 2: Menu Not Appearing — Missing CTO Resource

- **Symptom**: `HrLoadNativeUILibrary failed with 0x800a006f` in activity log
- **Root Cause**: SDK-style projects do not have a `.resx` file by default. Without `VSPackage.resx` with `<MergeWithCTO>true</MergeWithCTO>`, the VSCT-compiled CTO (`Menus.ctmenu`) is never embedded as a managed resource in the output DLL
- **Build Warning**: `VSSDK1205: There are no resources to merge the cto files into`
- **Fix**: Created `VSPackage.resx` in all 6 shell projects with `<EmbeddedResource Update="VSPackage.resx"><MergeWithCTO>true</MergeWithCTO><ManifestResourceName>VSPackage</ManifestResourceName></EmbeddedResource>` in each `.csproj`
- **Gotcha**: Must use `Update=` not `Include=` — SDK-style projects auto-include `.resx` files, causing `NETSDK1022: Duplicate EmbeddedResource`

#### Issue 3: Menu Appears But Clicks Do Nothing

- **Symptom**: AKML SQL menu visible, but clicking any item produces no response
- **Root Cause 1 — AsyncPackage**: SSMS 20 (VS 2017 shell) does not properly wire up command handlers when using `AsyncPackage` with background loading before menu clicks arrive
- **Fix**: Changed `AkmlSqlPackage` from `AsyncPackage` to synchronous `Package` for SSMS 20
- **Root Cause 2 — Initialization Order**: `LoggerFactory.Initialize()` or `LoadValidator.Validate()` threw exceptions that were silently caught, but this happened BEFORE command registration, so no commands were ever registered
- **Fix**: Reordered `Initialize()` to register all menu commands FIRST, then perform non-critical initialization (logging, validation, update check) in a separate try-catch
- **Root Cause 3 — Missing Dependency DLLs**: `System.Text.Json`, `System.Memory`, `System.Buffers`, and other transitive NuGet dependencies were not deployed to the extension folder
- **Fix**: Changed Inno Setup installer from per-file DLL listing to `*.dll` wildcard pattern for all 6 targets

#### Issue 4: VSCT CTO Cross-Contamination

- **Symptom**: Build errors — all projects look for `AkmlSqlVS2026.cto` regardless of which project is being built
- **Root Cause**: Building via the solution file causes VSCT to use the last project's CTO output path
- **Fix**: Build each shell project individually with MSBuild, never via solution-level build

#### Additional SSMS 20 Fixes

- **pkgdef**: Added `Menus` registration entry, set `AllowsBackgroundLoad=dword:00000000` (synchronous), autoload flags `dword:00000000`
- **vsixmanifest**: Uses Schema 2010 (`<Vsix>` root) with `<IsolatedShell Version="1.0">ssms</IsolatedShell>`
- **Command signatures**: Changed all 5 command classes from `AsyncPackage` parameter to `Package` parameter
- **MEF Cache**: Located at `%LocalAppData%/Microsoft/SQL Server Management Studio/20.0_IsoShell/ComponentModelCache/` (not under `VisualStudio`)

### Milestone 3: SSMS 22 Extension Loading

**Status**: Complete — verified working (menu under Tools, commands functional)

#### Issue 5: Extension Visible in Extension Manager But No Menu

- **Symptom**: AKML SQL appears in Extensions > Manage Extensions but no menu item in the top menu bar
- **Root Cause 1 — Wrong Extension Path**: Files were initially deployed to root-level `Common7/IDE/Extensions/AkmlSql/`, but SSMS 22 executable lives under `Release/Common7/IDE/` and loads extensions from `Release/Common7/IDE/Extensions/`
- **Fix**: Deploy to `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql\`

#### Issue 6: Wrong vsixmanifest InstallationTarget

- **Symptom**: Extension not recognized as compatible with SSMS
- **Root Cause**: vsixmanifest targeted `Microsoft.VisualStudio.Community/Pro/Enterprise` instead of `Microsoft.VisualStudio.Ssms`
- **Fix**: Changed to `<InstallationTarget Id="Microsoft.VisualStudio.Ssms" Version="[17.0,)" />` with `AllUsers="true"`
- **Applied to**: SSMS 21 and SSMS 22 vsixmanifest files

#### Issue 7: Package Never AutoLoads — Wrong UI Context

- **Symptom**: pkgdef is imported (visible in activity log) but `Begin package load [AkmlSqlPackage]` never appears; menu never renders
- **Root Cause**: AutoLoad registered for `{e8fbc700-a1bd-11d0-a67c-00a0c9110051}` (`UICONTEXT_ShellInitialized`) which is a standard VS context. SSMS 22 uses its own context: `{B7B07F42-6013-4C67-A504-C771CBC7625A}` (`UICONTEXT_SSMS`)
- **Evidence**: Found in `SSMS.Application.pkgdef`: `[$RootKey$\AutoLoadPackages\{B7B07F42-6013-4C67-A504-C771CBC7625A}] @="UICONTEXT_SSMS"`
- **Fix**: Changed `[ProvideAutoLoad]` attribute and pkgdef to use `{B7B07F42-6013-4C67-A504-C771CBC7625A}`
- **Status**: Verified working — applied to SSMS 21 and SSMS 22

#### Issue 8: PkgDef Cache Not Refreshing

- **Symptom**: Activity log shows `PkgDefCache fast check: timestamps are current` even after deploying new files
- **Root Cause**: The private registry hive (`privateregistry.bin`) caches pkgdef entries and the timestamp check doesn't detect new extension folders
- **Fix**: Delete `%LocalAppData%/Microsoft/SSMS/22.0_05e71b86/privateregistry.bin` (and `.LOG1`, `.LOG2`), plus clear `ComponentModelCache/`, `MEFCacheBackup/`, and CTM files
- **Alternative**: Run `SSMS.exe /updateconfiguration` from PowerShell (not Git Bash — path mangling converts `/updateconfiguration` to a file path)

#### Issue 9: Menu Not Visible — SSMS 22 Custom Menu Bar

- **Symptom**: Package loads successfully, menu commands visible in Customize dialog, but no "AKML SQL" menu in the top menu bar
- **Root Cause**: SSMS 22 uses a custom menu bar via `SSMSMnu.dll` that does NOT include the standard VS `guidSHLMainMenu:IDG_VS_MM_TOOLSADDINS` group. This group is where VS extensions traditionally place their top-level menus, but it has no visible parent in SSMS 22's menu hierarchy
- **Investigation**: Extracted native CTO from `SSMSMui.dll` (satellite of SSMS Menu Package `{B7B07F42-...}`). Confirmed `guidSHLMainMenu` GUID is absent from the SSMS CTM binary. The CFCT v5 format is compressed, preventing further analysis
- **Fix**: Added `<CommandPlacement>` in VSCT to additionally place the menu in `guidSHLMainMenu:IDG_VS_TOOLS_EXT_TOOLS`, which maps to the Tools menu in SSMS 22
- **Result**: "AKML SQL" appears as a submenu under the Tools menu
- **Applied to**: SSMS 21 and SSMS 22 VSCT files
- **Note**: Attempting to parent a group directly to `IDM_VS_MENU_BAR` (0x0001) caused the package to silently fail to load — the CTM merger appears to reject unknown parent references

#### Issue 10: Menu Clicks Do Nothing — Init Order (Same as SSMS 20 Issue 3)

- **Symptom**: Menu visible under Tools, but clicking any item (About, Options, etc.) produces no response
- **Root Cause**: Same as SSMS 20 Issue 3 — `InitializeAsync()` performed `LoggerFactory.Initialize()` and `LoadValidator.Validate()` BEFORE registering menu command handlers. If either threw an exception, the outer catch swallowed it and commands were never registered
- **Fix**: Reordered `InitializeAsync()` to register all menu commands FIRST (critical path), then perform non-critical initialization (logging, validation, status bar, update check) in a separate try-catch
- **Applied to**: All 6 shell extension projects (SSMS 20/21/22, VS 2019/2022/2026)
- **Status**: Verified working on SSMS 22

---

## SSMS Version Differences — Quick Reference

| Aspect | SSMS 20 | SSMS 21/22 |
|--------|---------|------------|
| VS Shell Base | VS 2017 IsolatedShell | VS 2022 Shell |
| Platform | x86 | x64 |
| Package Base Class | `Package` (synchronous) | `AsyncPackage` |
| vsixmanifest Schema | 2010 (`<Vsix>`) | 2011 v2.0 (`<PackageManifest>`) |
| InstallationTarget | `<IsolatedShell>ssms</IsolatedShell>` | `Microsoft.VisualStudio.Ssms` |
| AutoLoad Context | `{e8fbc700-...}` (ShellInitialized) | `{B7B07F42-...}` (UICONTEXT_SSMS) |
| Extension Path | `<Root>/Common7/IDE/Extensions/AkmlSql/` | `<Root>/Release/Common7/IDE/Extensions/AkmlSql/` |
| MEF Cache | `%LocalAppData%/Microsoft/SQL Server Management Studio/20.0_IsoShell/ComponentModelCache/` | `%LocalAppData%/Microsoft/SSMS/22.0_*/ComponentModelCache/` |
| Activity Log | `%AppData%/Microsoft/SQL Server Management Studio/20.0_IsoShell/ActivityLog.xml` | `%AppData%/Microsoft/SSMS/22.0_*/ActivityLog.xml` |
| Activity Log Encoding | UTF-16LE | UTF-16LE |
| VS SDK Version | 15.9.3 | 17.14.x |
| AllowsBackgroundLoad | 0 (disabled) | 1 (enabled) |

## Cache Clearing Procedures

### SSMS 20

```powershell
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SQL Server Management Studio\20.0_IsoShell\ComponentModelCache"
```

### SSMS 22

```powershell
# Full cache reset (required when adding new extensions)
Remove-Item -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_05e71b86\privateregistry.bin*"
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_05e71b86\ComponentModelCache"
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_05e71b86\MEFCacheBackup"
Remove-Item -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_05e71b86\1033\SSMS.CTM*"

# Then rebuild configuration (run from PowerShell, NOT Git Bash)
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\SSMS.exe" /updateconfiguration
```

## Deployment Procedures

### Manual Deployment (Development)

```powershell
# SSMS 20
$src = "src\AkmlSql.Ssms20"
$dest = "<SSMS20Root>\Common7\IDE\Extensions\AkmlSql"
Copy-Item "$src\bin\Release\net472\*.dll" $dest
Copy-Item "$src\AkmlSql.Ssms20.pkgdef" $dest
Copy-Item "$src\source.extension.vsixmanifest" "$dest\extension.vsixmanifest"

# SSMS 22
$src = "src\AkmlSql.Ssms22"
$dest = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql"
Copy-Item "$src\bin\Release\net472\*.dll" $dest
Copy-Item "$src\AkmlSql.Ssms22.pkgdef" $dest
Copy-Item "$src\source.extension.vsixmanifest" "$dest\extension.vsixmanifest"
```

### Debugging with Activity Log

```powershell
# Launch with logging enabled
& "<SSMS_EXE_PATH>" /log

# Activity log location (UTF-16LE encoded)
# SSMS 20: %AppData%\Microsoft\SQL Server Management Studio\20.0_IsoShell\ActivityLog.xml
# SSMS 22: %AppData%\Microsoft\SSMS\22.0_05e71b86\ActivityLog.xml

# Reading from Git Bash (requires iconv for UTF-16)
iconv -f UTF-16LE -t UTF-8 "<path>/ActivityLog.xml" | grep -i "akml"

# Reading from PowerShell
Get-Content "<path>\ActivityLog.xml" -Encoding Unicode | Select-String "akml"
```

---

## Diagnostic Tools Used

1. **Activity Log Analysis**: `SSMS.exe /log` generates XML activity log with package load events and errors
2. **Assembly Reference Inspector**: Custom .NET 4.72 console app using `Assembly.ReflectionOnlyLoadFrom()` to verify DLL assembly references and embedded resources
3. **Inline MessageBox Diagnostic**: Replaced entire package init with inline `MessageBox.Show()` handlers to confirm command wiring works, isolating the issue to initialization order
4. **PkgDef Search Path Inspection**: Activity log reveals `PkgDefSearchPath` entries showing exactly which directories SSMS scans for extensions

---

## Build: Analyzer CLI (Phase 5)

`AkmlSql.Analyzer` is a self-contained .NET 10 CLI tool for static SQL analysis in CI/CD pipelines.

### Build & Publish

```bash
dotnet publish src/AkmlSql.Analyzer/AkmlSql.Analyzer.csproj -c Release -r win-x64
# Output: src/AkmlSql.Analyzer/bin/Release/net10.0/win-x64/publish/AkmlSql.Analyzer.exe
```

### CLI Usage Examples

```bash
# Analyze a single file
AkmlSql.Analyzer.exe --file query.sql

# Analyze a directory recursively (exit 1 if any warnings found — for CI/CD)
AkmlSql.Analyzer.exe --directory scripts/ --recursive --check --severity warning

# Analyze with specific rules only
AkmlSql.Analyzer.exe --file query.sql --rules PE001,BP004,SE001

# Exclude rules
AkmlSql.Analyzer.exe --directory scripts/ --exclude-rules NM006,ST001

# JSON report (stdout) + file report
AkmlSql.Analyzer.exe --file query.sql --format json --report report.json

# With custom settings file
AkmlSql.Analyzer.exe --directory scripts/ --settings .casettings

# Show help / version
AkmlSql.Analyzer.exe --help
AkmlSql.Analyzer.exe --version
```

### Exit Codes

| Code | Meaning |
|------|---------|
| 0    | Clean — no violations at `--severity` level, or `--check` not specified |
| 1    | Violations found (only when `--check` is used) |
| 2    | Fatal error (parse failure, invalid args, missing file) |

### Importing SQL Prompt Settings

To convert an existing SQL Prompt `.casettings` XML file to AKML's JSON format:

```csharp
// In code (SqlPromptImporter.Convert returns the count of converted rules)
int count = AkmlSql.Engine.Analysis.SqlPromptImporter.Convert(
    xmlInputPath: "SqlPrompt.casettings",
    jsonOutputPath: ".casettings");
```

The importer maps 55 SQL Prompt rule IDs to their AKML equivalents. Unknown SQL Prompt rule IDs are logged and skipped.

### Configuring CAsettings in CI/CD

Place a `.casettings` file in the root of the SQL scripts directory. The analyzer walks up the directory tree to find the nearest file. Example `.casettings`:

```json
{
  "metadata": { "name": "CI Rules", "version": "1.0" },
  "rules": {
    "PE001": { "enabled": true, "severity": "error" },
    "NM006": { "enabled": false, "severity": "ignore" },
    "ST001": { "enabled": true, "severity": "warning" }
  },
  "globalSuppressions": [
    { "rule": "BP012", "reason": "Date literals intentional in migration scripts" }
  ]
}
```

GitHub Actions example:

```yaml
- name: SQL Static Analysis
  run: |
    AkmlSql.Analyzer.exe --directory sql/ --recursive --check --severity warning --report analysis-report.json
  continue-on-error: false
```

---

*Last updated: 2026-03-22*
