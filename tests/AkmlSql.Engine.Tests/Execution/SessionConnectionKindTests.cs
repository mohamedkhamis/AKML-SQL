using AkmlSql.Core;
using AkmlSql.Engine.Execution;
using AkmlSql.Engine.Tests.PostgreSql;
using Npgsql;
using Xunit;

namespace AkmlSql.Engine.Tests.Execution;

/// <summary>
/// Spec 042 (research R19): one registry, one gate, one cancel registry and one idle sweep serve both kinds;
/// the connection it opens depends on the kind of the call. The SQL Server path is covered by the
/// existing execution tests, which still hand the callback a <c>SqlConnection</c>.
/// </summary>
[Collection("PostgreSqlLive")]
public class SessionConnectionKindTests
{
    [Fact]
    public void GetOrCreate_records_the_kind_and_defaults_to_SQL_Server()
    {
        var registry = new SessionConnectionRegistry();

        Assert.Equal(DatabaseKind.SqlServer, registry.GetOrCreate("a", "Server=(local)").Kind);
        Assert.Equal(DatabaseKind.PostgreSql, registry.GetOrCreate("b", "Host=localhost", DatabaseKind.PostgreSql).Kind);
    }

    [Fact]
    public async Task A_different_kind_for_the_same_session_replaces_the_connection()
    {
        var registry = new SessionConnectionRegistry();
        var first = registry.GetOrCreate("s", "Server=(local)");

        var second = registry.GetOrCreate("s", "Host=localhost", DatabaseKind.PostgreSql);

        Assert.NotSame(first, second);
        Assert.Equal(DatabaseKind.PostgreSql, second.Kind);
        Assert.Same(second, registry.TryGet("s"));
        await registry.DisposeAllAsync();
    }

    [Fact]
    public void The_same_kind_for_the_same_session_keeps_the_connection_and_updates_its_string()
    {
        var registry = new SessionConnectionRegistry();
        var first = registry.GetOrCreate("s", "Host=a", DatabaseKind.PostgreSql);

        var again = registry.GetOrCreate("s", "Host=b", DatabaseKind.PostgreSql);

        Assert.Same(first, again);
        Assert.Equal("Host=b", again.ConnectionString);
    }

    [Fact]
    public async Task A_PostgreSQL_run_to_an_unreachable_server_fails_as_a_connection_error_not_a_cast_error()
    {
        var registry = new SessionConnectionRegistry();
        var connection = registry.GetOrCreate("s", "Host=127.0.0.1;Port=1;Timeout=2;Username=u;Password=p", DatabaseKind.PostgreSql);

        var error = await Record.ExceptionAsync(() => connection.RunExclusivePostgresAsync(
            connection.ConnectionString, (c, _) => Task.FromResult(c.State), CancellationToken.None));

        Assert.NotNull(error);
        Assert.IsNotType<InvalidCastException>(error);
        Assert.True(error is NpgsqlException || error is System.Net.Sockets.SocketException || error is TimeoutException, error.GetType().Name);
        await registry.DisposeAllAsync();
    }

    [SkippableFact]
    public async Task A_PostgreSQL_run_hands_the_callback_an_open_NpgsqlConnection_and_keeps_session_state()
    {
        PgTestServer.SkipUnlessAvailable();
        var registry = new SessionConnectionRegistry();
        var connection = registry.GetOrCreate("s", PgTestServer.AdminConnectionString!, DatabaseKind.PostgreSql);

        var (version, reset1) = await connection.RunExclusivePostgresAsync(
            connection.ConnectionString, async (c, ct) =>
            {
                await using var set = new NpgsqlCommand("SET application_name = 'akml-session-test'", c);
                await set.ExecuteNonQueryAsync(ct);
                return c.PostgreSqlVersion.Major;
            }, CancellationToken.None);
        Assert.True(version >= 14);
        Assert.False(reset1);

        // The second run sees the SET of the first: it is the same physical connection.
        var (applicationName, reset2) = await connection.RunExclusivePostgresAsync(
            connection.ConnectionString, async (c, ct) =>
            {
                await using var show = new NpgsqlCommand("SHOW application_name", c);
                return (string?)await show.ExecuteScalarAsync(ct);
            }, CancellationToken.None);
        Assert.Equal("akml-session-test", applicationName);
        Assert.False(reset2);
        await registry.DisposeAllAsync();
    }
}

[CollectionDefinition("PostgreSqlLive", DisableParallelization = true)]
public sealed class PostgreSqlLiveCollection { }
