# tests\AkmlSql.Formatting.Tests\AkmlSql.Formatting.Tests.csproj

[← Back to the assessment index](../../assessment.md)

## Project Info

- **Current Target Framework:** net10.0
- **Proposed Target Framework:** net11.0
- **SDK-style**: True
- **Project Kind:** DotNetCoreApp
- **Dependencies**: 1
- **Dependants**: 0
- **Number of Files**: 77
- **Number of Files with Incidents**: 30
- **Lines of Code**: 13991
- **Estimated LOC to modify**: 40+ (at least 0.3% of the project)

## Related Projects

**Depends on (1)** — projects this one references:

- [c:\Repos\AKML\AKML-SQL\src\AkmlSql.Formatting\AkmlSql.Formatting.csproj](../projects/AkmlSql.Formatting.md)

## Dependency Graph

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart TB
    subgraph current["AkmlSql.Formatting.Tests.csproj"]
        MAIN["<b>📦&nbsp;AkmlSql.Formatting.Tests.csproj</b><br/><small>net10.0</small>"]
        click MAIN "../projects/AkmlSql.Formatting.Tests.md"
    end
    subgraph downstream["Dependencies (1)"]
        P7["<b>📦&nbsp;AkmlSql.Formatting.csproj</b><br/><small>net10.0</small>"]
        click P7 "../projects/AkmlSql.Formatting.md"
    end
    MAIN --> P7

```

## API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 0 | High - Require code changes |
| 🟡 Source Incompatible | 39 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 1 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 16822 |  |
| ***Total APIs Analyzed*** | ***16862*** |  |

## NuGet Package Issues

| Package | Current Version | Suggested Version | Severity | Issue |
| :--- | :---: | :---: | :---: | :--- |
| xunit | 2.9.3 | — | 🔵 Optional | NuGet package is deprecated |

Every project affected by these packages, and the versions the repository settles on: [aggregate NuGet packages](../nuget/aggregate-packages.md).

