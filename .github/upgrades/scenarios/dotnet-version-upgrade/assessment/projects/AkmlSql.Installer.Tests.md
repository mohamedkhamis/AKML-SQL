# tests\AkmlSql.Installer.Tests\AkmlSql.Installer.Tests.csproj

[← Back to the assessment index](../../assessment.md)

## Project Info

- **Current Target Framework:** net10.0
- **Proposed Target Framework:** net11.0
- **SDK-style**: True
- **Project Kind:** DotNetCoreApp
- **Dependencies**: 2
- **Dependants**: 0
- **Number of Files**: 9
- **Number of Files with Incidents**: 2
- **Lines of Code**: 669
- **Estimated LOC to modify**: 2+ (at least 0.3% of the project)

## Related Projects

**Depends on (2)** — projects this one references:

- [c:\Repos\AKML\AKML-SQL\src\AkmlSql.Core\AkmlSql.Core.csproj](../projects/AkmlSql.Core.md)
- [c:\Repos\AKML\AKML-SQL\src\AkmlSql.Updater\AkmlSql.Updater.csproj](../projects/AkmlSql.Updater.md)

## Dependency Graph

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart TB
    subgraph current["AkmlSql.Installer.Tests.csproj"]
        MAIN["<b>📦&nbsp;AkmlSql.Installer.Tests.csproj</b><br/><small>net10.0</small>"]
        click MAIN "../projects/AkmlSql.Installer.Tests.md"
    end
    subgraph downstream["Dependencies (2)"]
        P11["<b>📦&nbsp;AkmlSql.Updater.csproj</b><br/><small>net10.0</small>"]
        P4["<b>📦&nbsp;AkmlSql.Core.csproj</b><br/><small>netstandard2.0;net10.0</small>"]
        click P11 "../projects/AkmlSql.Updater.md"
        click P4 "../projects/AkmlSql.Core.md"
    end
    MAIN --> P11
    MAIN --> P4

```

## API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 0 | High - Require code changes |
| 🟡 Source Incompatible | 0 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 2 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 979 |  |
| ***Total APIs Analyzed*** | ***981*** |  |

## NuGet Package Issues

| Package | Current Version | Suggested Version | Severity | Issue |
| :--- | :---: | :---: | :---: | :--- |
| xunit | 2.9.3 | — | 🔵 Optional | NuGet package is deprecated |

Every project affected by these packages, and the versions the repository settles on: [aggregate NuGet packages](../nuget/aggregate-packages.md).

