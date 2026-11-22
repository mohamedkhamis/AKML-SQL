using AkmlSql.Core.Config;
using AkmlSql.Engine.Refactoring.Operations.Lightweight;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring.Operations.Lightweight;

/// <summary>
/// Spec 047 installed-product run (baseline.md › Installed-product run), F6 and F7, on Expand INSERT columns with type
/// comments (the web's Refactor menu and the SSMS Format SQL action):
/// <list type="bullet">
/// <item>F6: the blank after the table name was left at the end of its line, the comments did not line up, and a CRLF script
/// got LF lines.</item>
/// <item>F7: an IDENTITY column was written into the list as a bare name with no comma, so the list was invalid and one column
/// longer than the VALUES (an INSERT without a column list has no value for it).</item>
/// </list>
/// </summary>
public sealed class ExpandInsertLayoutTests
{
    private readonly ExpandInsertColumnsOperation _op = new();

    private static DatabaseCache Cache()
    {
        var cache = new DatabaseCache();
        var schema = new SchemaEntry { SchemaName = "dbo" };
        schema.Objects.Add(new DatabaseObject
        {
            SchemaName = "dbo", ObjectName = "Region", ObjectType = DbObjectType.Table, ColumnsLoaded = true,
            Columns =
            [
                new Column { ColumnId = 1, ColumnName = "RegionID", TypeName = "int" },
                new Column { ColumnId = 2, ColumnName = "RegionDescription", TypeName = "nchar", MaxLength = 100 },
            ],
        });
        schema.Objects.Add(new DatabaseObject
        {
            SchemaName = "dbo", ObjectName = "Categories", ObjectType = DbObjectType.Table, ColumnsLoaded = true,
            Columns =
            [
                new Column { ColumnId = 1, ColumnName = "CategoryID", TypeName = "int", IsIdentity = true },
                new Column { ColumnId = 2, ColumnName = "CategoryName", TypeName = "nvarchar", MaxLength = 15 },
                new Column { ColumnId = 3, ColumnName = "Description", TypeName = "ntext", IsNullable = true },
            ],
        });
        cache.Schemas["dbo"] = schema;
        return cache;
    }

    private string Expand(string sql)
    {
        var context = LightweightOperationTestHelper.CreateContext(sql, new IntelliSenseSettings());
        context.SchemaCache = Cache();
        var (text, warnings) = _op.Apply(context);
        Assert.Empty(warnings);
        return text;
    }

    private static InsertStatement Parse(string sql)
    {
        var fragment = new TSql170Parser(true).Parse(new StringReader(sql), out var errors);
        Assert.Empty(errors);
        return (InsertStatement)((TSqlScript)fragment).Batches[0].Statements[0];
    }

    [Fact]
    public void The_list_starts_on_the_next_line_and_no_line_ends_in_a_blank()
    {
        var text = Expand("insert into dbo.Region values (9,'x')");

        Assert.StartsWith("insert into dbo.Region\n(\n", text);
        Assert.All(text.Split('\n'), line => Assert.Equal(line.TrimEnd(), line));
        Assert.EndsWith(") values (9,'x')", text);
    }

    [Fact]
    public void The_type_comments_line_up_in_one_column()
    {
        var text = Expand("insert into dbo.Region values (9,'x')");

        var commentColumns = text.Split('\n').Where(l => l.Contains("--")).Select(l => l.IndexOf("--", StringComparison.Ordinal)).Distinct();
        Assert.Single(commentColumns);
    }

    [Fact]
    public void A_CRLF_script_keeps_CRLF()
    {
        var text = Expand("insert into dbo.Region values (9,'x')\r\nselect 1;\r\n");

        Assert.Contains("insert into dbo.Region\r\n(\r\n", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", string.Empty));
    }

    [Fact]
    public void An_identity_column_is_named_in_a_comment_and_the_insert_still_parses_and_matches_its_values()
    {
        var text = Expand("insert into dbo.Categories values (N'Drinks', N'Soft drinks')");

        var insert = Parse(text);
        var listed = insert.InsertSpecification.Columns.Select(c => c.MultiPartIdentifier.Identifiers[^1].Value).ToList();
        Assert.Equal(new[] { "CategoryName", "Description" }, listed);
        Assert.Contains("-- CategoryID: IDENTITY, auto-generated", text);
    }

    [Fact]
    public void Expanding_the_Region_insert_gives_a_statement_that_parses()
    {
        var insert = Parse(Expand("insert into dbo.Region values (9,'x')"));

        Assert.Equal(new[] { "RegionID", "RegionDescription" },
            insert.InsertSpecification.Columns.Select(c => c.MultiPartIdentifier.Identifiers[^1].Value));
    }
}
