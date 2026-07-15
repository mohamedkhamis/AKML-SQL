using Xunit;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Completion.Providers;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;

namespace AkmlSql.Engine.Tests.Completion;

/// <summary>
/// ON-clause suggestions must belong to the JOIN being written. Regression cover for the bug where
/// `FROM a JOIN b ON … JOIN c ON |` suggested the `a ↔ b` FK predicate — a condition that never
/// mentions `c`, the table whose ON clause holds the cursor — because JoinOnFkProvider paired every
/// alias in scope with every other.
/// </summary>
public class JoinOnCurrentTargetTests
{
    private readonly TsqlParserService _parserService = new();

    // ── Fixture: three tables. Roster→ShiftType FK (the "prior pair"), Roster→Employee FK
    //    (the pair that involves the table being joined on the current line). ──

    private static DatabaseCache MakeCache(bool employeeFk = true)
    {
        var cache = new DatabaseCache { CacheKey = "test:hr" };
        var dbo = new SchemaEntry { SchemaName = "dbo" };

        dbo.Objects.Add(MakeTable(1, "HR_ShiftRosterEntry", "Id", "ShiftTypeId", "EmployeeId"));
        dbo.Objects.Add(MakeTable(2, "HR_ShiftType", "Id", "Name"));
        dbo.Objects.Add(MakeTable(3, "HR_Employee", "Id", "FullName"));
        cache.Schemas.TryAdd("dbo", dbo);

        cache.ForeignKeys.Add(MakeFk("FK_Roster_ShiftType", "HR_ShiftRosterEntry", "ShiftTypeId", "HR_ShiftType", "Id"));
        if (employeeFk)
            cache.ForeignKeys.Add(MakeFk("FK_Roster_Employee", "HR_ShiftRosterEntry", "EmployeeId", "HR_Employee", "Id"));
        cache.RebuildFkIndex();
        return cache;
    }

    private static DatabaseObject MakeTable(int id, string name, params string[] columns)
    {
        var obj = new DatabaseObject
        {
            ObjectId = id,
            SchemaName = "dbo",
            ObjectName = name,
            ObjectType = DbObjectType.Table,
            ApproxRowCount = 1,
            ColumnsLoaded = true
        };
        foreach (var col in columns)
            obj.Columns.Add(new Column { ColumnName = col, TypeName = "int", IsPrimaryKey = col == "Id" });
        return obj;
    }

    private static ForeignKey MakeFk(string fkName, string parent, string parentCol, string referenced, string refCol) =>
        new()
        {
            FkName = fkName,
            ParentSchema = "dbo",
            ParentTable = parent,
            ParentColumns = [parentCol],
            ReferencedSchema = "dbo",
            ReferencedTable = referenced,
            ReferencedColumns = [refCol]
        };

    private CursorContext AnalyzeAt(string sql, int cursorOffset) =>
        new CursorContextAnalyzer().Analyze(_parserService.GetTokenStream(sql), cursorOffset);

    // ── Analyzer: the current join target is resolved from the owning JOIN ──

    [Fact]
    public void Analyzer_ResolvesCurrentJoinTarget_FromOwningJoin()
    {
        // The user's exact repro: caret sits right after the LAST `ON`.
        const string sql =
            "SELECT *\n" +
            "from  dbo.HR_ShiftRosterEntry\n" +
            "LEFT JOIN dbo.HR_ShiftType ON dbo.HR_ShiftType.Id = HR_ShiftRosterEntry.ShiftTypeId\n" +
            "LEFT JOIN  dbo.HR_Employee ON \n" +
            ";";
        int cursor = sql.IndexOf("ON \n;", StringComparison.Ordinal) + 3;

        var ctx = AnalyzeAt(sql, cursor);

        Assert.Equal(ClauseType.JoinOn, ctx.ClauseType);
        Assert.Equal("HR_Employee", ctx.CurrentJoinTargetAlias);
        Assert.Equal("dbo.HR_Employee", ctx.CurrentJoinTargetFullName);
    }

    [Fact]
    public void Analyzer_ResolvesAlias_WhenJoinTargetIsAliased()
    {
        const string sql = "SELECT * FROM Customers c JOIN Orders AS o ON ";
        var ctx = AnalyzeAt(sql, sql.Length);

        Assert.Equal(ClauseType.JoinOn, ctx.ClauseType);
        Assert.Equal("o", ctx.CurrentJoinTargetAlias);
        Assert.Equal("dbo.Orders", ctx.CurrentJoinTargetFullName);
    }

    [Fact]
    public void Analyzer_ResolvesTarget_WhenCursorIsMidCondition()
    {
        // The cursor need not sit immediately after ON — `ON o.Id = |` is still o's ON clause.
        const string sql = "SELECT * FROM Customers c JOIN Orders o ON o.Id = ";
        var ctx = AnalyzeAt(sql, sql.Length);

        Assert.Equal(ClauseType.JoinOn, ctx.ClauseType);
        Assert.Equal("o", ctx.CurrentJoinTargetAlias);
    }

    [Fact]
    public void Analyzer_LeavesTargetEmpty_ForMergeOn()
    {
        // MERGE's ON is not a JOIN's ON — attributing a table to it would be a lie.
        const string sql = "MERGE dbo.Target AS t USING dbo.Source AS s ON ";
        var ctx = AnalyzeAt(sql, sql.Length);

        Assert.Equal(string.Empty, ctx.CurrentJoinTargetAlias);
    }

    [Fact]
    public void Analyzer_LeavesTargetEmpty_ForCreateIndexOn()
    {
        const string sql = "CREATE INDEX IX_Foo ON ";
        var ctx = AnalyzeAt(sql, sql.Length);

        Assert.Equal(string.Empty, ctx.CurrentJoinTargetAlias);
    }

    [Theory]
    // A table hint sits between the table and the ON — the target is still the table.
    [InlineData("SELECT * FROM Customers c JOIN Orders WITH (NOLOCK) ON ", "Orders")]
    [InlineData("SELECT * FROM Customers c JOIN Orders AS o WITH (NOLOCK) ON ", "o")]
    // Bracketed multi-part names, and the alias behind a table-valued function's argument list.
    [InlineData("SELECT * FROM Customers c JOIN [dbo].[Order Items] t ON ", "t")]
    [InlineData("SELECT * FROM Customers c JOIN dbo.fnOrders(1) f ON ", "f")]
    // Double-quoted names arrive as AsciiStringOrQuotedIdentifier, not QuotedIdentifier.
    [InlineData("SELECT * FROM Customers c JOIN \"dbo\".\"Employee\" e ON ", "e")]
    [InlineData("SELECT * FROM Customers c JOIN dbo.\"Orders\" ON ", "Orders")]
    // `db..Table` elides the schema — the target is the table, not the database.
    [InlineData("SELECT * FROM Customers c JOIN payroll..Employee m ON ", "m")]
    [InlineData("SELECT * FROM Customers c JOIN payroll..Employee ON ", "Employee")]
    public void Analyzer_ResolvesTarget_AcrossTableReferenceForms(string sql, string expectedTarget)
    {
        var ctx = AnalyzeAt(sql, sql.Length);

        Assert.Equal(ClauseType.JoinOn, ctx.ClauseType);
        Assert.Equal(expectedTarget, ctx.CurrentJoinTargetAlias);
    }

    [Fact]
    public void Analyzer_ResolvesTarget_WhenOnConditionContainsParenthesisedPredicate()
    {
        // `AND |` after a parenthesised EXISTS — still b's ON clause.
        const string sql = "SELECT * FROM Customers c JOIN Orders b ON EXISTS (SELECT 1 FROM Items) AND ";
        var ctx = AnalyzeAt(sql, sql.Length);

        Assert.Equal(ClauseType.JoinOn, ctx.ClauseType);
        Assert.Equal("b", ctx.CurrentJoinTargetAlias);
    }

    [Fact]
    public void Analyzer_SkipsJoinNestedInDerivedTable()
    {
        // The inner JOIN belongs to the derived table, not to the outer ON.
        const string sql =
            "SELECT * FROM Customers c JOIN (SELECT o.Id FROM Orders o JOIN Items i ON i.Id = o.Id) d ON ";
        var ctx = AnalyzeAt(sql, sql.Length);

        Assert.Equal(ClauseType.JoinOn, ctx.ClauseType);
        Assert.Equal("d", ctx.CurrentJoinTargetAlias);
    }

    // ── Provider: predicates are scoped to the current join target ──

    [Fact]
    public void JoinOnFkProvider_OmitsPredicatesThatDoNotInvolveCurrentTarget()
    {
        var provider = new JoinOnFkProvider();
        var cache = MakeCache();

        var ctx = new CursorContext { ClauseType = ClauseType.JoinOn };
        ctx.AvailableAliases["HR_ShiftRosterEntry"] = "dbo.HR_ShiftRosterEntry";
        ctx.AvailableAliases["HR_ShiftType"] = "dbo.HR_ShiftType";
        ctx.AvailableAliases["HR_Employee"] = "dbo.HR_Employee";
        ctx.CurrentJoinTargetAlias = "HR_Employee";

        var items = provider.GetCompletions(ctx, cache).ToList();

        // The Employee predicate is offered...
        Assert.Contains(items, i => i.InsertText.Contains("HR_Employee", StringComparison.OrdinalIgnoreCase));
        // ...and the already-joined prior pair is not, since it never mentions HR_Employee.
        Assert.DoesNotContain(items, i =>
            i.InsertText.Contains("HR_ShiftType", StringComparison.OrdinalIgnoreCase) &&
            !i.InsertText.Contains("HR_Employee", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void JoinOnFkProvider_FallsBackToAllPairs_WhenTargetUnknown()
    {
        // Contexts the analyzer can't attribute to a JOIN (and CursorContexts built directly)
        // must keep the historical unscoped behaviour rather than silently yield nothing.
        var provider = new JoinOnFkProvider();
        var cache = MakeCache();

        var ctx = new CursorContext { ClauseType = ClauseType.JoinOn };
        ctx.AvailableAliases["HR_ShiftRosterEntry"] = "dbo.HR_ShiftRosterEntry";
        ctx.AvailableAliases["HR_ShiftType"] = "dbo.HR_ShiftType";
        ctx.AvailableAliases["HR_Employee"] = "dbo.HR_Employee";
        // CurrentJoinTargetAlias deliberately left empty.

        var items = provider.GetCompletions(ctx, cache).ToList();

        Assert.Contains(items, i => i.InsertText.Contains("HR_ShiftType", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(items, i => i.InsertText.Contains("HR_Employee", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void JoinOnFkProvider_TwoTableJoin_IsUnaffected()
    {
        var provider = new JoinOnFkProvider();
        var cache = MakeCache();

        var ctx = new CursorContext { ClauseType = ClauseType.JoinOn };
        ctx.AvailableAliases["HR_ShiftRosterEntry"] = "dbo.HR_ShiftRosterEntry";
        ctx.AvailableAliases["HR_ShiftType"] = "dbo.HR_ShiftType";
        ctx.CurrentJoinTargetAlias = "HR_ShiftType";

        var items = provider.GetCompletions(ctx, cache).ToList();

        Assert.Contains(items, i =>
            i.InsertText.Contains("ShiftTypeId", StringComparison.OrdinalIgnoreCase));
    }

    // ── End-to-end through CompletionEngine: what the user actually sees ──

    [Fact]
    public void Engine_TopSuggestion_IsThePredicateForTheCurrentJoinTarget()
    {
        var engine = new CompletionEngine(_parserService) { JoinAssistEnabled = true };
        var cache = MakeCache();

        const string sql =
            "SELECT *\n" +
            "from  dbo.HR_ShiftRosterEntry\n" +
            "LEFT JOIN dbo.HR_ShiftType ON dbo.HR_ShiftType.Id = HR_ShiftRosterEntry.ShiftTypeId\n" +
            "LEFT JOIN  dbo.HR_Employee ON \n" +
            ";";
        int cursor = sql.IndexOf("ON \n;", StringComparison.Ordinal) + 3;

        var response = engine.GetCompletions(sql, cursor, cache);
        var items = response.Items.ToList();

        Assert.NotEmpty(items);

        // The very first suggestion must be the FK predicate joining the CURRENT table.
        Assert.Contains("HR_Employee", items[0].InsertText, StringComparison.OrdinalIgnoreCase);

        // The prior pair's predicate must not be offered at all.
        Assert.DoesNotContain(items, i =>
            i.SecondaryText != null &&
            i.SecondaryText.StartsWith("FK ·", StringComparison.Ordinal) &&
            !i.InsertText.Contains("HR_Employee", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The reported scenario: the current join target has NO foreign key to anything in scope.
    /// Previously the prior pair's FK predicate was emitted anyway and sorted to the very top.
    /// Now no predicate is offered at all — a condition that doesn't mention the table being
    /// joined is never a useful suggestion — and the target's own columns lead the list instead.
    /// </summary>
    [Fact]
    public void Engine_TargetWithoutForeignKey_OffersNoStalePredicate_AndLeadsWithTargetColumns()
    {
        var engine = new CompletionEngine(_parserService) { JoinAssistEnabled = true };
        var cache = MakeCache(employeeFk: false);

        const string sql =
            "SELECT *\n" +
            "from  dbo.HR_ShiftRosterEntry\n" +
            "LEFT JOIN dbo.HR_ShiftType ON dbo.HR_ShiftType.Id = HR_ShiftRosterEntry.ShiftTypeId\n" +
            "LEFT JOIN  dbo.HR_Employee ON \n" +
            ";";
        int cursor = sql.IndexOf("ON \n;", StringComparison.Ordinal) + 3;

        var items = engine.GetCompletions(sql, cursor, cache).Items.ToList();

        // No FK predicate at all — the prior pair's condition is not a suggestion for this ON.
        Assert.DoesNotContain(items, i =>
            i.SecondaryText != null && i.SecondaryText.StartsWith("FK ·", StringComparison.Ordinal));

        // The first column offered belongs to the table being joined on the current line.
        var firstColumn = items.First(i => i.ObjectType == (int)Core.Ipc.Messages.CompletionObjectType.Column);
        Assert.StartsWith("HR_Employee.", firstColumn.InsertText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The other side of an ON predicate is almost always a key column of a table already in scope,
    /// so demoting the current target's neighbours must not bury their keys: in
    /// <c>ON HR_Employee.Id = |</c> the column the user needs is <c>HR_ShiftRosterEntry.EmployeeId</c>.
    /// It must still outrank the target's own non-key columns.
    /// </summary>
    [Fact]
    public void Engine_NonTargetKeyColumns_OutrankTargetsNonKeyColumns()
    {
        var engine = new CompletionEngine(_parserService) { JoinAssistEnabled = true };
        var cache = MakeCache();

        const string sql =
            "SELECT * FROM dbo.HR_ShiftRosterEntry\n" +
            "LEFT JOIN dbo.HR_ShiftType ON dbo.HR_ShiftType.Id = HR_ShiftRosterEntry.ShiftTypeId\n" +
            "LEFT JOIN dbo.HR_Employee ON ";

        var columns = engine.GetCompletions(sql, sql.Length, cache).Items
            .Where(i => i.ObjectType == (int)Core.Ipc.Messages.CompletionObjectType.Column)
            .Select(i => i.InsertText)
            .ToList();

        int rosterFk = columns.FindIndex(c => c.Equals("HR_ShiftRosterEntry.EmployeeId", StringComparison.OrdinalIgnoreCase));
        int targetNonKey = columns.FindIndex(c => c.Equals("HR_Employee.FullName", StringComparison.OrdinalIgnoreCase));

        Assert.True(rosterFk >= 0, "the other table's FK column must still be offered");
        Assert.True(targetNonKey >= 0, "the target's own columns must be offered");
        Assert.True(rosterFk < targetNonKey,
            $"a non-target key column must outrank the target's non-key columns (FK at {rosterFk}, FullName at {targetNonKey})");
    }

    /// <summary>
    /// Two-table join: both operands are equally needed to write `o.CustomerId = c.Id`, so the FROM
    /// table's FK column must not sink below the joined table's descriptive columns.
    /// </summary>
    [Fact]
    public void Engine_TwoTableJoin_FromTablesKeyColumnStaysAboveTargetsPlainColumns()
    {
        var engine = new CompletionEngine(_parserService) { JoinAssistEnabled = true };
        var cache = MakeCache();

        const string sql = "SELECT * FROM dbo.HR_ShiftRosterEntry JOIN dbo.HR_Employee ON ";

        var columns = engine.GetCompletions(sql, sql.Length, cache).Items
            .Where(i => i.ObjectType == (int)Core.Ipc.Messages.CompletionObjectType.Column)
            .Select(i => i.InsertText)
            .ToList();

        int rosterFk = columns.FindIndex(c => c.Equals("HR_ShiftRosterEntry.EmployeeId", StringComparison.OrdinalIgnoreCase));
        int targetNonKey = columns.FindIndex(c => c.Equals("HR_Employee.FullName", StringComparison.OrdinalIgnoreCase));

        Assert.True(rosterFk >= 0 && targetNonKey >= 0);
        Assert.True(rosterFk < targetNonKey,
            $"the FROM table's join key must outrank the joined table's plain columns (FK at {rosterFk}, FullName at {targetNonKey})");
    }

    /// <summary>
    /// Double-quoted join targets must scope exactly like their bracketed/bare equivalents —
    /// otherwise the target is unresolved, the provider silently reverts to all-pairs, and the
    /// original bug (a predicate between the two prior tables) comes back.
    /// </summary>
    [Fact]
    public void Engine_DoubleQuotedJoinTarget_StillSuppressesThePriorPairPredicate()
    {
        var engine = new CompletionEngine(_parserService) { JoinAssistEnabled = true };
        var cache = MakeCache();

        const string sql =
            "SELECT * FROM dbo.HR_ShiftRosterEntry\n" +
            "LEFT JOIN dbo.HR_ShiftType ON dbo.HR_ShiftType.Id = HR_ShiftRosterEntry.ShiftTypeId\n" +
            "LEFT JOIN \"dbo\".\"HR_Employee\" ON ";

        var items = engine.GetCompletions(sql, sql.Length, cache).Items.ToList();

        Assert.DoesNotContain(items, i =>
            i.SecondaryText != null &&
            i.SecondaryText.StartsWith("FK ·", StringComparison.Ordinal) &&
            !i.InsertText.Contains("HR_Employee", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Engine_CurrentTargetColumnsOutrankOtherTablesColumns()
    {
        var engine = new CompletionEngine(_parserService) { JoinAssistEnabled = true };
        var cache = MakeCache();

        const string sql =
            "SELECT * FROM dbo.HR_ShiftRosterEntry\n" +
            "LEFT JOIN dbo.HR_ShiftType ON dbo.HR_ShiftType.Id = HR_ShiftRosterEntry.ShiftTypeId\n" +
            "LEFT JOIN dbo.HR_Employee ON ";

        var response = engine.GetCompletions(sql, sql.Length, cache);
        var columns = response.Items
            .Where(i => i.ObjectType == (int)Core.Ipc.Messages.CompletionObjectType.Column)
            .ToList();

        Assert.NotEmpty(columns);

        int firstEmployee = columns.FindIndex(i => i.InsertText.StartsWith("HR_Employee.", StringComparison.OrdinalIgnoreCase));
        int firstShiftType = columns.FindIndex(i => i.InsertText.StartsWith("HR_ShiftType.", StringComparison.OrdinalIgnoreCase));

        Assert.True(firstEmployee >= 0, "expected the current join target's columns to be offered");
        Assert.True(firstShiftType >= 0, "other in-scope tables' columns must still be offered");
        Assert.True(firstEmployee < firstShiftType,
            $"current join target's columns must rank first (HR_Employee at {firstEmployee}, HR_ShiftType at {firstShiftType})");
    }
}
