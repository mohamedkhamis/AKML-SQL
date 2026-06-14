using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Handlers.Refactoring;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring;

/// <summary>
/// Unit tests for <see cref="FindInvalidObjectsHandler"/>'s pure mapping method
/// (<see cref="FindInvalidObjectsHandler.MapInvalidObjects"/>). These exercise the
/// row-to-record projection and the noise-exclusion rules WITHOUT a live SQL Server:
/// the test feeds <see cref="FindInvalidObjectsHandler.DependencyRow"/> values directly
/// to the mapper. Spec 030 / T058 / FR-019 / R8.
/// </summary>
public class FindInvalidObjectsHandlerTests
{
    private static readonly DateTime FixedNow = new(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

    private static FindInvalidObjectsHandler.DependencyRow Row(
        string schema = "dbo",
        string name = "vw_Broken",
        string typeCode = "V ",
        string? referencedServer = null,
        string? referencedDatabase = null,
        string? referencedSchema = "dbo",
        string? referencedEntity = "MissingTable",
        bool isAmbiguous = false,
        bool isCallerDependent = false)
        => new(schema, name, typeCode, referencedServer, referencedDatabase,
               referencedSchema, referencedEntity, isAmbiguous, isCallerDependent);

    [Fact]
    public void MapInvalidObjects_Empty_ReturnsEmpty()
    {
        var records = FindInvalidObjectsHandler.MapInvalidObjects([], FixedNow);
        Assert.Empty(records);
    }

    [Fact]
    public void MapInvalidObjects_BrokenLocalReference_EmitsRecord()
    {
        var records = FindInvalidObjectsHandler.MapInvalidObjects([Row()], FixedNow);

        var rec = Assert.Single(records);
        Assert.Equal("dbo", rec.Schema);
        Assert.Equal("vw_Broken", rec.Name);
        Assert.Equal(1, rec.Type); // View
        Assert.Equal(FixedNow, rec.ScannedAtUtc);
    }

    [Fact]
    public void MapInvalidObjects_PopulatesMissingDependency_FromSchemaQualifiedName()
    {
        var records = FindInvalidObjectsHandler.MapInvalidObjects(
            [Row(referencedSchema: "sales", referencedEntity: "Gone")], FixedNow);

        var rec = Assert.Single(records);
        Assert.Equal("sales.Gone", rec.MissingDependency);
    }

    [Fact]
    public void MapInvalidObjects_MissingDependency_NoSchema_UsesEntityNameOnly()
    {
        var records = FindInvalidObjectsHandler.MapInvalidObjects(
            [Row(referencedSchema: null, referencedEntity: "Gone")], FixedNow);

        var rec = Assert.Single(records);
        Assert.Equal("Gone", rec.MissingDependency);
    }

    [Fact]
    public void MapInvalidObjects_ErrorMessage_MentionsMissingDependency()
    {
        var records = FindInvalidObjectsHandler.MapInvalidObjects(
            [Row(referencedSchema: "dbo", referencedEntity: "MissingTable")], FixedNow);

        var rec = Assert.Single(records);
        Assert.Contains("dbo.MissingTable", rec.ErrorMessage);
    }

    [Fact]
    public void MapInvalidObjects_SourceLine_IsNull()
    {
        // sys.sql_expression_dependencies carries no line info — must not be fabricated.
        var records = FindInvalidObjectsHandler.MapInvalidObjects([Row()], FixedNow);
        Assert.Null(Assert.Single(records).SourceLine);
    }

    [Fact]
    public void MapInvalidObjects_CrossDatabaseReference_IsExcluded()
    {
        var records = FindInvalidObjectsHandler.MapInvalidObjects(
            [Row(referencedDatabase: "OtherDb")], FixedNow);
        Assert.Empty(records);
    }

    [Fact]
    public void MapInvalidObjects_LinkedServerReference_IsExcluded()
    {
        var records = FindInvalidObjectsHandler.MapInvalidObjects(
            [Row(referencedServer: "LINKED01")], FixedNow);
        Assert.Empty(records);
    }

    [Fact]
    public void MapInvalidObjects_CallerDependentReference_IsExcluded()
    {
        // referenced_id is NULL by design for runtime-resolved refs (e.g. unqualified
        // EXEC SomeProc) — these are valid objects, not broken ones.
        var records = FindInvalidObjectsHandler.MapInvalidObjects(
            [Row(isCallerDependent: true)], FixedNow);
        Assert.Empty(records);
    }

    [Fact]
    public void MapInvalidObjects_AmbiguousReference_IsExcluded()
    {
        var records = FindInvalidObjectsHandler.MapInvalidObjects(
            [Row(isAmbiguous: true)], FixedNow);
        Assert.Empty(records);
    }

    [Fact]
    public void MapInvalidObjects_TempTableReference_IsExcluded()
    {
        var records = FindInvalidObjectsHandler.MapInvalidObjects(
            [Row(referencedSchema: null, referencedEntity: "#TempStage")], FixedNow);
        Assert.Empty(records);
    }

    [Fact]
    public void MapInvalidObjects_NullReferencedEntity_IsExcluded()
    {
        // A dependency with no referenced entity name is not actionable.
        var records = FindInvalidObjectsHandler.MapInvalidObjects(
            [Row(referencedSchema: null, referencedEntity: null)], FixedNow);
        Assert.Empty(records);
    }

    [Theory]
    [InlineData("U ", 0)]  // user table
    [InlineData("V ", 1)]  // view
    [InlineData("P ", 2)]  // stored procedure
    [InlineData("PC", 2)]  // CLR stored procedure
    [InlineData("FN", 3)]  // scalar function
    [InlineData("IF", 3)]  // inline table-valued function
    [InlineData("TF", 3)]  // multi-statement table-valued function
    [InlineData("FS", 3)]  // CLR scalar function
    [InlineData("TR", 4)]  // DML trigger
    [InlineData("TA", 4)]  // CLR trigger
    [InlineData("SN", 5)]  // synonym
    public void MapInvalidObjects_MapsObjectTypeCode(string typeCode, int expectedType)
    {
        var records = FindInvalidObjectsHandler.MapInvalidObjects(
            [Row(typeCode: typeCode)], FixedNow);

        Assert.Equal(expectedType, Assert.Single(records).Type);
    }

    [Fact]
    public void MapInvalidObjects_MixedRows_KeepsOnlyGenuineBrokenRefs()
    {
        var rows = new[]
        {
            Row(name: "vw_Good", referencedDatabase: "OtherDb"),   // cross-db -> excluded
            Row(name: "vw_Bad", referencedEntity: "MissingTable"), // broken    -> kept
            Row(name: "vw_Temp", referencedSchema: null, referencedEntity: "#t"), // temp -> excluded
        };

        var records = FindInvalidObjectsHandler.MapInvalidObjects(rows, FixedNow);

        var rec = Assert.Single(records);
        Assert.Equal("vw_Bad", rec.Name);
    }
}
