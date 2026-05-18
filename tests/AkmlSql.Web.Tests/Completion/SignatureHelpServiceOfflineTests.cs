using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using MessagePack;
using Xunit;

namespace AkmlSql.Web.Tests.Completion;

/// <summary>
/// Spec 021 (web edition) — M5 task T109 follow-up. SignatureHelpService offline
/// path: when the bridge is closed, the service scans for the enclosing function
/// call in the persisted document and matches it against the cached PhaseB blob.
/// </summary>
public sealed class SignatureHelpServiceOfflineTests
{
    private static IEngineBridge ClosedBridge() =>
        new EngineBridge(() => new FakeBridgeWebSocket(_ => null!),
            new DiagnosticsRingBuffer(new InMemoryIndexedDbAdapter()));

    private static byte[] PhaseBWithProcedure(
        string schema, string procName,
        params (string Name, string Type, bool IsOutput, bool HasDefault)[] parameters)
    {
        return MessagePackSerializer.Serialize(new SchemaPhasePayload
        {
            DatabaseName = "db", Phase = 2,
            Schemas = new[]
            {
                new SchemaPhaseSchema
                {
                    Name = schema,
                    Objects = new[]
                    {
                        new SchemaPhaseObject
                        {
                            SchemaName = schema,
                            ObjectName = procName,
                            ObjectType = 2,   // Procedure
                            Parameters = parameters.Select(p => new SchemaPhaseParameter
                            {
                                Name = p.Name, TypeName = p.Type,
                                IsOutput = p.IsOutput, HasDefault = p.HasDefault,
                            }).ToArray(),
                        },
                    },
                },
            },
        });
    }

    private static async Task<SignatureHelpService> BuildAsync(
        string documentText, int cursorOffset, byte[]? phaseB = null)
    {
        var db = new InMemoryIndexedDbAdapter();
        var cache = new SchemaCacheStore(db);
        var session = new EditorSessionStore(db);
        await session.SaveAsync(new EditorSessionRecord { DocumentText = documentText, CursorOffset = cursorOffset });
        await session.FlushAsync();
        if (phaseB != null)
        {
            await cache.SetAsync(new SchemaSnapshot
            {
                ServerCanonicalIdentity = "s", DatabaseName = "db", PhaseB = phaseB,
            });
        }
        return new SignatureHelpService(ClosedBridge(), cache, session);
    }

    [Fact]
    public async Task With_no_cache_or_session_returns_empty()
    {
        var service = new SignatureHelpService(ClosedBridge());
        var response = await service.GetAsync(new SignatureRequest(), CancellationToken.None);
        Assert.NotNull(response);
        Assert.Empty(response.FunctionName);
    }

    [Fact]
    public async Task Paren_call_resolves_procedure_and_parameter_list()
    {
        // Offline scanner is paren-based; the online engine also handles EXEC syntax
        // but that's a TSql parser job we don't replicate offline.
        const string call = "SELECT dbo.UpdateOrder(1, 'done')";
        var phaseB = PhaseBWithProcedure("dbo", "UpdateOrder",
            ("@orderId", "int", false, false),
            ("@status", "nvarchar", false, false));
        var cursor = call.IndexOf("'done'", System.StringComparison.Ordinal) + 2;
        var service = await BuildAsync(call, cursor, phaseB);

        var response = await service.GetAsync(new SignatureRequest { CursorOffset = cursor }, CancellationToken.None);

        Assert.Equal("dbo.UpdateOrder", response.FunctionName);
        Assert.NotNull(response.Overloads);
        Assert.Single(response.Overloads!);
        Assert.Equal(2, response.Overloads![0].Parameters.Length);
        Assert.Equal(1, response.ActiveParameter);   // caret is after the first comma
        Assert.Contains("@orderId", response.Overloads[0].Label);
    }

    [Fact]
    public async Task Caret_outside_any_call_returns_empty()
    {
        const string doc = "SELECT * FROM dbo.Customers";
        var phaseB = PhaseBWithProcedure("dbo", "UpdateOrder",
            ("@id", "int", false, false));
        var cursor = doc.IndexOf("Customers", System.StringComparison.Ordinal);
        var service = await BuildAsync(doc, cursor, phaseB);

        var response = await service.GetAsync(new SignatureRequest { CursorOffset = cursor }, CancellationToken.None);

        Assert.Empty(response.FunctionName);
    }

    [Fact]
    public async Task Call_for_unknown_procedure_returns_empty()
    {
        const string doc = "SELECT dbo.Mystery(42, 'x')";
        var phaseB = PhaseBWithProcedure("dbo", "UpdateOrder",
            ("@id", "int", false, false));
        var cursor = doc.IndexOf("42", System.StringComparison.Ordinal);
        var service = await BuildAsync(doc, cursor, phaseB);

        var response = await service.GetAsync(new SignatureRequest { CursorOffset = cursor }, CancellationToken.None);

        Assert.Empty(response.FunctionName);
    }

    [Fact]
    public async Task Parameter_index_counts_commas_at_depth_zero_only()
    {
        // ConcatLike(a, ConcatInner(b, c), d) — caret after the outer second comma
        // should land at parameter index 2 (counting outer commas only).
        const string doc = "SELECT dbo.ConcatLike(a, ConcatInner(b, c), d)";
        var phaseB = PhaseBWithProcedure("dbo", "ConcatLike",
            ("@a", "nvarchar", false, false),
            ("@b", "nvarchar", false, false),
            ("@c", "nvarchar", false, false));
        var cursor = doc.IndexOf(", d", System.StringComparison.Ordinal) + 2;
        var service = await BuildAsync(doc, cursor, phaseB);

        var response = await service.GetAsync(new SignatureRequest { CursorOffset = cursor }, CancellationToken.None);

        Assert.Equal("dbo.ConcatLike", response.FunctionName);
        Assert.Equal(2, response.ActiveParameter);
    }
}
