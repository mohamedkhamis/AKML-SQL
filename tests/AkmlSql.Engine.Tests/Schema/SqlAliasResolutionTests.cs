using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Handlers.Control;
using AkmlSql.Engine.Pairing;
using AkmlSql.Engine.Schema;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AkmlSql.Engine.Tests.Schema;

/// <summary>
/// A SQL Server client alias ("ServerDemo" → DBMSSOCN,192.168.4.5,1433 in the ConnectTo registry
/// key) is resolved before the engine connects: SqlClient does not read the alias registry, so the
/// schema connection, Test connection and the database list used to fail with "server not found".
/// The alias registry is replaced by a fake here; nothing is written to the machine.
/// </summary>
[Collection(SqlAliasCollection.Name)]
public sealed class SqlAliasResolutionTests : IDisposable
{
    private readonly Func<string, string?> _real = SqlAliasResolution.Lookup;

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ServerDemo"] = "DBMSSOCN,192.168.4.5,1433",
        ["AkmlLocalAlias"] = "DBMSSOCN,127.0.0.1,1433",
    };

    public SqlAliasResolutionTests() =>
        SqlAliasResolution.Lookup = name => Aliases.TryGetValue(name, out var v) ? v : null;

    public void Dispose() => SqlAliasResolution.Lookup = _real;

    [Fact]
    public void An_alias_data_source_becomes_its_target_and_the_rest_is_kept()
    {
        var resolved = new SqlConnectionStringBuilder(SqlAliasResolution.Apply(
            "Data Source=ServerDemo;Initial Catalog=Northwind;User ID=app;Password='p;a;ss';TrustServerCertificate=true"));

        Assert.Equal("tcp:192.168.4.5,1433", resolved.DataSource);
        Assert.Equal("Northwind", resolved.InitialCatalog);
        Assert.Equal("app", resolved.UserID);
        Assert.Equal("p;a;ss", resolved.Password);
        Assert.True(resolved.TrustServerCertificate);
    }

    [Theory]
    [InlineData("Data Source=192.168.4.5;Initial Catalog=Northwind;Integrated Security=true")]
    [InlineData("Data Source=(local);Initial Catalog=Northwind;Integrated Security=true")]
    [InlineData("Data Source=tcp:ServerDemo;Initial Catalog=Northwind;Integrated Security=true")]
    [InlineData("not a connection string")]
    [InlineData("")]
    public void Anything_else_is_passed_through_unchanged(string connectionString)
        => Assert.Equal(connectionString, SqlAliasResolution.Apply(connectionString));

    [Fact]
    public void The_bridge_guard_judges_the_server_the_alias_points_at()
    {
        // Windows auth from the web bridge is allowed only for the engine's own machine: an alias
        // for 127.0.0.1 is local, an alias for another machine is not.
        const string local = "Data Source=AkmlLocalAlias;Initial Catalog=Northwind;Integrated Security=true";
        const string remote = "Data Source=ServerDemo;Initial Catalog=Northwind;Integrated Security=true";

        Assert.Null(BridgeSqlTargetGuard.Check(SqlAliasResolution.Apply(local), fromBridge: true));
        Assert.NotNull(BridgeSqlTargetGuard.Check(SqlAliasResolution.Apply(remote), fromBridge: true));
    }

    [SkippableFact]
    public async Task Test_connection_reaches_the_server_behind_an_alias()
    {
        Skip.IfNot(LocalSqlOnLoopback(), "No local SQL Server listening on 127.0.0.1,1433.");

        var response = await new TestSqlConnectionHandler().HandleAsync(
            new TestSqlConnectionRequest
            {
                ConnectionString = "Data Source=AkmlLocalAlias;Initial Catalog=master;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=5",
            },
            null!, CancellationToken.None);

        Assert.True(response.Ok, response.ErrorMessage);
    }

    private static bool LocalSqlOnLoopback()
    {
        try
        {
            using var conn = new SqlConnection("Data Source=tcp:127.0.0.1,1433;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=3");
            conn.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>The alias registry is process-wide: its tests don't run alongside each other.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlAliasCollection
{
    public const string Name = "SQL Server aliases (not parallel)";
}
