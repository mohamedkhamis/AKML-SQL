#nullable enable
using System;
using System.Linq;
using AkmlSql.Engine.Parser;
using Xunit;

namespace AkmlSql.Core.Tests.Completion;

/// <summary>
/// Unit tests for <see cref="CursorContextAnalyzer"/> clause detection.
/// Validates that UPDATE SET and ALTER TABLE ALTER COLUMN contexts are detected
/// correctly so ColumnProvider can return column suggestions (US1 / T007).
///
/// Note: HistorySearchParser (US6 / T024) lives in AkmlSql.Shell.Shared and uses
/// VS-SDK types — it cannot be referenced from this net10.0 test project.
/// Its parsing logic is covered by integration testing in SSMS22.
/// </summary>
public class ContextAnalyzerClauseTests
{
    private readonly TsqlParserService _parser = new();
    private readonly CursorContextAnalyzer _analyzer = new();

    private CursorContext Analyze(string sql)
    {
        var tokens = _parser.GetTokenStream(sql);
        return _analyzer.Analyze(tokens, sql.Length);
    }

    [Fact]
    public void UpdateSet_DetectsCorrectClauseType()
    {
        var ctx = Analyze("UPDATE Users SET ");
        Assert.Equal(ClauseType.UpdateSet, ctx.ClauseType);
    }

    [Fact]
    public void AlterTableAlterColumn_DetectsCorrectClauseType()
    {
        var ctx = Analyze("ALTER TABLE Users ALTER COLUMN ");
        Assert.Equal(ClauseType.AlterTableColumn, ctx.ClauseType);
    }

    [Fact]
    public void AlterTableAlterColumn_PopulatesAliasWithTableName()
    {
        // AvailableAliases is populated with the target table so ColumnProvider
        // can resolve columns without a schema alias in the query.
        var ctx = Analyze("ALTER TABLE Users ALTER COLUMN ");
        // AvailableAliases stores fully-qualified names ("dbo.Users") from DetectAlterClauseType
        Assert.True(ctx.AvailableAliases.Values.Any(v =>
                v.EndsWith("Users", StringComparison.OrdinalIgnoreCase)),
            $"Expected 'Users' (possibly schema-qualified) in AvailableAliases but found: [{string.Join(", ", ctx.AvailableAliases.Values)}]");
    }
}
