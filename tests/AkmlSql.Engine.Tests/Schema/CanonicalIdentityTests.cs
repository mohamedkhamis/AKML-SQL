using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Handlers.Schema;
using Xunit;

namespace AkmlSql.Engine.Tests.Schema;

/// <summary>
/// Spec 021 (web edition) -- M5 task T106. The SchemaIdentify handler returns the same
/// canonical identity for distinct connection-string aliases of the same SQL Server, so
/// the browser's IndexedDB cache (keyed by [serverCanonicalIdentity, databaseName]) shares
/// one entry across all aliases. See contracts/schema-cache-shape.md.
/// </summary>
public sealed class CanonicalIdentityTests
{
    private static SchemaIdentifyHandler BuildHandler(
        string? databaseName,
        string? identity)
    {
        return new SchemaIdentifyHandler(
            databaseLookup: _ => databaseName,
            identityResolver: _ => identity);
    }

    [Fact]
    public async Task Returns_pair_when_both_lookups_succeed()
    {
        var handler = BuildHandler(
            databaseName: "AdventureWorks2022",
            identity: "PROD-DB01\\SQL2022");

        var response = await handler.HandleAsync(
            new SchemaIdentifyRequest { SessionId = "s1" }, null!, CancellationToken.None);

        Assert.True(response.HasConnection);
        Assert.Equal("PROD-DB01\\SQL2022", response.ServerCanonicalIdentity);
        Assert.Equal("AdventureWorks2022", response.DatabaseName);
        Assert.Equal("s1", response.SessionId);
        Assert.Null(response.ErrorMessage);
    }

    [Fact]
    public async Task Returns_no_connection_when_identity_resolver_returns_null()
    {
        // The session has a DB name in SessionManager but the engine reports no live
        // connection -- e.g. the session was registered with a connection-string and then
        // the connection failed. The browser must NOT cache against this response.
        var handler = BuildHandler(databaseName: "master", identity: null);

        var response = await handler.HandleAsync(
            new SchemaIdentifyRequest { SessionId = "s1" }, null!, CancellationToken.None);

        Assert.False(response.HasConnection);
        Assert.Equal(string.Empty, response.ServerCanonicalIdentity);
        Assert.NotNull(response.ErrorMessage);
    }

    [Fact]
    public async Task Returns_no_connection_when_database_name_missing()
    {
        var handler = BuildHandler(databaseName: null, identity: "PROD-DB01");

        var response = await handler.HandleAsync(
            new SchemaIdentifyRequest { SessionId = "s1" }, null!, CancellationToken.None);

        Assert.False(response.HasConnection);
        Assert.NotNull(response.ErrorMessage);
    }

    [Fact]
    public async Task Empty_session_id_is_rejected()
    {
        var handler = BuildHandler(databaseName: "x", identity: "y");

        var response = await handler.HandleAsync(
            new SchemaIdentifyRequest { SessionId = string.Empty }, null!, CancellationToken.None);

        Assert.False(response.HasConnection);
        Assert.Contains("SessionId", response.ErrorMessage);
    }

    [Fact]
    public async Task Resolver_exception_is_swallowed_into_error_message()
    {
        // The resolver runs SQL in production; throwing would crash the bridge. The handler
        // turns it into a "no connection" response with the message preserved for the
        // diagnostics bundle (FR-013a).
        var handler = new SchemaIdentifyHandler(
            databaseLookup: _ => "master",
            identityResolver: _ => throw new System.InvalidOperationException("connection closed"));

        var response = await handler.HandleAsync(
            new SchemaIdentifyRequest { SessionId = "s1" }, null!, CancellationToken.None);

        Assert.False(response.HasConnection);
        Assert.Contains("connection closed", response.ErrorMessage);
    }

    // ── Aliasing: same canonical identity across multiple host strings ────────

    [Theory]
    [InlineData("PROD-DB01.contoso.local")]
    [InlineData("10.0.0.1,1433")]
    [InlineData("prod-db01")]
    public async Task Same_canonical_identity_collapses_distinct_host_aliases(string sessionAlias)
    {
        // Three sessions, three distinct host strings, ALL pointing at the same SQL Server.
        // The identity resolver -- which in production runs @@SERVERNAME -- returns the
        // canonical name regardless of how the client connected. The browser keys the
        // cache by (canonical, db) so these three sessions share one entry.
        const string canonical = "PROD-DB01\\SQL2022";

        var handler = new SchemaIdentifyHandler(
            databaseLookup: _ => "AdventureWorks2022",
            identityResolver: _ => canonical);

        var response = await handler.HandleAsync(
            new SchemaIdentifyRequest { SessionId = sessionAlias }, null!, CancellationToken.None);

        Assert.True(response.HasConnection);
        Assert.Equal(canonical, response.ServerCanonicalIdentity);
    }

    // ── ParseServerFromConnectionString helper ────────────────────────────────

    [Theory]
    [InlineData("Server=PROD-DB01;Database=master;Integrated Security=true", "PROD-DB01")]
    [InlineData("Data Source=PROD-DB01\\SQL2022;Initial Catalog=foo;", "PROD-DB01\\SQL2022")]
    [InlineData("data source = 10.0.0.1,1433 ; database = bar", "10.0.0.1,1433")]
    [InlineData("Address=192.168.1.5;Database=x", "192.168.1.5")]
    [InlineData("Network Address=tcp:host,1433;Database=y", "tcp:host,1433")]
    public void ParseServerFromConnectionString_extracts_data_source(string cs, string expected)
    {
        Assert.Equal(expected, SchemaIdentifyHandlerSupport.ParseServerFromConnectionString(cs));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Database=master;Integrated Security=true")]
    [InlineData("garbage without equals")]
    public void ParseServerFromConnectionString_returns_empty_when_no_server_key(string? cs)
    {
        Assert.Equal(string.Empty, SchemaIdentifyHandlerSupport.ParseServerFromConnectionString(cs));
    }
}
