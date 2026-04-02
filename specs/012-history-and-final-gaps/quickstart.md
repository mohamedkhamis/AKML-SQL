# Quickstart: SQL History Enhancements & Final Parity Gaps

**Branch**: `012-history-and-final-gaps`

## Build & Test

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build -p:Configuration=Release -v:minimal
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
```

## Key Files

| Feature | File |
|---------|------|
| Starring retention | `src/AkmlSql.Engine/History/HistoryRetentionService.cs` |
| Rename queries | `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs` |
| Copy as IN | `src/AkmlSql.Shell.Shared/Productivity/Grid/GridCopyAsMenu.cs` |
| Unformat | `src/AkmlSql.Engine/Refactoring/Operations/Lightweight/UnformatOperation.cs` (new) |
| Search parser | `src/AkmlSql.Shell.Shared/History/HistorySearchParser.cs` (new) |
| Highlighting | `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs` |
| Version history | `src/AkmlSql.Engine/History/HistoryDatabase.cs` + shell UI |
