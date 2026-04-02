# Quickstart: SQL History & Tab Management

**Feature**: 007-sql-history-tabs | **Date**: 2026-03-24

## Prerequisites

- Visual Studio 2022 Enterprise (for building shell extensions with MSBuild)
- .NET 10 SDK (for engine and tests)
- Inno Setup 7 (for installer, optional)
- SSMS 22 (for manual testing)

## New Dependencies to Add

### Engine (`src/AkmlSql.Engine/AkmlSql.Engine.csproj`)

```xml
<PackageReference Include="Microsoft.Data.Sqlite" Version="9.*" />
```

### Core (`src/AkmlSql.Core/AkmlSql.Core.csproj`)

No new dependencies. `System.Security.Cryptography.ProtectedData` is available via .NET Framework 4.7.2 (shell) and .NET 10 (engine) — add the NuGet polyfill only for netstandard2.0 if needed:

```xml
<PackageReference Include="System.Security.Cryptography.ProtectedData" Version="9.*"
                  Condition="'$(TargetFramework)' == 'netstandard2.0'" />
```

## Build & Test

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"

# Build Core (shared library)
dotnet build src/AkmlSql.Core/AkmlSql.Core.csproj -c Release

# Build Engine (includes new History, Sessions, Safety handlers)
dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release

# Build a shell extension (e.g., SSMS 22) — must use MSBuild
"$MSBUILD" src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Build -p:Configuration=Release -v:minimal

# Run tests
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj

# Publish engine (required before installer)
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64
```

## Key Implementation Entry Points

### 1. Add New IPC Message Types

**File**: `src/AkmlSql.Core/Ipc/RpcMessage.cs`

Add constants in the 40-56 (shell→engine) and 140-156 (engine→shell) ranges, following the existing pattern (see ranges 1-31 and 101-131).

### 2. Add New IPC Message POCOs

**Directory**: `src/AkmlSql.Core/Ipc/Messages/`

Create `[MessagePackObject]` classes following the existing pattern (e.g., `FormatRequest.cs`). Each property uses `[Key(n)]` attributes for MessagePack serialization.

### 3. Add New Configuration Sections

**File**: `src/AkmlSql.Core/Config/AppSettings.cs`

Add `HistorySettings`, `TabSettings`, `SafetySettings` nested classes with safe defaults, following the existing pattern (e.g., `IntelliSenseSettings`, `CacheSettings`).

### 4. Register Message Handlers in Engine

**File**: `src/AkmlSql.Engine/Server/PipeRpcServer.cs`

Add `case` branches in the message dispatch switch for new message types (40-56), routing to the new handler classes.

### 5. Register New Commands in Shell

**File**: `src/AkmlSql.Shell.Shared/AkmlSqlPackage.cs` (via shared .projitems)

Add `Initialize()` calls for new commands (HistoryPanelCommand, RestoreClosedTabCommand, etc.) in the `InitializeAsync` method, following the existing command registration pattern.

### 6. Add VSCT Entries

**Files**: `src/AkmlSql.Ssms*/AkmlSql*.vsct` (one per target)

Add `<Button>`, `<Group>`, and `<KeyBinding>` entries for new commands (Ctrl+Alt+H, Ctrl+Shift+T), following the existing VSCT structure.

### 7. Create History SQLite Database

**New file**: `src/AkmlSql.Engine/History/HistoryDatabase.cs`

Initialize SQLite database with the schema from `data-model.md`. Use WAL mode and busy timeout for concurrent access.

### 8. Create History Tool Window

**New files**: `src/AkmlSql.Shell.Shared/History/HistoryToolWindow.cs`, `HistoryToolWindowControl.xaml`

Implement `IVsWindowPane` for a dockable tool window with WPF content, following the VS SDK tool window pattern.

## File Organization Convention

New files follow the existing project structure:
- **Models/entities** → `src/AkmlSql.Core/Models/{Category}/`
- **IPC messages** → `src/AkmlSql.Core/Ipc/Messages/`
- **Engine handlers** → `src/AkmlSql.Engine/{Category}/`
- **Shell commands** → `src/AkmlSql.Shell.Shared/Commands/`
- **Shell UI** → `src/AkmlSql.Shell.Shared/{Category}/`
- **Tests** → `tests/AkmlSql.Core.Tests/{Category}/`

## Manual Testing Checklist

1. **History recording**: Execute queries in SSMS, verify entries appear in History panel
2. **History search**: Search for keywords, filter by server/database/status/date
3. **Tab coloring**: Connect to servers matching PROD/DEV/STG patterns, verify colors
4. **Session recovery**: Force-kill SSMS process, restart, verify recovery dialog
5. **Ctrl+Shift+T**: Close tabs, verify they reopen in reverse order
6. **Safety warnings**: Execute DELETE without WHERE, DROP TABLE on production server
7. **Transaction reminder**: Execute BEGIN TRAN, wait for status bar indicator and popup
