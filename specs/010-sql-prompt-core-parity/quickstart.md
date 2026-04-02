# Quickstart: SQL Prompt Core Feature Parity

**Branch**: `010-sql-prompt-core-parity`

## Prerequisites

- Visual Studio 2022 Enterprise with VS SDK workload
- .NET 10 SDK (for Engine/Updater)
- .NET Framework 4.7.2 targeting pack (for Shell projects)
- Inno Setup 7 (for installer, optional)

## Build

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"

# Build individual shell project (never use dotnet build for shell projects)
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build -p:Configuration=Release -v:minimal

# Build Engine
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64

# Run tests
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
```

## Key Directories for This Feature

```
src/AkmlSql.Shell.Shared/
  Safety/                    # ExecutionInterceptor, SafetyWarningDialog (exist)
  Tabs/                      # EnvironmentDetector (exists)
  Dialogs/                   # SettingsWindow, SettingsDialog (exist)
  Ui/                        # ThemeManager, OptionCategoryTreeBuilder (exist)
  Editor/Completion/         # AkmlCompletionPopup (exists, extend for definition box)
  Analysis/                  # LightbulbProvider, FixAction (exist, extend for actions list)
  Productivity/Grid/         # GridFeatureInitializer, GridAccessHelper (exist)
  Refactoring/               # SafeRenameCommand (stub), RefactoringPreviewDialog (stub)
  Snippets/                  # EMPTY — create SnippetManagerDialog here
  Commands/                  # DocumentOutlineCommand (stub)

src/AkmlSql.Engine/
  Safety/                    # SafetyCheckHandler (exists)
  Snippets/                  # Full engine (SnippetLoader, SnippetIndex, etc.)
  Refactoring/               # SafeRenameOperation (exists, fully implemented)
  Navigation/                # NavigationRequestHandler (exists)
  Completion/Providers/      # QuickInfoProvider (exists)

src/AkmlSql.Core/
  Config/                    # AppSettings, ConfigManager (exist)
  Ipc/Messages/              # All IPC DTOs (exist)
  Models/Safety/             # SafetyWarningType (exists)
```

## Implementation Order (by priority and dependency)

### Phase 1: P1 Features
1. **Execution Guard hookup** — Wire `ExecutionInterceptor.OnBeforeExecute()` to SSMS pre-execution event + add audit logging
2. **Snippet Manager dialog** — WPF dialog using ProfileEditorDialog pattern + menu command

### Phase 2: P2 Features
3. **Settings UI completion** — Extend SettingsWindow with pages for all 15 AppSettings sections
4. **Safe Rename shell** — Complete SafeRenameCommand + RefactoringPreviewDialog + script generation

### Phase 3: P3 Features
5. **Actions List** — Add refactoring actions to existing LightbulbProvider
6. **Grid sort/filter** — Add to GridFeatureInitializer
7. **Object Definition Box** — Secondary popup on AkmlCompletionPopup

### Phase 4: P4 Features
8. **Bookmarks** — IGlyphFactory margin glyphs + navigation commands
9. **Document Outline** — Implement engine handler + complete shell stub

## Testing

```bash
# Unit tests
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj

# Manual testing requires SSMS 22 with the extension installed
# Deploy: copy VSIX output to SSMS extensions directory
# Clear MEF cache after deployment
```
