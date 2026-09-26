# src\AkmlSql.AI\AkmlSql.AI.csproj

[← Back to the assessment index](../../assessment.md)

## Project Info

- **Current Target Framework:** net10.0
- **Proposed Target Framework:** net11.0
- **SDK-style**: True
- **Project Kind:** ClassLibrary
- **Dependencies**: 2
- **Dependants**: 7
- **Number of Files**: 17
- **Number of Files with Incidents**: 3
- **Lines of Code**: 2646
- **Estimated LOC to modify**: 18+ (at least 0.7% of the project)

## Related Projects

**Depends on (2)** — projects this one references:

- [c:\Repos\AKML\AKML-SQL\src\AkmlSql.Core\AkmlSql.Core.csproj](../projects/AkmlSql.Core.md)
- [c:\Repos\AKML\AKML-SQL\src\AkmlSql.IntelliSense\AkmlSql.IntelliSense.csproj](../projects/AkmlSql.IntelliSense.md)

**Depended on by (7)** — projects that reference this one:

- [c:\Repos\AKML\AKML-SQL\src\AkmlSql.Analyzer\AkmlSql.Analyzer.csproj](../projects/AkmlSql.Analyzer.md)
- [c:\Repos\AKML\AKML-SQL\src\AkmlSql.Engine\AkmlSql.Engine.csproj](../projects/AkmlSql.Engine.md)
- [c:\Repos\AKML\AKML-SQL\src\AkmlSql.Web\AkmlSql.Web.csproj](../projects/AkmlSql.Web.md)
- [c:\Repos\AKML\AKML-SQL\tests\AkmlSql.AI.Tests\AkmlSql.AI.Tests.csproj](../projects/AkmlSql.AI.Tests.md)
- [c:\Repos\AKML\AKML-SQL\tests\AkmlSql.Core.Tests\AkmlSql.Core.Tests.csproj](../projects/AkmlSql.Core.Tests.md)
- [c:\Repos\AKML\AKML-SQL\tests\AkmlSql.Engine.Tests\AkmlSql.Engine.Tests.csproj](../projects/AkmlSql.Engine.Tests.md)
- [c:\Repos\AKML\AKML-SQL\tests\AkmlSql.Web.Tests\AkmlSql.Web.Tests.csproj](../projects/AkmlSql.Web.Tests.md)

## Dependency Graph

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart TB
    subgraph upstream["Dependants (7)"]
        P3["<b>📦&nbsp;AkmlSql.Analyzer.csproj</b><br/><small>net10.0</small>"]
        P5["<b>📦&nbsp;AkmlSql.Engine.csproj</b><br/><small>net10.0</small>"]
        P14["<b>📦&nbsp;AkmlSql.Web.csproj</b><br/><small>net10.0</small>"]
        P15["<b>📦&nbsp;AkmlSql.AI.Tests.csproj</b><br/><small>net10.0</small>"]
        P17["<b>📦&nbsp;AkmlSql.Core.Tests.csproj</b><br/><small>net10.0</small>"]
        P19["<b>📦&nbsp;AkmlSql.Engine.Tests.csproj</b><br/><small>net10.0</small>"]
        P27["<b>📦&nbsp;AkmlSql.Web.Tests.csproj</b><br/><small>net10.0</small>"]
        click P3 "../projects/AkmlSql.Analyzer.md"
        click P5 "../projects/AkmlSql.Engine.md"
        click P14 "../projects/AkmlSql.Web.md"
        click P15 "../projects/AkmlSql.AI.Tests.md"
        click P17 "../projects/AkmlSql.Core.Tests.md"
        click P19 "../projects/AkmlSql.Engine.Tests.md"
        click P27 "../projects/AkmlSql.Web.Tests.md"
    end
    subgraph current["AkmlSql.AI.csproj"]
        MAIN["<b>📦&nbsp;AkmlSql.AI.csproj</b><br/><small>net10.0</small>"]
        click MAIN "../projects/AkmlSql.AI.md"
    end
    subgraph downstream["Dependencies (2)"]
        P4["<b>📦&nbsp;AkmlSql.Core.csproj</b><br/><small>netstandard2.0;net10.0</small>"]
        P8["<b>📦&nbsp;AkmlSql.IntelliSense.csproj</b><br/><small>net10.0</small>"]
        click P4 "../projects/AkmlSql.Core.md"
        click P8 "../projects/AkmlSql.IntelliSense.md"
    end
    P3 --> MAIN
    P5 --> MAIN
    P14 --> MAIN
    P15 --> MAIN
    P17 --> MAIN
    P19 --> MAIN
    P27 --> MAIN
    MAIN --> P4
    MAIN --> P8

```

## API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 0 | High - Require code changes |
| 🟡 Source Incompatible | 8 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 10 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 1991 |  |
| ***Total APIs Analyzed*** | ***2009*** |  |

