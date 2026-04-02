# Quickstart: SQL Prompt Core Parity — Remaining Gaps

**Branch**: `011-core-parity-remaining-gaps`

## Build & Test

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build -p:Configuration=Release -v:minimal
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
```

## Key Files

| Feature | File to Modify/Create |
|---------|----------------------|
| INSERT metadata | `src/AkmlSql.Engine/Refactoring/Operations/Lightweight/ExpandInsertColumnsOperation.cs` |
| sp_executesql | `src/AkmlSql.Engine/Refactoring/Operations/Lightweight/ConvertSpExecutesqlOperation.cs` (new) |
| Ctrl transparency | `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs` |
| Tab gradient | `src/AkmlSql.Shell.Shared/Tabs/TabColoringManager.cs` |
| Excel precision | `src/AkmlSql.Engine/Export/GridExportService.cs` |
| Split Table | `src/AkmlSql.Engine/Refactoring/Operations/Heavyweight/SplitTableOperation.cs` (new) |
