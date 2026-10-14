using Npgsql;
using Xunit;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 live-test helper. Reads <c>AKML_PG_TEST_CONNECTION</c> (an Npgsql connection string to the
/// <c>postgres</c> database of a throwaway server, for example the one started by
/// <c>scripts/pg-test-server.ps1</c>) and hands out isolated databases so tests never see each other's
/// objects. Tests call <see cref="SkipUnlessAvailable"/> first; with no variable, or no server behind it,
/// they are skipped (not failed), exactly like the SQL Server integration tests.
/// </summary>
public static class PgTestServer
{
    public const string EnvVar = "AKML_PG_TEST_CONNECTION";

    private static readonly Lazy<string?> Reachable = new(Probe);

    /// <summary>The admin connection string when the server answers, otherwise <c>null</c>.</summary>
    public static string? AdminConnectionString => Reachable.Value;

    public static void SkipUnlessAvailable()
    {
        Skip.If(AdminConnectionString is null, $"No PostgreSQL test server: set {EnvVar} (see scripts/pg-test-server.ps1).");
    }

    /// <summary>Creates a fresh database; dispose it to drop it. Optional <paramref name="seedSql"/> runs inside it.</summary>
    public static async Task<PgTestDatabase> CreateDatabaseAsync(string? seedSql = null)
    {
        SkipUnlessAvailable();
        var name = "akml_t_" + Guid.NewGuid().ToString("N")[..12];

        await using (var admin = new NpgsqlConnection(AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = name };
        var database = new PgTestDatabase(name, builder.ConnectionString, AdminConnectionString!);
        if (!string.IsNullOrWhiteSpace(seedSql))
        {
            await using var connection = new NpgsqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await using var seed = new NpgsqlCommand(seedSql, connection);
            await seed.ExecuteNonQueryAsync();
        }
        return database;
    }

    /// <summary>Reads a fixture from <c>PostgreSql/Fixtures</c> beside the test assembly.</summary>
    public static string Fixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PostgreSql", "Fixtures", fileName);
        return File.ReadAllText(path);
    }

    private static string? Probe()
    {
        var connectionString = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { Timeout = 5 };
            using var connection = new NpgsqlConnection(builder.ConnectionString);
            connection.Open();
            using var command = new NpgsqlCommand("SELECT 1", connection);
            command.ExecuteScalar();
            return builder.ConnectionString;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>A database created by <see cref="PgTestServer.CreateDatabaseAsync"/>; disposing drops it.</summary>
public sealed class PgTestDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;

    internal PgTestDatabase(string name, string connectionString, string adminConnectionString)
    {
        Name = name;
        ConnectionString = connectionString;
        _adminConnectionString = adminConnectionString;
    }

    public string Name { get; }

    /// <summary>Connection string to this database (the admin credentials of the test server).</summary>
    public string ConnectionString { get; }

    public async ValueTask DisposeAsync()
    {
        try
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(_adminConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{Name}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
        catch
        {
            // A leftover akml_t_* database in a throwaway server is harmless; never fail a test on cleanup.
        }
    }
}
