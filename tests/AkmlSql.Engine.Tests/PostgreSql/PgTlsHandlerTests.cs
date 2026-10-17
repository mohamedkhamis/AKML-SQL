using AkmlSql.Core;
using AkmlSql.Core.Connections;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Handlers.Control;
using Xunit;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (FR-101): every handler that opens a PostgreSQL connection words a refused certificate the same way - where it happened, which thing was wrong and
/// what to do - through <c>PgConnectionFailure.DescribeAsync</c>. A fake server with a certificate no one trusts stands in for the PostgreSQL server, so
/// no real one is needed; the live counterpart is <see cref="PgTlsLiveTests"/>.
/// </summary>
[Collection("PostgreSqlLive")]
public sealed class PgTlsHandlerTests : IDisposable
{
    private readonly PgTlsFixtures _fixtures = new();

    public void Dispose() => _fixtures.Dispose();

    private PgFakeTlsServer UntrustedServer() =>
        _fixtures.Serve(_fixtures.NewServerCertificate(_fixtures.NewAuthority(), new[] { "localhost" }, new[] { "127.0.0.1" }));

    private static string Spell(int port) => PgConnectionString.Build("127.0.0.1", port, "postgres", "postgres", "never-sent", "verify-ca", null);

    private static void AssertWorded(string? message, int port)
    {
        Assert.NotNull(message);
        Assert.StartsWith($"The TLS connection to 127.0.0.1:{port} failed: The server's certificate", message);
        Assert.Contains("is not signed by a root this machine trusts", message);
        Assert.DoesNotContain("RemoteCertificateValidationCallback", message);
        Assert.DoesNotContain("never-sent", message);
        Assert.DoesNotContain("..", message);
    }

    private static ConnectionInfo Session(string id, string connectionString) => new()
    {
        SessionId = id, ConnectionString = connectionString, DatabaseName = "postgres", Kind = (int)DatabaseKind.PostgreSql,
    };

    [Fact]
    public async Task Testing_a_connection_words_the_refusal()
    {
        var server = UntrustedServer();

        var response = await new TestSqlConnectionHandler().HandleAsync(
            new TestSqlConnectionRequest { ConnectionString = Spell(server.Port), Kind = (int)DatabaseKind.PostgreSql }, EngineComposition.Build().Context, CancellationToken.None);

        Assert.False(response.Ok);
        AssertWorded(response.ErrorMessage, server.Port);
    }

    [Fact]
    public async Task Listing_the_databases_from_a_connection_string_words_the_refusal()
    {
        var server = UntrustedServer();

        var response = await new ListDatabasesHandler().HandleAsync(
            new ListDatabasesRequest { ConnectionString = Spell(server.Port), Kind = (int)DatabaseKind.PostgreSql }, EngineComposition.Build().Context, CancellationToken.None);

        Assert.False(response.Ok);
        AssertWorded(response.ErrorMessage, server.Port);
    }

    [Fact]
    public async Task Listing_the_databases_of_a_session_words_the_refusal()
    {
        var server = UntrustedServer();
        var engine = EngineComposition.Build();
        var session = Guid.NewGuid().ToString("N");
        engine.Context.Sessions.UpdateSession(Session(session, Spell(server.Port)));

        var response = await new ListDatabasesHandler().HandleAsync(new ListDatabasesRequest { SessionId = session }, engine.Context, CancellationToken.None);

        Assert.False(response.Ok);
        AssertWorded(response.ErrorMessage, server.Port);
    }

    [Fact]
    public async Task Switching_the_database_words_the_refusal_and_keeps_the_session_as_it_was()
    {
        var server = UntrustedServer();
        var engine = EngineComposition.Build();
        var session = Guid.NewGuid().ToString("N");
        engine.Context.Sessions.UpdateSession(Session(session, Spell(server.Port)));

        var response = await new ChangeDatabaseHandler().HandleAsync(
            new ChangeDatabaseRequest { SessionId = session, DatabaseName = "other" }, engine.Context, CancellationToken.None);

        Assert.False(response.Ok);
        AssertWorded(response.ErrorMessage, server.Port);
        Assert.Equal("postgres", engine.Context.Sessions.GetSession(session)!.DatabaseName);
    }
}
