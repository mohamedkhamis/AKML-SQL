using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>The scripts the schema explorer's context menu opens, for SQL Server and PostgreSQL.</summary>
public sealed class SchemaObjectScriptsTests
{
    private static SchemaPhaseObject Customers(int type = 0) => new()
    {
        SchemaName = "dbo",
        ObjectName = "Customers",
        ObjectType = type,
        Columns = new[]
        {
            new SchemaPhaseColumn { Name = "Id", TypeName = "int", IsPrimaryKey = true },
            new SchemaPhaseColumn { Name = "Name", TypeName = "nvarchar", IsNullable = true },
        },
    };

    [Fact]
    public void Select_top_reads_1000_rows_with_the_columns_named()
    {
        Assert.Equal("SELECT TOP (1000) [Id],\n       [Name]\nFROM [dbo].[Customers];",
            SchemaObjectScripts.Script(SchemaObjectCommand.SelectTop, DatabaseKind.SqlServer, "dbo", Customers()));
        Assert.Equal("SELECT \"Id\",\n       \"Name\"\nFROM dbo.\"Customers\"\nLIMIT 1000;",
            SchemaObjectScripts.Script(SchemaObjectCommand.SelectTop, DatabaseKind.PostgreSql, "dbo", Customers()));
    }

    [Fact]
    public void Without_loaded_columns_select_uses_a_star_and_insert_is_not_offered()
    {
        var bare = new SchemaPhaseObject { SchemaName = "public", ObjectName = "orders", ObjectType = 0 };

        Assert.Equal("SELECT *\nFROM public.orders\nLIMIT 1000;",
            SchemaObjectScripts.Script(SchemaObjectCommand.SelectTop, DatabaseKind.PostgreSql, "public", bare));
        Assert.Null(SchemaObjectScripts.Script(SchemaObjectCommand.ScriptInsert, DatabaseKind.PostgreSql, "public", bare));
    }

    [Fact]
    public void Update_and_delete_match_no_row_until_their_where_is_filled_in()
    {
        var update = SchemaObjectScripts.Script(SchemaObjectCommand.ScriptUpdate, DatabaseKind.SqlServer, "dbo", Customers())!;
        Assert.StartsWith("UPDATE [dbo].[Customers]\nSET    [Name] = NULL", update);
        Assert.EndsWith("\nWHERE  [Id] = NULL; -- the key of the row", update);

        var keyless = new SchemaPhaseObject { ObjectName = "log", ObjectType = 0, Columns = new[] { new SchemaPhaseColumn { Name = "msg", TypeName = "text" } } };
        Assert.Equal("DELETE FROM public.log\nWHERE 1 = 0; -- the rows to change",
            SchemaObjectScripts.Script(SchemaObjectCommand.ScriptDelete, DatabaseKind.PostgreSql, "public", keyless));
    }

    [Fact]
    public void Routines_are_called_as_their_kind_calls_them()
    {
        var proc = new SchemaPhaseObject
        {
            ObjectName = "AddOrder", ObjectType = 2,
            Parameters = new[] { new SchemaPhaseParameter { Name = "@Id", TypeName = "int" }, new SchemaPhaseParameter { Name = "@Out", TypeName = "int", IsOutput = true } },
        };
        Assert.Equal("EXEC [dbo].[AddOrder]\n    @Id = NULL, -- int\n    @Out = NULL OUTPUT; -- int",
            SchemaObjectScripts.Script(SchemaObjectCommand.ScriptExecute, DatabaseKind.SqlServer, "dbo", proc));
        Assert.Equal("CALL public.\"AddOrder\"(NULL, NULL);",
            SchemaObjectScripts.Script(SchemaObjectCommand.ScriptExecute, DatabaseKind.PostgreSql, "public", proc));

        var function = new SchemaPhaseObject
        {
            ObjectName = "totals", ObjectType = 4,
            Parameters = new[] { new SchemaPhaseParameter { Name = "since", TypeName = "date" }, new SchemaPhaseParameter { Name = "total", TypeName = "numeric", IsOutput = true } },
        };
        Assert.Equal("SELECT *\nFROM public.totals(NULL);",
            SchemaObjectScripts.Script(SchemaObjectCommand.ScriptExecute, DatabaseKind.PostgreSql, "public", function));
    }

    [Fact]
    public void The_menu_offers_what_the_object_supports()
    {
        Assert.Equal(5, SchemaObjectScripts.CommandsFor(0).Count);                       // table
        Assert.Equal(new[] { SchemaObjectCommand.SelectTop, SchemaObjectCommand.ScriptSelect }, SchemaObjectScripts.CommandsFor(1));
        Assert.Equal(new[] { SchemaObjectCommand.ScriptExecute }, SchemaObjectScripts.CommandsFor(2));
        Assert.Empty(SchemaObjectScripts.CommandsFor(7));                                  // sequence: names only
        Assert.Equal("Select first 1000 rows", SchemaObjectScripts.Label(SchemaObjectCommand.SelectTop, DatabaseKind.PostgreSql, 0));
        Assert.Equal("Script as CALL", SchemaObjectScripts.Label(SchemaObjectCommand.ScriptExecute, DatabaseKind.PostgreSql, 2));
    }

    [Fact]
    public void A_closing_bracket_in_a_SQL_Server_name_is_doubled()
    {
        Assert.Equal("[dbo].[odd]]name]", SchemaObjectScripts.Name(DatabaseKind.SqlServer, "dbo", "odd]name"));
    }
}
