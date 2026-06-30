using System.Collections.Generic;
using System.Linq;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Xunit;

namespace AkmlSql.Engine.Tests.Completion;

/// <summary>
/// User-reported bug: typing <c>IS</c> in a predicate (e.g. <c>WHERE A.date is </c>) offered the full
/// <c>IS NULL</c> / <c>IS NOT NULL</c> keywords, which duplicate the already-typed <c>IS</c> on commit
/// (<c>is is not null</c>). After an <c>IS</c> token the engine now offers the continuations
/// <c>NULL</c> / <c>NOT NULL</c> instead.
/// </summary>
public class KeywordIsNullTests
{
    private readonly TsqlParserService _parser = new();

    private static DatabaseCache Cache()
    {
        var c = new DatabaseCache { CacheKey = "srv:db" };
        var s = new SchemaEntry { SchemaName = "dbo" };
        var t = new DatabaseObject { SchemaName = "dbo", ObjectName = "A", ObjectType = DbObjectType.Table, ColumnsLoaded = true };
        t.Columns.Add(new Column { ColumnId = 1, ColumnName = "date", TypeName = "datetime" });
        s.Objects.Add(t);
        c.Schemas["dbo"] = s;
        c.RebuildFkIndex();
        return c;
    }

    private List<string> KeywordsAt(string sqlWithCaret)
    {
        var offset = sqlWithCaret.IndexOf('|');
        var sql = sqlWithCaret.Replace("|", string.Empty);
        var resp = new CompletionEngine(_parser).GetCompletions(sql, offset, Cache());
        return resp.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Keyword)
            .Select(i => i.DisplayText)
            .ToList();
    }

    [Fact]
    public void AfterIs_OffersNullAndNotNull_NotFullIsNullPredicate()
    {
        var kws = KeywordsAt("SELECT * FROM A WHERE A.date is |");

        Assert.Contains("NULL", kws);
        Assert.Contains("NOT NULL", kws);
        Assert.DoesNotContain("IS NULL", kws);       // would duplicate the typed IS
        Assert.DoesNotContain("IS NOT NULL", kws);
    }

    [Fact]
    public void BeforeIs_StillOffersTheFullIsNullPredicate()
    {
        // Regression: before IS is typed, the full predicate keywords remain reachable.
        var kws = KeywordsAt("SELECT * FROM A WHERE A.date |");

        Assert.Contains("IS NULL", kws);
        Assert.Contains("IS NOT NULL", kws);
    }
}
