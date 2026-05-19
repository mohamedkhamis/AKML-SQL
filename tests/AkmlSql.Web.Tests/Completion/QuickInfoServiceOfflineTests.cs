using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using MessagePack;
using Xunit;

namespace AkmlSql.Web.Tests.Completion;

/// <summary>
/// Spec 021 (web edition) — M5 task T109 follow-up. QuickInfoService offline path:
/// when the bridge is closed, the service walks the persisted editor session for
/// the identifier under the caret, then matches it against the cached PhaseB blob.
/// </summary>
public sealed class QuickInfoServiceOfflineTests
{
    private static IEngineBridge ClosedBridge() =>
        new EngineBridge(() => new FakeBridgeWebSocket(_ => null!),
            new DiagnosticsRingBuffer(new InMemoryIndexedDbAdapter()));

    private static byte[] PhaseBBlob(params (string Schema, string Object, (string Name, string Type, bool PK)[] Cols)[] objects)
    {
        return MessagePackSerializer.Serialize(new SchemaPhasePayload
        {
            DatabaseName = "db", Phase = 2, Checksum = "PhaseB:t",
            Schemas = objects.GroupBy(o => o.Schema).Select(g => new SchemaPhaseSchema
            {
                Name = g.Key,
                Objects = g.Select(o => new SchemaPhaseObject
                {
                    SchemaName = o.Schema,
                    ObjectName = o.Object,
                    ObjectType = 0,
                    Columns = o.Cols.Select(c => new SchemaPhaseColumn
                    {
                        Name = c.Name, TypeName = c.Type, IsPrimaryKey = c.PK,
                    }).ToArray(),
                }).ToArray(),
            }).ToArray(),
        });
    }

    private static async Task<(QuickInfoService svc, IEditorSessionStore session, ISchemaCacheStore cache)>
        BuildAsync(string documentText, int cursorOffset, byte[]? phaseB = null)
    {
        var db = new InMemoryIndexedDbAdapter();
        var cache = new SchemaCacheStore(db);
        var session = new EditorSessionStore(db);
        await session.SaveAsync(new EditorSessionRecord { DocumentText = documentText, CursorOffset = cursorOffset });
        await session.FlushAsync();   // bypass the 500 ms debounce for tests

        if (phaseB != null)
        {
            await cache.SetAsync(new SchemaSnapshot
            {
                ServerCanonicalIdentity = "s", DatabaseName = "db", PhaseB = phaseB,
            });
        }

        return (new QuickInfoService(ClosedBridge(), cache, session), session, cache);
    }

    [Fact]
    public async Task With_no_cache_or_session_returns_empty()
    {
        var service = new QuickInfoService(ClosedBridge());
        var response = await service.GetAsync(new QuickInfoRequest(), CancellationToken.None);
        Assert.NotNull(response);
        Assert.Empty(response.Header);
    }

    [Fact]
    public async Task Dotted_identifier_resolves_schema_object()
    {
        const string doc = "SELECT * FROM dbo.Customers";
        var phaseB = PhaseBBlob(("dbo", "Customers",
            new[] { ("Id", "int", true), ("Name", "nvarchar", false) }));
        // Cursor sits inside "Customers".
        var cursor = doc.IndexOf("Customers", System.StringComparison.Ordinal) + 3;
        var (service, _, _) = await BuildAsync(doc, cursor, phaseB);

        var response = await service.GetAsync(new QuickInfoRequest { CursorOffset = cursor }, CancellationToken.None);

        Assert.Equal("dbo.Customers", response.Header);
        Assert.Equal("Table", response.ObjectType);
        Assert.Equal(2, response.Details.Length);
        Assert.Contains(response.Details, d => d.Label == "Id" && d.Value.Contains("(PK)"));
    }

    [Fact]
    public async Task Column_under_caret_resolves_when_prefix_is_unknown_alias()
    {
        // No alias-table inference offline -- a column named "Name" still resolves
        // because the search falls back to a column-name scan across all objects.
        const string doc = "SELECT c.Name FROM dbo.Customers c";
        var phaseB = PhaseBBlob(("dbo", "Customers",
            new[] { ("Id", "int", true), ("Name", "nvarchar", false) }));
        var cursor = doc.IndexOf("Name", System.StringComparison.Ordinal) + 1;
        var (service, _, _) = await BuildAsync(doc, cursor, phaseB);

        var response = await service.GetAsync(new QuickInfoRequest { CursorOffset = cursor }, CancellationToken.None);

        Assert.Equal("Column", response.ObjectType);
        Assert.Equal("dbo.Customers.Name", response.Header);
        Assert.Contains(response.Details, d => d.Label == "Type" && d.Value.Contains("nvarchar"));
    }

    [Fact]
    public async Task Caret_on_unknown_identifier_returns_empty()
    {
        const string doc = "SELECT * FROM Something";
        var phaseB = PhaseBBlob(("dbo", "Customers",
            new[] { ("Id", "int", true) }));
        var cursor = doc.IndexOf("Something", System.StringComparison.Ordinal) + 2;
        var (service, _, _) = await BuildAsync(doc, cursor, phaseB);

        var response = await service.GetAsync(new QuickInfoRequest { CursorOffset = cursor }, CancellationToken.None);

        Assert.Empty(response.Header);
    }

    [Fact]
    public async Task Schema_name_under_caret_returns_schema_object_count()
    {
        const string doc = "SELECT * FROM sales.Invoices";
        var phaseB = PhaseBBlob(
            ("sales", "Invoices", new[] { ("Id", "int", true) }),
            ("sales", "InvoiceLines", new[] { ("Id", "int", true) }));
        var cursor = doc.IndexOf("sales", System.StringComparison.Ordinal) + 2;
        var (service, _, _) = await BuildAsync(doc, cursor, phaseB);

        var response = await service.GetAsync(new QuickInfoRequest { CursorOffset = cursor }, CancellationToken.None);

        Assert.Equal("Schema", response.ObjectType);
        Assert.Equal("sales", response.Header);
        Assert.Contains("2 object", response.Description);
    }
}
