using System.Text.Json;
using AkmlSql.Core;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Bridge;

/// <summary>
/// Spec 047 (WEB-010): a saved SQL Server connection used to carry PostgreSQL's port ("effectivePort": 5432), a computed
/// property System.Text.Json wrote out. Only stored fields are written now; records saved before still load.
/// </summary>
public sealed class SavedConnectionJsonTests
{
    [Fact]
    public void A_SQL_Server_row_is_written_without_a_PostgreSQL_port()
    {
        var row = new SavedSqlConnection { Name = "Local", Kind = DatabaseKind.SqlServer, Server = "localhost", Database = "master", WindowsAuth = true };

        var json = JsonSerializer.Serialize(row);

        Assert.DoesNotContain("effectivePort", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SavedSqlConnection.DefaultPostgresPort, row.EffectivePort);   // still computed for the PostgreSQL form
    }

    [Fact]
    public void A_row_written_by_an_earlier_build_still_loads()
    {
        const string old = "{\"Id\":\"abc\",\"Name\":\"Local\",\"Kind\":0,\"Server\":\"localhost\",\"Database\":\"master\"," +
                           "\"WindowsAuth\":true,\"EffectivePort\":5432}";

        var row = JsonSerializer.Deserialize<SavedSqlConnection>(old);

        Assert.NotNull(row);
        Assert.Equal("abc", row!.Id);
        Assert.Equal("localhost", row.Server);
        Assert.Null(row.Port);
    }

    [Fact]
    public async Task The_store_round_trip_writes_no_PostgreSQL_port()
    {
        var adapter = new RecordingIndexedDbAdapter();
        var store = new SavedSqlConnectionStore(adapter);
        await store.AddAsync(new SavedSqlConnection { Name = "Local", Server = "localhost", Database = "master", WindowsAuth = true });

        var loaded = Assert.Single(await store.ListAsync());
        Assert.Equal("localhost", loaded.Server);
        Assert.NotEmpty(adapter.Writes);
        foreach (var (_, _, written) in adapter.Writes)
            Assert.DoesNotContain("effectivePort", written, StringComparison.OrdinalIgnoreCase);
    }
}
