using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Ai;
using AkmlSql.Engine.Ai.Context;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using AkmlSql.Engine.Server;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers.Ai;

/// <summary>
/// Spec 036 (US1) T007 — FR-021 session binding: a schema-aware AI request carrying the session
/// id of a connected <see cref="SessionState"/> must resolve to that session's database cache and
/// produce a non-empty context; an unknown id must produce the explicit unbound context
/// (FR-028), never a silently empty one. Drives the exact sessionLookup closure shape that
/// AiChatHandler uses, through the same <see cref="AiPipelineServices"/> wiring the registry
/// builds.
/// </summary>
public class AiSessionBindingTests
{
    private const string SessionId = "0f8fad5bd9cb469fa1657086772892fe"; // buffer-shaped id ("N" format)

    private static (SessionManager Sessions, SchemaCacheManager Cache, AiPipelineServices Services) BuildEngineSide()
    {
        var sessions = new SessionManager();
        var schemaCache = new SchemaCacheManager();
        var services = AiPipelineServices.Build(
            schemaCache,
            new TsqlParserService(),
            () => new AiSettings { Provider = "ollama", Enabled = true });
        return (sessions, schemaCache, services);
    }

    private static void SeedConnectedSession(SessionManager sessions, SchemaCacheManager schemaCache)
    {
        // Mirrors ConnectionChangedHandler: the session is created connected, and the schema
        // cache is keyed by SESSION ID (see GetOrCreateCache(request.SessionId, ...) there).
        sessions.UpdateSession(new ConnectionInfo
        {
            SessionId = SessionId,
            ConnectionString = "Server=localhost;Database=TestDb;Integrated Security=true",
            DatabaseName = "TestDb",
        });

        var cache = schemaCache.GetOrCreateCache(SessionId, "TestDb");
        cache.Schemas["dbo"] = new SchemaEntry
        {
            SchemaName = "dbo",
            Objects =
            [
                new DatabaseObject { SchemaName = "dbo", ObjectName = "Orders", ObjectType = DbObjectType.Table },
                new DatabaseObject { SchemaName = "dbo", ObjectName = "Customers", ObjectType = DbObjectType.Table },
            ],
        };
    }

    /// <summary>The exact closure shape used by AiChatHandler / AiExplainHandler / ... .</summary>
    private static (string? ConnectionString, string? DatabaseName) SessionLookup(
        SessionManager sessions, string sid)
    {
        var s = sessions.GetSession(sid);
        return s == null || !s.IsConnected ? (null, null) : (s.ConnectionString, s.DatabaseName);
    }

    [Fact]
    public async Task Connected_session_id_resolves_to_that_databases_cache()
    {
        var (sessions, schemaCache, services) = BuildEngineSide();
        SeedConnectedSession(sessions, schemaCache);

        var context = await services.SchemaContext.BuildAsync(
            SessionId,
            sid => SessionLookup(sessions, sid),
            "what tables do I have",
            compressionLevel: 3);

        Assert.Equal("TestDb", context.DatabaseName);
        Assert.NotEmpty(context.Objects);
        Assert.Contains(context.Objects, o => o.Name == "Orders");
        Assert.Contains(context.Objects, o => o.Name == "Customers");
    }

    [Fact]
    public async Task Unknown_session_id_produces_the_explicit_unbound_context()
    {
        var (sessions, schemaCache, services) = BuildEngineSide();
        SeedConnectedSession(sessions, schemaCache); // another session exists; the request's id is not it

        var context = await services.SchemaContext.BuildAsync(
            "no-such-session",
            sid => SessionLookup(sessions, sid),
            "what tables do I have",
            compressionLevel: 3);

        Assert.Equal(string.Empty, context.DatabaseName);
        Assert.Empty(context.Objects);

        // FR-028: the rendered context must SAY there is no connection — not look like an
        // empty database. A connected-but-empty database renders a different message.
        var unboundText = SchemaContextFormatter.Format(context);
        Assert.Contains("No database connection", unboundText);

        // Connected-but-empty: same session, cache with every object removed.
        var emptyCache = schemaCache.GetCache(SessionId, "TestDb")!;
        emptyCache.Schemas.Clear();
        var emptyContext = await services.SchemaContext.BuildAsync(
            SessionId,
            sid => SessionLookup(sessions, sid),
            "what tables do I have",
            compressionLevel: 3);
        var emptyText = SchemaContextFormatter.Format(emptyContext);
        Assert.NotEqual(unboundText, emptyText);
        Assert.Contains("TestDb", emptyText);
        Assert.DoesNotContain("No database connection", emptyText);
    }
}
