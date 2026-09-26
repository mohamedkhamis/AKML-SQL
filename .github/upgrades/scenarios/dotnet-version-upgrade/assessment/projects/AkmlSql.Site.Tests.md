# tests\AkmlSql.Site.Tests\AkmlSql.Site.Tests.csproj

[← Back to the assessment index](../../assessment.md)

## Project Info

- **Current Target Framework:** net10.0
- **Proposed Target Framework:** net11.0
- **SDK-style**: True
- **Project Kind:** DotNetCoreApp
- **Dependencies**: 1
- **Dependants**: 0
- **Number of Files**: 62
- **Number of Files with Incidents**: 17
- **Lines of Code**: 10998
- **Estimated LOC to modify**: 34+ (at least 0.3% of the project)

## Related Projects

**Depends on (1)** — projects this one references:

- [c:\Repos\AKML\AKML-SQL\src\AkmlSql.Site\AkmlSql.Site.csproj](../projects/AkmlSql.Site.md)

## Dependency Graph

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart TB
    subgraph current["AkmlSql.Site.Tests.csproj"]
        MAIN["<b>📦&nbsp;AkmlSql.Site.Tests.csproj</b><br/><small>net10.0</small>"]
        click MAIN "../projects/AkmlSql.Site.Tests.md"
    end
    subgraph downstream["Dependencies (1)"]
        P9["<b>📦&nbsp;AkmlSql.Site.csproj</b><br/><small>net10.0</small>"]
        click P9 "../projects/AkmlSql.Site.md"
    end
    MAIN --> P9

```

## API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 0 | High - Require code changes |
| 🟡 Source Incompatible | 19 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 15 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 15802 |  |
| ***Total APIs Analyzed*** | ***15836*** |  |

## NuGet Package Issues

| Package | Current Version | Suggested Version | Severity | Issue |
| :--- | :---: | :---: | :---: | :--- |
| xunit | 2.9.3 | — | 🔵 Optional | NuGet package is deprecated |

Every project affected by these packages, and the versions the repository settles on: [aggregate NuGet packages](../nuget/aggregate-packages.md).

