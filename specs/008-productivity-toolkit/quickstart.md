# Quickstart: Productivity Toolkit

**Feature**: 008-productivity-toolkit | **Date**: 2026-03-24

## Prerequisites

- Visual Studio 2022 Enterprise (for MSBuild shell builds)
- .NET 10 SDK (for engine and tests)
- SSMS 22 (for manual testing)
- Inno Setup 7 (for installer, optional)

## New Dependencies to Add

### Engine (`src/AkmlSql.Engine/AkmlSql.Engine.csproj`)

```xml
<PackageReference Include="ClosedXML" Version="0.104.*" />
```

### Shell (optional, for toast notifications)

```xml
<!-- Add to Core if needed for toast notifications on netstandard2.0 -->
<PackageReference Include="Microsoft.Toolkit.Uwp.Notifications" Version="7.*"
                  Condition="'$(TargetFramework)' == 'netstandard2.0'" />
```

## Build & Test

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"

# Build Core
dotnet build src/AkmlSql.Core/AkmlSql.Core.csproj -c Release

# Build Engine
dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release

# Build SSMS 22 shell
"$MSBUILD" src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Build -p:Configuration=Release -v:minimal

# Run tests
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
```

## Key Implementation Entry Points

### 1. Add New IPC Message Types
**File**: `src/AkmlSql.Core/Ipc/RpcMessage.cs`
Add constants 60-68 (shell→engine) and 160-168 (engine→shell).

### 2. Add Configuration Sections
**File**: `src/AkmlSql.Core/Config/AppSettings.cs`
Add `GridSettings`, `EditorProductivitySettings`, `ExecutionProductivitySettings`, `NavigationSettings`.

### 3. Grid Features (Shell-Only)
**Directory**: `src/AkmlSql.Shell.Shared/Productivity/Grid/`
Hook into SSMS DataGridView for find, aggregates, export, copy-as.

### 4. Command Palette
**Directory**: `src/AkmlSql.Shell.Shared/Productivity/CommandPalette/`
WPF popup, command registry, fuzzy search.

### 5. Editor Taggers (Shell-Only MEF)
**Directory**: `src/AkmlSql.Shell.Shared/Editor/`
New ITagger implementations for highlights, brackets, regions.

### 6. Navigation (Engine-Side)
**Directory**: `src/AkmlSql.Engine/Navigation/`
Object definition retrieval, reference collection, object search.

## Manual Testing Checklist

1. **Find in Grid**: Execute query, Ctrl+F in grid, search text, verify highlights
2. **Grid Aggregates**: Select numeric cells, verify SUM/AVG/COUNT in status bar
3. **Copy As**: Right-click grid, Copy As > JSON, paste and verify
4. **Export to Excel**: Export full result set, open .xlsx and verify
5. **Command Palette**: Ctrl+Shift+P, type "format", select Format SQL
6. **Execute Current Statement**: Script with 3 SELECTs, cursor in 2nd, Alt+Enter
7. **Document Outline**: Open large script, verify outline tree, click to navigate
8. **Highlight Occurrences**: Click on @variable, verify all occurrences highlight
9. **Bracket Matching**: Cursor on BEGIN, verify matching END highlighted
10. **Go to Definition**: F12 on table name, verify CREATE script opens
11. **Peek Definition**: Alt+F12 on procedure name, verify inline preview
12. **Find All References**: Shift+F12 on table, verify referencing objects listed
13. **Named Regions**: Add --region/--endregion, verify collapsible
14. **Execution Timer**: Execute long query, verify live timer in status bar
15. **Toast Notification**: Set threshold to 5s, run 6s query, verify notification
