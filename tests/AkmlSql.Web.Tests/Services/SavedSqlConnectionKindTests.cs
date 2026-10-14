using System;
using System.Text.Json;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 042 (FR-001, FR-002, data-model §2): a saved connection has a kind. IndexedDB values are schemaless JSON, so
/// a record written before the feature simply lacks the new fields and must read back as a SQL Server connection with
/// no other change; a PostgreSQL record validates the fields that make a PostgreSQL connection.
/// </summary>
public sealed class SavedSqlConnectionKindTests
{
    private static ISavedSqlConnectionStore Build(out InMemoryIndexedDbAdapter adapter)
    {
        adapter = new InMemoryIndexedDbAdapter();
        return new SavedSqlConnectionStore(adapter);
    }

    private static SavedSqlConnection Postgres(Action<SavedSqlConnection>? tweak = null)
    {
        var connection = new SavedSqlConnection
        {
            Id = "pg1",
            Name = "Local PostgreSQL",
            Kind = DatabaseKind.PostgreSql,
            Server = "localhost",
            Database = "northwind",
            Login = "postgres",
            Port = 5432,
            SslMode = "prefer",
        };
        tweak?.Invoke(connection);
        return connection;
    }

    [Fact]
    public async Task A_record_saved_before_the_feature_reads_back_as_SQL_Server_with_defaults()
    {
        var store = Build(out var adapter);
        // Exactly the JSON the previous build stored: no Kind, Port, SslMode, RootCertificatePath or RememberPassword.
        await adapter.SetAsync(StoreNames.SavedSqlConnections, "old1",
            "{\"Id\":\"old1\",\"Name\":\"Prod\",\"Server\":\"sql01\",\"Database\":\"master\",\"WindowsAuth\":false,\"Login\":\"sa\",\"LastConnectedAt\":null}");

        var loaded = await store.GetAsync("old1");

        Assert.NotNull(loaded);
        Assert.Equal(DatabaseKind.SqlServer, loaded!.Kind);
        Assert.Null(loaded.Port);
        Assert.Null(loaded.SslMode);
        Assert.Null(loaded.RootCertificatePath);
        Assert.False(loaded.RememberPassword);
        Assert.False(loaded.WindowsAuth);
        Assert.Equal("sa", loaded.Login);
        Assert.Single(await store.ListAsync());
    }

    [Fact]
    public async Task A_PostgreSQL_record_round_trips_every_field()
    {
        var store = Build(out _);
        await store.AddAsync(Postgres(c =>
        {
            c.RootCertificatePath = @"C:\certs\root.crt";
            c.SslMode = "verify-full";
            c.RememberPassword = true;
        }));

        var loaded = await store.GetAsync("pg1");

        Assert.Equal(DatabaseKind.PostgreSql, loaded!.Kind);
        Assert.Equal(5432, loaded.Port);
        Assert.Equal("verify-full", loaded.SslMode);
        Assert.Equal(@"C:\certs\root.crt", loaded.RootCertificatePath);
        Assert.True(loaded.RememberPassword);
        Assert.Equal("postgres", loaded.Login);
    }

    [Fact]
    public async Task No_password_is_ever_serialized_even_with_remember_password_on()
    {
        var store = Build(out var adapter);
        await store.AddAsync(Postgres(c => c.RememberPassword = true));

        var json = await adapter.GetAsync(StoreNames.SavedSqlConnections, "pg1");

        // The flag is stored, under a key that does not mention a password; a secret never is. The existing store
        // test scans the whole payload for the word, so nothing in the record may contain it.
        Assert.DoesNotContain("password", json!, StringComparison.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json!);
        Assert.True(doc.RootElement.GetProperty("RememberOnEngine").GetBoolean());
        Assert.False(doc.RootElement.TryGetProperty("RememberPassword", out _));
    }

    [Fact]
    public async Task Both_kinds_with_the_same_name_coexist()
    {
        var store = Build(out _);
        await store.AddAsync(new SavedSqlConnection { Id = "ms", Name = "northwind", Server = "localhost", Database = "northwind" });
        await store.AddAsync(Postgres(c => { c.Id = "pg"; c.Name = "northwind"; }));

        var all = await store.ListAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, c => c.Kind == DatabaseKind.SqlServer);
        Assert.Contains(all, c => c.Kind == DatabaseKind.PostgreSql);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task A_PostgreSQL_record_needs_a_user_name(string? login)
    {
        var store = Build(out _);
        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync(Postgres(c => c.Login = login)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public async Task A_PostgreSQL_port_must_be_a_valid_port(int port)
    {
        var store = Build(out _);
        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync(Postgres(c => c.Port = port)));
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("prefer")]
    [InlineData("require")]
    [InlineData("verify-ca")]
    [InlineData("verify-full")]
    [InlineData("VERIFY-FULL")]
    [InlineData(null)]
    public async Task The_five_TLS_modes_and_unset_are_accepted(string? mode)
    {
        var store = Build(out _);
        await store.AddAsync(Postgres(c => c.SslMode = mode));
        Assert.NotNull(await store.GetAsync("pg1"));
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("allow")]   // a libpq mode this product does not offer
    [InlineData("verify_full")]
    public async Task An_unknown_TLS_mode_is_refused(string mode)
    {
        var store = Build(out _);
        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync(Postgres(c => c.SslMode = mode)));
    }

    [Fact]
    public async Task The_port_is_unset_by_default_and_means_5432()
    {
        var store = Build(out _);
        await store.AddAsync(Postgres(c => c.Port = null));

        var loaded = await store.GetAsync("pg1");

        Assert.Null(loaded!.Port);
        Assert.Equal(5432, SavedSqlConnection.DefaultPostgresPort);
        Assert.Equal(5432, loaded.EffectivePort);
    }

    [Fact]
    public async Task A_SQL_Server_record_does_not_need_a_port_or_TLS_mode_and_ignores_them()
    {
        var store = Build(out _);
        await store.AddAsync(new SavedSqlConnection { Id = "ms", Name = "n", Server = "s", Database = "d", Port = 99999, SslMode = "bogus" });

        Assert.NotNull(await store.GetAsync("ms")); // the PostgreSQL rules are not applied to a SQL Server record
    }
}
