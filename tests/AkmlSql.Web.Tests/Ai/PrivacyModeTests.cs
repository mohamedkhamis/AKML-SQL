using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using AkmlSql.Web.Services;
using MessagePack;
using Xunit;

namespace AkmlSql.Web.Tests.Ai;

/// <summary>
/// Spec 028 (M6) task T013 (US1) + review hardening. Verifies the privacy disclosure modes
/// drive exactly what schema the AI prompt carries (resolved from the M5 cache via the
/// rehydrator), real type widths survive the round-trip, and the fully-local guard refuses a
/// cloud provider at the send boundary (FR-004/FR-012). The network-capture proof (SC-003) is
/// the US7 audit; this asserts the same behaviour at the service boundary.
/// </summary>
public sealed class PrivacyModeTests
{
    private const string Sql = "select * from Orders where CustomerId = 1";
    private const string Description = "Orders table description";
    private const string FkName = "FK_Orders_Customers";

    private static SchemaPhasePayload BuildPayload() => new()
    {
        Phase = (int)PopulationPhase.PhaseB,
        Schemas =
        [
            new SchemaPhaseSchema
            {
                Name = "dbo",
                Objects =
                [
                    new SchemaPhaseObject
                    {
                        SchemaName = "dbo",
                        ObjectName = "Orders",
                        ObjectType = (int)DbObjectType.Table,
                        Description = Description,
                        Columns =
                        [
                            new SchemaPhaseColumn { Name = "OrderId", TypeName = "int", IsPrimaryKey = true },
                            new SchemaPhaseColumn { Name = "CustomerId", TypeName = "int" },
                            new SchemaPhaseColumn { Name = "Notes", TypeName = "nvarchar", MaxLength = 100 },
                            new SchemaPhaseColumn { Name = "Total", TypeName = "decimal", Precision = 18, Scale = 2 },
                        ],
                    },
                    new SchemaPhaseObject
                    {
                        SchemaName = "dbo",
                        ObjectName = "Customers",
                        ObjectType = (int)DbObjectType.Table,
                        Columns = [new SchemaPhaseColumn { Name = "CustomerId", TypeName = "int", IsPrimaryKey = true }],
                    },
                ],
            },
        ],
        ForeignKeys =
        [
            new SchemaPhaseForeignKey
            {
                Name = FkName,
                ParentSchema = "dbo", ParentTable = "Orders", ParentColumns = ["CustomerId"],
                ReferencedSchema = "dbo", ReferencedTable = "Customers", ReferencedColumns = ["CustomerId"],
            },
        ],
    };

    private static async Task<AiSchemaContextProvider> BuildProviderAsync(AiFeatureSettings settings, bool seedCache = true)
    {
        var db = new InMemoryIndexedDbAdapter();
        var store = new AiFeatureSettingsStore(db);
        await store.SetAsync(settings);
        var cache = new SchemaCacheStore(db);
        if (seedCache)
        {
            await cache.SetAsync(new SchemaSnapshot
            {
                ServerCanonicalIdentity = "srv",
                DatabaseName = "Sales",
                PhaseB = MessagePackSerializer.Serialize(BuildPayload()),
            });
        }
        return new AiSchemaContextProvider(cache, store);
    }

    [Fact]
    public async Task FullSchema_IncludesColumnsWithRealTypeWidths()
    {
        var provider = await BuildProviderAsync(new AiFeatureSettings { GlobalDefaultMode = AiPrivacyMode.FullSchema });
        var text = await provider.GetSchemaTextAsync("explain", Sql, CancellationToken.None);

        Assert.Contains("Orders", text);
        Assert.Contains("Notes", text);
        // The wire payload now carries facets, so the rehydrated types are accurate (not (0)).
        Assert.Contains("nvarchar(100)", text);
        Assert.Contains("decimal(18,2)", text);
        Assert.DoesNotContain("nvarchar(0)", text);
        Assert.DoesNotContain("decimal(0,0)", text);
        // Full schema uses the canonical formatter, not the names-only emit.
        Assert.DoesNotContain("names only", text);
    }

    [Fact]
    public async Task SchemaNamesOnly_HasNamesButNoTypesForeignKeysOrDescriptions()
    {
        var provider = await BuildProviderAsync(new AiFeatureSettings { GlobalDefaultMode = AiPrivacyMode.SchemaNamesOnly });
        var text = await provider.GetSchemaTextAsync("explain", Sql, CancellationToken.None);

        Assert.Contains("names only", text);
        Assert.Contains("Orders", text);
        Assert.Contains("Notes", text);   // column NAMES are present
        Assert.Contains("Total", text);
        // ...but no data types, no FK names, no descriptions (FR-003).
        Assert.DoesNotContain("nvarchar", text);
        Assert.DoesNotContain("decimal", text);
        Assert.DoesNotContain(FkName, text);
        Assert.DoesNotContain(Description, text);
    }

    [Fact]
    public async Task NoSchema_ReturnsEmptyOnEveryFeature()
    {
        var provider = await BuildProviderAsync(new AiFeatureSettings { GlobalDefaultMode = AiPrivacyMode.NoSchema });
        Assert.Equal(string.Empty, await provider.GetSchemaTextAsync("explain", Sql, CancellationToken.None));
        Assert.Equal(string.Empty, await provider.GetSchemaTextAsync("optimize", Sql, CancellationToken.None));
    }

    [Fact]
    public async Task PerFeatureOverride_BeatsGlobalDefault()
    {
        var settings = new AiFeatureSettings { GlobalDefaultMode = AiPrivacyMode.NoSchema };
        settings.FeatureModeOverrides["explain"] = AiPrivacyMode.FullSchema;
        var provider = await BuildProviderAsync(settings);

        var explain = await provider.GetSchemaTextAsync("explain", Sql, CancellationToken.None);
        var fix = await provider.GetSchemaTextAsync("fix", Sql, CancellationToken.None);

        Assert.Contains("Orders", explain);
        Assert.Equal(string.Empty, fix);
    }

    [Fact]
    public async Task FullyLocal_StillDisclosesFullSchemaText()
    {
        var provider = await BuildProviderAsync(new AiFeatureSettings { GlobalDefaultMode = AiPrivacyMode.FullyLocal });
        var text = await provider.GetSchemaTextAsync("explain", Sql, CancellationToken.None);

        Assert.Contains("Orders", text);
        Assert.DoesNotContain("names only", text);
    }

    [Fact]
    public async Task NoCachedSchema_DegradesToEmpty()
    {
        var provider = await BuildProviderAsync(new AiFeatureSettings { GlobalDefaultMode = AiPrivacyMode.FullSchema }, seedCache: false);
        Assert.Equal(string.Empty, await provider.GetSchemaTextAsync("explain", Sql, CancellationToken.None));
    }

    // --- Fully-local send-time guard (FR-004/FR-012) at the AiPromptService boundary ---

    private static AiPromptService BuildPromptService(string activeProvider, AiPrivacyMode mode, RecordingClient client)
    {
        var db = new InMemoryIndexedDbAdapter();
        var settings = new AiFeatureSettingsStore(db);
        settings.SetAsync(new AiFeatureSettings { GlobalDefaultMode = mode }).GetAwaiter().GetResult();
        return new AiPromptService(client, new FixedPreference(activeProvider), new EmptySchema(), settings);
    }

    [Fact]
    public async Task FullyLocal_WithCloudProvider_RefusesAndDoesNotSend()
    {
        var client = new RecordingClient();
        var svc = BuildPromptService("openai", AiPrivacyMode.FullyLocal, client);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ExplainAsync("select 1", CancellationToken.None));
        Assert.Equal(0, client.SendCount); // nothing left the browser
    }

    [Fact]
    public async Task FullyLocal_WithLocalProvider_Sends()
    {
        var client = new RecordingClient();
        var svc = BuildPromptService("ollama", AiPrivacyMode.FullyLocal, client);

        var result = await svc.ExplainAsync("select 1", CancellationToken.None);
        Assert.Equal("ok", result);
        Assert.Equal(1, client.SendCount);
    }

    private sealed class RecordingClient : IAiClientFactory
    {
        public int SendCount { get; private set; }
        public Task<string> SendAsync(string providerId, AkmlSql.Web.Services.AiChatRequest request, CancellationToken ct)
        {
            SendCount++;
            return Task.FromResult("ok");
        }
#pragma warning disable CS1998 // async iterator with no await is intentional for the fake
        public async IAsyncEnumerable<string> StreamAsync(
            string providerId, AkmlSql.Web.Services.AiChatRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            SendCount++;
            yield return "ok";
        }
#pragma warning restore CS1998
        public bool IsOriginAllowed(string providerId, string origin) => true;
        public bool IsBrowserDirectCapable(string providerId) => true;
    }

    private sealed class FixedPreference : IAiPreference
    {
        private readonly string _id;
        public FixedPreference(string id) => _id = id;
        public Task<string> GetActiveAsync() => Task.FromResult(_id);
        public Task SetActiveAsync(string providerId) => Task.CompletedTask;
    }

    private sealed class EmptySchema : IAiSchemaContextProvider
    {
        public Task<string> GetSchemaTextAsync(string featureId, string? promptOrSql, CancellationToken ct) =>
            Task.FromResult(string.Empty);
    }
}
