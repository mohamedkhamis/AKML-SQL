using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Handlers.Refactoring;
using MessagePack;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring;

/// <summary>
/// Spec 047 (SSMS-003, data-model §9, contracts/workspace-ai.md §5) — Find Invalid Objects reports how many references it
/// checked, not how many it could not resolve. Before, <c>TotalScanned</c> counted only the unresolved candidates, so a
/// clean Northwind (63 references, all resolved) read "No invalid objects found (0 dependency rows scanned)", which looks
/// like a scan that did not run.
/// </summary>
public sealed class FindInvalidObjectsCountTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

    private static FindInvalidObjectsHandler.DependencyRow Candidate(string entity, bool callerDependent = false) =>
        new("dbo", "vw_X", "V ", null, null, "dbo", entity, IsAmbiguous: false, IsCallerDependent: callerDependent);

    [Fact]
    public void The_count_is_every_reference_checked_not_the_unresolved_candidates()
    {
        // Two candidates the server could not bind, both resolved at run time (caller-dependent): no invalid object.
        var clean = FindInvalidObjectsHandler.BuildResponse(63, new[] { Candidate("Orders", true), Candidate("Customers", true) }, Now);
        Assert.Equal(63, clean.TotalScanned);
        Assert.Empty(clean.Records);
        Assert.True(clean.IsFinalChunk);

        var broken = FindInvalidObjectsHandler.BuildResponse(63, new[] { Candidate("DroppedTable") }, Now);
        Assert.Equal(63, broken.TotalScanned);
        Assert.Single(broken.Records);
    }

    private const string Northwind =
        "Server=(local);Database=Northwind;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=5";

    /// <summary>Live: the scan of a local Northwind counts what an independent query counts, and finds nothing invalid.</summary>
    [SkippableFact]
    public async Task On_Northwind_the_count_matches_the_dependency_catalog()
    {
        int expected;
        try
        {
            await using var conn = new SqlConnection(Northwind);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT COUNT(*) FROM sys.sql_expression_dependencies d JOIN sys.objects o ON o.object_id = d.referencing_id WHERE o.is_ms_shipped = 0;", conn);
            expected = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        catch (Exception ex) { throw new SkipException($"No local SQL Server with Northwind: {ex.Message}"); }
        Skip.If(expected == 0, "This Northwind has no object references.");

        var request = new RpcMessage
        {
            MessageType = MessageTypes.FindInvalidObjects,
            RequestId = 7,
            Payload = MessagePackSerializer.Serialize(new FindInvalidObjectsRequest { SessionId = "s", DatabaseName = "Northwind" }),
        };
        var reply = await new FindInvalidObjectsHandler().HandleAsync(request, _ => (Northwind, "Northwind"), CancellationToken.None);

        var response = MessagePackSerializer.Deserialize<FindInvalidObjectsResponse>(reply!.Payload);
        Assert.Equal(0, response.Status);
        Assert.Equal(expected, response.TotalScanned);
        Assert.Empty(response.Records);
    }
}
