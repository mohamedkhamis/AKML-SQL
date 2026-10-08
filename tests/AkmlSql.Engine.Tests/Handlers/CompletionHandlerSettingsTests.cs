using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Handlers.Completion;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using AkmlSql.Engine.Server;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers;

/// <summary>
/// Spec 040 (T011, OPT-01, FR-001) — the Suggestions › Behavior settings that used to change
/// nothing now reach the completion list: maximum suggestions, fuzzy matching and the three
/// detail flags (data types, nullability, key indicators). Defaults reproduce today's output.
/// </summary>
public sealed class CompletionHandlerSettingsTests
{
    private const string SessionId = "settings-session";
    private const string Database = "Shop";

    private static async Task<CompletionResponse> CompleteAsync(string sqlWithCursor, AppSettings settings)
    {
        int cursor = sqlWithCursor.IndexOf('|');
        string sql = sqlWithCursor.Replace("|", string.Empty);

        var sessions = new SessionManager();
        sessions.UpdateSession(new ConnectionInfo { SessionId = SessionId, DatabaseName = Database, ConnectionString = string.Empty });
        sessions.UpdateDocument(new DocumentChange { SessionId = SessionId, ChangeType = 0, FullText = sql });

        var schemaCache = new SchemaCacheManager();
        Populate(schemaCache.GetOrCreateCache(SessionId, Database));

        var ctx = new RpcContext
        {
            Sessions = sessions,
            SchemaCache = schemaCache,
            Logger = Log.Logger,
            SettingsLoader = () => settings,
        };
        var handler = new CompletionHandler(new CompletionEngine(new TsqlParserService()), () => settings);
        return await handler.HandleAsync(new CompletionRequest { SessionId = SessionId, CursorOffset = cursor }, ctx, CancellationToken.None);
    }

    private static void Populate(DatabaseCache cache)
    {
        cache.Phase = PopulationPhase.PhaseB;
        var dbo = new SchemaEntry { SchemaName = "dbo" };
        cache.Schemas["dbo"] = dbo;

        var products = new DatabaseObject { SchemaName = "dbo", ObjectName = "Products", ObjectType = DbObjectType.Table, ColumnsLoaded = true };
        products.Columns.Add(new Column { ColumnId = 1, ColumnName = "ProductID", TypeName = "int", IsNullable = false, IsPrimaryKey = true, IsIdentity = true });
        products.Columns.Add(new Column { ColumnId = 2, ColumnName = "ProductName", TypeName = "nvarchar", MaxLength = 40, IsNullable = false });
        products.Columns.Add(new Column { ColumnId = 3, ColumnName = "QuantityPerUnit", TypeName = "nvarchar", MaxLength = 20, IsNullable = true });
        products.Columns.Add(new Column { ColumnId = 4, ColumnName = "UnitPrice", TypeName = "money", IsNullable = true });
        dbo.Objects.Add(products);

        // Enough tables that "FROM dbo." has more candidates than a small limit allows.
        for (int i = 1; i <= 15; i++)
            dbo.Objects.Add(new DatabaseObject { SchemaName = "dbo", ObjectName = $"Table{i:00}", ObjectType = DbObjectType.Table, ColumnsLoaded = true });

        cache.RebuildFkIndex();
    }

    private static CompletionItem Column(CompletionResponse response, string name) =>
        response.Items.Single(i => i.ObjectType == (int)CompletionObjectType.Column && i.DisplayText == name);

    [Fact]
    public async Task MaxSuggestions_limits_the_list()
    {
        var settings = new AppSettings();
        settings.IntelliSense.MaxSuggestions = 10;

        var response = await CompleteAsync("SELECT * FROM dbo.|", settings);

        Assert.InRange(response.Items.Count(), 1, 10);
    }

    [Fact]
    public async Task Default_limit_still_returns_more_than_ten()
    {
        var response = await CompleteAsync("SELECT * FROM dbo.|", new AppSettings());

        Assert.True(response.Items.Count() > 10, $"expected the default limit to allow more than 10, got {response.Items.Count()}");
    }

    [Fact]
    public async Task FuzzyMatch_off_keeps_only_prefix_matches()
    {
        var settings = new AppSettings();
        settings.IntelliSense.FuzzyMatch = false;

        var response = await CompleteAsync("SELECT * FROM dbo.Products WHERE unit|", settings);

        Assert.Contains(response.Items, i => i.DisplayText == "UnitPrice");
        Assert.DoesNotContain(response.Items, i => i.DisplayText == "QuantityPerUnit");
    }

    [Fact]
    public async Task FuzzyMatch_on_keeps_substring_matches()
    {
        var response = await CompleteAsync("SELECT * FROM dbo.Products WHERE unit|", new AppSettings());

        Assert.Contains(response.Items, i => i.DisplayText == "UnitPrice");
        Assert.Contains(response.Items, i => i.DisplayText == "QuantityPerUnit");
    }

    [Fact]
    public async Task ShowNullability_off_removes_null_markers()
    {
        var settings = new AppSettings();
        settings.IntelliSense.ShowNullability = false;

        var response = await CompleteAsync("SELECT * FROM dbo.Products WHERE |", settings);

        foreach (var item in response.Items.Where(i => i.ObjectType == (int)CompletionObjectType.Column))
            Assert.DoesNotContain("NULL", item.SecondaryText ?? string.Empty);
        Assert.StartsWith("int, PK, IDENTITY", Column(response, "ProductID").SecondaryText);
    }

    [Fact]
    public async Task All_detail_flags_off_leave_only_the_table_name()
    {
        var settings = new AppSettings();
        settings.IntelliSense.ShowDataTypes = false;
        settings.IntelliSense.ShowNullability = false;
        settings.IntelliSense.ShowPkFk = false;

        var response = await CompleteAsync("SELECT * FROM dbo.Products WHERE |", settings);

        foreach (var item in response.Items.Where(i => i.ObjectType == (int)CompletionObjectType.Column))
        {
            var text = item.SecondaryText ?? string.Empty;
            Assert.DoesNotContain("int", text);
            Assert.DoesNotContain("PK", text);
            Assert.DoesNotContain("NULL", text);
            Assert.False(text.StartsWith(" •") || text.StartsWith("•"), $"'{item.DisplayText}' starts with a bullet: '{text}'");
        }
    }

    [Fact]
    public async Task Defaults_reproduce_todays_text()
    {
        var response = await CompleteAsync("SELECT * FROM dbo.Products WHERE |", new AppSettings());

        Assert.StartsWith("int, NOT NULL, PK, IDENTITY", Column(response, "ProductID").SecondaryText);
        Assert.StartsWith("nvarchar(20), NULL", Column(response, "QuantityPerUnit").SecondaryText);
        Assert.StartsWith("money, NULL", Column(response, "UnitPrice").SecondaryText);
    }
}
