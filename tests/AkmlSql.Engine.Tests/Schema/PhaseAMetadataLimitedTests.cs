using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Handlers.Schema;
using AkmlSql.Engine.Schema;
using MessagePack;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AkmlSql.Engine.Tests.Schema;

/// <summary>
/// Spec 047 (WEB-013, data-model §8) — when the engine's login cannot see every object in the database (no VIEW
/// DEFINITION), Phase A says so (<c>SchemaPhasePayload.MetadataLimited</c>, key 8), so the explorer can tell the user why
/// procedures are missing. Before, the tree just showed fewer procedures (none, for the engine running as
/// NT AUTHORITY\SYSTEM) with nothing to say why.
/// </summary>
public sealed class PhaseAMetadataLimitedTests
{
    [Fact]
    public void A_limited_cache_says_so_in_both_phase_payloads()
    {
        var cache = new DatabaseCache { CacheKey = "srv:Northwind", MetadataLimited = true };

        var a = MessagePackSerializer.Deserialize<SchemaPhasePayload>(SchemaPhaseSerializer.SerializePhaseA(cache, "Northwind"));
        var b = MessagePackSerializer.Deserialize<SchemaPhasePayload>(SchemaPhaseSerializer.SerializePhaseB(cache, "Northwind"));
        Assert.True(a.MetadataLimited);
        Assert.True(b.MetadataLimited);

        cache.MetadataLimited = false;
        Assert.False(MessagePackSerializer.Deserialize<SchemaPhasePayload>(SchemaPhaseSerializer.SerializePhaseA(cache, "Northwind")).MetadataLimited);
    }

    [Fact]
    public void A_payload_from_an_engine_before_the_flag_reads_as_not_limited()
    {
        // Keys 0-7 only, as an older engine writes them (an integer-keyed object is a MessagePack array).
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(8);
        writer.Write("Northwind");
        writer.Write(1);
        writer.Write("PhaseA:0");
        for (int i = 0; i < 5; i++) writer.WriteArrayHeader(0);   // schemas, FKs, inaccessible, hidden, search path
        writer.Flush();

        var payload = MessagePackSerializer.Deserialize<SchemaPhasePayload>(buffer.WrittenMemory);

        Assert.Equal("Northwind", payload.DatabaseName);
        Assert.False(payload.MetadataLimited);
    }

    private const string Northwind =
        "Server=(local);Database=Northwind;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=5";

    private static async Task<object?> ScalarAsync(SqlConnection conn, string sql)
    {
        await using var cmd = new SqlCommand(sql, conn);
        return await cmd.ExecuteScalarAsync();
    }

    /// <summary>
    /// Live: the probe as the engine's service account sees it. EXECUTE AS is session-scoped and reverted, so nothing in the
    /// database changes. Skips without a local Northwind, when the runner may not impersonate, or when the account already
    /// has VIEW DEFINITION there (the V15 grant).
    /// </summary>
    [SkippableFact]
    public async Task The_probe_reports_a_login_without_VIEW_DEFINITION()
    {
        SqlConnection conn;
        try
        {
            conn = new SqlConnection(Northwind);
            await conn.OpenAsync();
        }
        catch (Exception ex) { throw new SkipException($"No local SQL Server with Northwind: {ex.Message}"); }

        await using (conn)
        {
            Assert.False(await SchemaMetadataService.ReadMetadataLimitedAsync(conn, CancellationToken.None));   // the runner sees everything

            try { await ScalarAsync(conn, @"EXECUTE AS LOGIN = N'NT AUTHORITY\SYSTEM';"); }
            catch (SqlException ex) { throw new SkipException($"EXECUTE AS LOGIN was refused: {ex.Message}"); }
            try
            {
                int held;
                try
                {
                    held = Convert.ToInt32(await ScalarAsync(conn,
                        "SELECT COUNT(*) FROM fn_my_permissions(NULL, 'DATABASE') WHERE permission_name = 'VIEW DEFINITION';"));
                }
                catch (SqlException ex) { throw new SkipException($"NT AUTHORITY\\SYSTEM cannot use Northwind here: {ex.Message}"); }
                Skip.If(held > 0, "NT AUTHORITY\\SYSTEM already has VIEW DEFINITION on Northwind here.");

                Assert.True(await SchemaMetadataService.ReadMetadataLimitedAsync(conn, CancellationToken.None));
            }
            finally
            {
                await ScalarAsync(conn, "REVERT;");
            }

            Assert.False(await SchemaMetadataService.ReadMetadataLimitedAsync(conn, CancellationToken.None));
        }
    }
}
