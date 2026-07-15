#nullable enable
using System.Linq;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Xunit;

namespace AkmlSql.Core.Tests.Completion;

/// <summary>
/// Unit tests for completion clause detection: UPDATE ... SET and ALTER TABLE ... ALTER COLUMN.
/// These clauses require column suggestions from the referenced table (US1 / T007).
/// </summary>
public class CompletionClauseTests
{
    private readonly CompletionEngine _engine;
    private readonly DatabaseCache _cache;

    public CompletionClauseTests()
    {
        var parser = new TsqlParserService();
        _engine = new CompletionEngine(parser);

        // Build a minimal cache with a dbo.Users table that has Id, Name, Email columns.
        _cache = new DatabaseCache { CacheKey = "server:db" };
        var usersObj = new DatabaseObject
        {
            SchemaName = "dbo",
            ObjectName = "Users",
            ObjectType = DbObjectType.Table,
            ColumnsLoaded = true
        };
        usersObj.Columns.Add(new Column { ColumnName = "Id",    ColumnId = 1 });
        usersObj.Columns.Add(new Column { ColumnName = "Name",  ColumnId = 2 });
        usersObj.Columns.Add(new Column { ColumnName = "Email", ColumnId = 3 });

        var schema = new SchemaEntry { SchemaName = "dbo" };
        schema.Objects.Add(usersObj);
        _cache.Schemas["dbo"] = schema;
    }

    [Fact]
    public void Update_SetClause_ReturnsColumnCompletions()
    {
        // After "UPDATE Users SET " the engine should suggest columns from Users.
        const string sql = "UPDATE Users SET ";
        int cursor = sql.Length;

        var response = _engine.GetCompletions(sql, cursor, _cache);

        var columnNames = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Column)
            .Select(i => i.DisplayText)
            .ToList();

        Assert.Contains("Name",  columnNames);
        Assert.Contains("Email", columnNames);
    }

    [Fact]
    public void Delete_FromlessForm_WhereClause_ReturnsColumnCompletions()
    {
        // Reported bug: "DELETE dbo.Users WHERE " (no FROM) gave no column completion because the
        // target table never reached AvailableAliases. Columns of the delete target must be offered.
        const string sql = "DELETE dbo.Users WHERE ";
        int cursor = sql.Length;

        var response = _engine.GetCompletions(sql, cursor, _cache);

        var columnNames = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Column)
            .Select(i => i.DisplayText)
            .ToList();

        Assert.Contains("Name",  columnNames);
        Assert.Contains("Email", columnNames);
    }

    [Fact]
    public void Delete_FromlessForm_WhereClause_FiltersOnPartialColumn()
    {
        // The exact reported keystroke: a partial column typed in the WHERE of a FROM-less DELETE.
        const string sql = "DELETE dbo.Users WHERE Em";
        int cursor = sql.Length;

        var response = _engine.GetCompletions(sql, cursor, _cache);

        var columnNames = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Column)
            .Select(i => i.DisplayText)
            .ToList();

        Assert.Contains("Email", columnNames);
    }

    [Fact]
    public void Delete_FromlessForm_Unqualified_WhereClause_ReturnsColumnCompletions()
    {
        // Same shorthand without the schema prefix: "DELETE Users WHERE ".
        const string sql = "DELETE Users WHERE ";
        int cursor = sql.Length;

        var response = _engine.GetCompletions(sql, cursor, _cache);

        var columnNames = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Column)
            .Select(i => i.DisplayText)
            .ToList();

        Assert.Contains("Name",  columnNames);
        Assert.Contains("Email", columnNames);
    }

    [Fact]
    public void Delete_WithFrom_WhereClause_StillReturnsColumnCompletions()
    {
        // Regression guard: the "DELETE FROM <table>" form (covered by the FROM branch) must keep working.
        const string sql = "DELETE FROM dbo.Users WHERE ";
        int cursor = sql.Length;

        var response = _engine.GetCompletions(sql, cursor, _cache);

        var columnNames = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Column)
            .Select(i => i.DisplayText)
            .ToList();

        Assert.Contains("Name",  columnNames);
        Assert.Contains("Email", columnNames);
    }

    [Fact]
    public void AlterTable_AlterColumn_ReturnsColumnCompletions()
    {
        // After "ALTER TABLE Users ALTER COLUMN " the engine should suggest columns from Users.
        const string sql = "ALTER TABLE Users ALTER COLUMN ";
        int cursor = sql.Length;

        var response = _engine.GetCompletions(sql, cursor, _cache);

        var columnNames = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Column)
            .Select(i => i.DisplayText)
            .ToList();

        Assert.Contains("Name",  columnNames);
        Assert.Contains("Email", columnNames);
    }
}
