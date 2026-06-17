using System;
using System.Data;
using System.Linq;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Execution;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AkmlSql.Engine.Tests.Execution;

/// <summary>
/// Spec 030 — Phase 5. Unit tests for the parameterized CRUD writer (locked constraint #2). No DB is
/// needed: <c>SqlConnection.CreateCommand()</c> works on a closed connection, so we build the command
/// text + parameters and assert their shape without ever opening a connection.
/// </summary>
public sealed class CrudWriteGeneratorTests
{
    private static SqlConnection NewClosedConnection() =>
        new("Server=(local);Database=tempdb;Integrated Security=true;TrustServerCertificate=true");

    private static CrudCellDto Cell(string col, SqlDbType type, string? value) => new()
    {
        BaseColumnName = col,
        ProviderType = (int)type,
        Value = value,
    };

    [Theory]
    [InlineData("Name", "[Name]")]
    [InlineData("Order Date", "[Order Date]")]
    [InlineData("weird]name", "[weird]]name]")]          // ] doubled
    [InlineData("a]]b", "[a]]]]b]")]                       // each ] doubled
    [InlineData("]", "[]]]")]
    public void QuoteName_DoublesClosingBracket(string id, string expected)
    {
        Assert.Equal(expected, CrudWriteGenerator.QuoteName(id));
    }

    [Fact]
    public void BuildQualifiedTable_ThreePart_WhenCatalogPresent()
    {
        var t = CrudWriteGenerator.BuildQualifiedTable("My]DB", "dbo", "Customers");
        Assert.Equal("[My]]DB].[dbo].[Customers]", t);
    }

    [Fact]
    public void BuildQualifiedTable_TwoPart_WhenCatalogEmpty()
    {
        var t = CrudWriteGenerator.BuildQualifiedTable(null, "Sales", "Orders");
        Assert.Equal("[Sales].[Orders]", t);
    }

    [Fact]
    public void Update_IsParameterized_SetThenWhere()
    {
        var req = new ApplyChangesRequest { BaseSchema = "dbo", BaseTable = "Customers" };
        var edit = new CrudEditDto
        {
            Op = CrudOp.Update,
            SetCells = new[] { Cell("Name", SqlDbType.NVarChar, "Alice"), Cell("Age", SqlDbType.Int, "30") },
            KeyCells = new[] { Cell("Id", SqlDbType.Int, "7") },
        };

        using var conn = NewClosedConnection();
        using var cmd = CrudWriteGenerator.BuildCommand(req, edit, conn, null);

        Assert.Equal("UPDATE [dbo].[Customers] SET [Name] = @p0, [Age] = @p1 WHERE [Id] = @k0", cmd.CommandText);
        // No inlined literals — every value is a typed parameter.
        Assert.Equal(3, cmd.Parameters.Count);
        Assert.Equal("Alice", cmd.Parameters["@p0"].Value);
        Assert.Equal(30, cmd.Parameters["@p1"].Value);
        Assert.Equal(7, cmd.Parameters["@k0"].Value);
        Assert.Equal(SqlDbType.NVarChar, cmd.Parameters["@p0"].SqlDbType);
        Assert.Equal(SqlDbType.Int, cmd.Parameters["@k0"].SqlDbType);
    }

    [Fact]
    public void Insert_IsParameterized_WithScopeIdentity()
    {
        var req = new ApplyChangesRequest { BaseSchema = "dbo", BaseTable = "Customers" };
        var edit = new CrudEditDto
        {
            Op = CrudOp.Insert,
            SetCells = new[] { Cell("Name", SqlDbType.NVarChar, "Bob"), Cell("Age", SqlDbType.Int, "25") },
        };

        using var conn = NewClosedConnection();
        using var cmd = CrudWriteGenerator.BuildCommand(req, edit, conn, null);

        Assert.Equal(
            "INSERT INTO [dbo].[Customers] ([Name], [Age]) VALUES (@p0, @p1); SELECT SCOPE_IDENTITY();",
            cmd.CommandText);
        Assert.Equal(2, cmd.Parameters.Count);
        Assert.Equal("Bob", cmd.Parameters["@p0"].Value);
        Assert.Equal(25, cmd.Parameters["@p1"].Value);
    }

    [Fact]
    public void Delete_IsParameterized_OnKeysOnly()
    {
        var req = new ApplyChangesRequest { BaseSchema = "dbo", BaseTable = "Customers" };
        var edit = new CrudEditDto
        {
            Op = CrudOp.Delete,
            KeyCells = new[] { Cell("Id", SqlDbType.Int, "42") },
        };

        using var conn = NewClosedConnection();
        using var cmd = CrudWriteGenerator.BuildCommand(req, edit, conn, null);

        Assert.Equal("DELETE FROM [dbo].[Customers] WHERE [Id] = @k0", cmd.CommandText);
        Assert.Single(cmd.Parameters);
        Assert.Equal(42, cmd.Parameters["@k0"].Value);
    }

    [Fact]
    public void Delete_CompositeKey_AndsKeys()
    {
        var req = new ApplyChangesRequest { BaseSchema = "dbo", BaseTable = "OrderLines" };
        var edit = new CrudEditDto
        {
            Op = CrudOp.Delete,
            KeyCells = new[] { Cell("OrderId", SqlDbType.Int, "1"), Cell("LineNo", SqlDbType.Int, "2") },
        };

        using var conn = NewClosedConnection();
        using var cmd = CrudWriteGenerator.BuildCommand(req, edit, conn, null);

        Assert.Equal("DELETE FROM [dbo].[OrderLines] WHERE [OrderId] = @k0 AND [LineNo] = @k1", cmd.CommandText);
    }

    [Fact]
    public void Update_RefusesKeylessWrite()
    {
        var req = new ApplyChangesRequest { BaseSchema = "dbo", BaseTable = "Customers" };
        var edit = new CrudEditDto
        {
            Op = CrudOp.Update,
            SetCells = new[] { Cell("Name", SqlDbType.NVarChar, "X") },
            KeyCells = Array.Empty<CrudCellDto>(),
        };

        using var conn = NewClosedConnection();
        var ex = Assert.Throws<InvalidOperationException>(() => CrudWriteGenerator.BuildCommand(req, edit, conn, null));
        Assert.Contains("without a key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Delete_RefusesKeylessWrite()
    {
        var req = new ApplyChangesRequest { BaseSchema = "dbo", BaseTable = "Customers" };
        var edit = new CrudEditDto { Op = CrudOp.Delete, KeyCells = Array.Empty<CrudCellDto>() };

        using var conn = NewClosedConnection();
        var ex = Assert.Throws<InvalidOperationException>(() => CrudWriteGenerator.BuildCommand(req, edit, conn, null));
        Assert.Contains("without a key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Update_RefusesEmptySet()
    {
        var req = new ApplyChangesRequest { BaseSchema = "dbo", BaseTable = "Customers" };
        var edit = new CrudEditDto
        {
            Op = CrudOp.Update,
            SetCells = Array.Empty<CrudCellDto>(),
            KeyCells = new[] { Cell("Id", SqlDbType.Int, "1") },
        };

        using var conn = NewClosedConnection();
        Assert.Throws<InvalidOperationException>(() => CrudWriteGenerator.BuildCommand(req, edit, conn, null));
    }

    [Fact]
    public void NullKeyCell_UsesIsNull_NotEquals()
    {
        var req = new ApplyChangesRequest { BaseSchema = "dbo", BaseTable = "Customers" };
        var edit = new CrudEditDto
        {
            Op = CrudOp.Delete,
            KeyCells = new[] { Cell("OptionalKey", SqlDbType.Int, null) },
        };

        using var conn = NewClosedConnection();
        using var cmd = CrudWriteGenerator.BuildCommand(req, edit, conn, null);

        Assert.Equal("DELETE FROM [dbo].[Customers] WHERE [OptionalKey] IS NULL", cmd.CommandText);
        Assert.Empty(cmd.Parameters); // no @kN bound for an IS NULL comparison.
    }

    [Fact]
    public void Update_NullSetValue_BindsDBNull()
    {
        var req = new ApplyChangesRequest { BaseSchema = "dbo", BaseTable = "Customers" };
        var edit = new CrudEditDto
        {
            Op = CrudOp.Update,
            SetCells = new[] { Cell("MiddleName", SqlDbType.NVarChar, null) },
            KeyCells = new[] { Cell("Id", SqlDbType.Int, "1") },
        };

        using var conn = NewClosedConnection();
        using var cmd = CrudWriteGenerator.BuildCommand(req, edit, conn, null);

        Assert.Equal("UPDATE [dbo].[Customers] SET [MiddleName] = @p0 WHERE [Id] = @k0", cmd.CommandText);
        Assert.Equal(DBNull.Value, cmd.Parameters["@p0"].Value);
    }

    [Fact]
    public void Update_InjectionAttemptInColumnName_IsQuotedNotExecuted()
    {
        var req = new ApplyChangesRequest { BaseSchema = "dbo", BaseTable = "Customers" };
        var edit = new CrudEditDto
        {
            Op = CrudOp.Update,
            SetCells = new[] { Cell("Name]; DROP TABLE Customers; --", SqlDbType.NVarChar, "x") },
            KeyCells = new[] { Cell("Id", SqlDbType.Int, "1") },
        };

        using var conn = NewClosedConnection();
        using var cmd = CrudWriteGenerator.BuildCommand(req, edit, conn, null);

        // The ] is doubled, so the malicious column name stays a single bracketed identifier.
        Assert.Contains("[Name]]; DROP TABLE Customers; --]", cmd.CommandText);
        Assert.DoesNotContain("] = @p0,", cmd.CommandText); // not broken out of the identifier.
    }
}
