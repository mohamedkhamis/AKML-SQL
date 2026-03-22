# Developer Quickstart: Static Code Analysis Engine

**Branch**: `005-static-code-analysis` | **Date**: 2026-03-22

This guide helps a developer get oriented and productive on the Phase 5 Static Code Analysis feature.

---

## What Was Built Before This Feature

The existing Engine provides:
- **Parser**: `TsqlParserService` — tokenize + parse SQL to ScriptDom `TSqlScript` AST
- **IPC**: Named-pipe `PipeRpcServer` dispatches typed `RpcMessage` payloads (MessagePack) to handlers
- **Session**: `SessionManager` tracks per-connection state and document text
- **Schema cache**: `SchemaCacheManager` / `DatabaseCache` provide table/column metadata
- **Completion**: `CompletionEngine` + `ICompletionProvider` pattern — stateless providers receive context and return results

The analysis engine mirrors the completion provider pattern.

---

## Key Source Locations

| What | Where |
|------|-------|
| Rule interface | `src/AkmlSql.Engine/Analysis/IAnalysisRule.cs` |
| Analysis orchestrator | `src/AkmlSql.Engine/Analysis/AnalysisEngine.cs` |
| All rule implementations | `src/AkmlSql.Engine/Analysis/Rules/<Category>/` |
| IPC message types | `src/AkmlSql.Core/Ipc/MessageTypes.cs` |
| IPC payload models | `src/AkmlSql.Core/Ipc/Messages/CodeAnalysisRequest.cs` etc. |
| CaSettings model | `src/AkmlSql.Core/Models/Analysis/CaSettings.cs` |
| Settings class | `src/AkmlSql.Core/Config/AppSettings.cs` → `CodeAnalysisSettings` |
| Shell controller | `src/AkmlSql.Shell.Shared/Analysis/AnalysisController.cs` |
| Shell squiggles | `src/AkmlSql.Shell.Shared/Analysis/DiagnosticTagger.cs` |
| CLI entry point | `src/AkmlSql.Analyzer/Program.cs` |
| Engine tests | `tests/AkmlSql.Engine.Tests/Analysis/` |

---

## Writing a New Rule

Every rule is a class in `src/AkmlSql.Engine/Analysis/Rules/<Category>/` that implements `IAnalysisRule`.

### Minimal Rule Template

```csharp
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

public sealed class BP004_NullComparison : IAnalysisRule
{
    public string RuleId => "BP004";
    public string Category => "Best Practices";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Error;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var visitor = new NullComparisonVisitor(ctx);
        ctx.CurrentBatch.Accept(visitor);
        return visitor.Diagnostics;
    }

    private sealed class NullComparisonVisitor : TSqlFragmentVisitor
    {
        private readonly AnalysisContext _ctx;
        public List<AnalysisDiagnostic> Diagnostics { get; } = new();

        public NullComparisonVisitor(AnalysisContext ctx) => _ctx = ctx;

        public override void Visit(BooleanComparisonExpression node)
        {
            // Fire on: expression = NULL  or  expression != NULL
            if (node.SecondExpression is NullLiteral &&
                node.ComparisonType is BooleanComparisonType.Equals
                    or BooleanComparisonType.NotEqualToBrackets
                    or BooleanComparisonType.NotEqualToExclamation)
            {
                Diagnostics.Add(new AnalysisDiagnostic
                {
                    RuleId = RuleId,
                    Severity = _ctx.Settings.GetSeverity(RuleId, DefaultSeverity),
                    Message = "Comparison with NULL must use IS NULL or IS NOT NULL",
                    StartOffset = node.StartOffset,
                    EndOffset = node.StartOffset + node.FragmentLength,
                    Line = node.StartLine,
                    Column = node.StartColumn,
                    FixActions = BuildFix(node)
                });
            }
        }

        private AnalysisFixAction[] BuildFix(BooleanComparisonExpression node)
        {
            var lhs = _ctx.DocumentText[node.FirstExpression.StartOffset..
                       (node.FirstExpression.StartOffset + node.FirstExpression.FragmentLength)];
            var isNot = node.ComparisonType != BooleanComparisonType.Equals;
            var replacement = isNot ? $"{lhs} IS NOT NULL" : $"{lhs} IS NULL";

            return [new AnalysisFixAction
            {
                Label = isNot ? "Replace with IS NOT NULL" : "Replace with IS NULL",
                FixType = FixType.Transform,
                ReplacementStart = node.StartOffset,
                ReplacementEnd = node.StartOffset + node.FragmentLength,
                ReplacementText = replacement
            }];
        }
    }
}
```

### Rule Discovery

Rules are auto-discovered at startup via `RuleRegistry` which scans the `AkmlSql.Engine.Analysis.Rules` namespace for all `IAnalysisRule` implementations. No manual registration needed — just add the class file.

---

## Writing a Rule Test

Tests live in `tests/AkmlSql.Engine.Tests/Analysis/Rules/<Category>/`.

```csharp
namespace AkmlSql.Engine.Tests.Analysis.Rules.BestPractices;

public class BP004_NullComparisonTests
{
    // ── Trigger cases ──────────────────────────────────────────────

    [Fact]
    public void EqualNull_FiresError()
    {
        var issues = Analyze("SELECT * FROM t WHERE col = NULL");
        Assert.Single(issues);
        Assert.Equal("BP004", issues[0].RuleId);
        Assert.Equal(DiagnosticSeverity.Error, issues[0].Severity);
    }

    [Fact]
    public void EqualNull_HasFixAction()
    {
        var issues = Analyze("SELECT * FROM t WHERE col = NULL");
        var fix = Assert.Single(issues[0].FixActions);
        Assert.Equal("Replace with IS NULL", fix.Label);
        Assert.Contains("IS NULL", fix.ReplacementText);
        Assert.DoesNotContain("IS NOT NULL", fix.ReplacementText);
    }

    // ── Non-trigger cases (false positive tests) ───────────────────

    [Fact]
    public void IsNull_DoesNotFire()
    {
        var issues = Analyze("SELECT * FROM t WHERE col IS NULL");
        Assert.Empty(issues);
    }

    [Fact]
    public void InComment_DoesNotFire()
    {
        var issues = Analyze("-- WHERE col = NULL");
        Assert.Empty(issues);
    }

    // ── Suppression ────────────────────────────────────────────────

    [Fact]
    public void NoqaComment_Suppresses()
    {
        var issues = Analyze("-- noqa: BP004\nSELECT * FROM t WHERE col = NULL");
        Assert.Empty(issues);
    }

    // ── Helper ─────────────────────────────────────────────────────
    private static IReadOnlyList<AnalysisDiagnostic> Analyze(string sql)
        => AnalysisEngineTestHelper.Analyze(sql, "BP004");
}
```

**Test helper pattern**: `AnalysisEngineTestHelper.Analyze(sql, ruleId)` creates a minimal `AnalysisContext` (no schema cache, default settings), runs the specified rule, and returns its diagnostics.

---

## Running the Tests

```bash
# All Engine tests (includes analysis tests)
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj

# Analysis tests only
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj \
  --filter "FullyQualifiedName~Analysis"
```

---

## Running the CLI Tool

```bash
# Build the CLI
dotnet publish src/AkmlSql.Analyzer/AkmlSql.Analyzer.csproj -c Release -r win-x64

# Analyze a file
./src/AkmlSql.Analyzer/bin/Release/net10.0/win-x64/publish/AkmlSql.Analyzer.exe \
  --file "my-query.sql"

# Check mode for CI
./AkmlSql.Analyzer.exe --directory scripts/ --recursive --check --severity error
```

---

## Adding a CAsettings Override (for testing)

Create a `.casettings` file in the same directory as your test SQL file:

```json
{
  "metadata": { "name": "Test" },
  "rules": {
    "PE008": { "enabled": false }
  }
}
```

The Engine will auto-discover this file and apply it for any SQL file analyzed from that directory.

---

## Debugging Analysis in SSMS 22

1. Rebuild `AkmlSql.Ssms22` with MSBuild and deploy
2. Set `AKML_ENGINE_WAIT_FOR_DEBUGGER=1` environment variable before launching SSMS
3. Attach the VS debugger to `AkmlSql.Engine.exe`
4. Set a breakpoint in `AnalysisEngine.AnalyzeAsync`
5. Open a SQL file and type — the Engine will break when analysis runs
