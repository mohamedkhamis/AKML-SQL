using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Handlers.Analysis;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using AkmlSql.Engine.Sessions;
using AkmlSql.Engine.Transports;
using MessagePack;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers;

/// <summary>
/// Spec 030 T052 — exercises <see cref="ListAnalysisRulesHandler"/>: it reports one row per
/// discovered rule, enriched with <see cref="RuleMetadataCatalog"/> metadata and the resolved
/// enabled/severity state, and round-trips through the <see cref="RpcRouter"/> as MessageType 133.
/// </summary>
public sealed class ListAnalysisRulesHandlerTests
{
    private static RpcContext CreateContext() => new()
    {
        Sessions = new SessionManager(),
        SchemaCache = new SchemaCacheManager(),
        Logger = Log.Logger,
        SettingsLoader = () => new AppSettings(),
    };

    private static ListAnalysisRulesHandler CreateHandler(RuleRegistry registry) =>
        new(registry, new CaSettingsLoader(), () => new AppSettings());

    [Fact]
    public async Task ListAnalysisRules_returns_one_row_per_discovered_rule()
    {
        var registry = new RuleRegistry();
        var handler = CreateHandler(registry);

        var response = await handler.HandleAsync(new ListAnalysisRulesRequest(), CreateContext(), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(registry.AllRules.Count, response.Rules.Length);
        Assert.True(response.Rules.Length > 100, $"expected 120+ rules, got {response.Rules.Length}");
        // Ids are unique and non-empty.
        Assert.All(response.Rules, r => Assert.False(string.IsNullOrWhiteSpace(r.RuleId)));
        Assert.Equal(response.Rules.Length, response.Rules.Select(r => r.RuleId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task ListAnalysisRules_applies_catalog_metadata_to_a_known_rule()
    {
        var registry = new RuleRegistry();
        var handler = CreateHandler(registry);

        var response = await handler.HandleAsync(new ListAnalysisRulesRequest(), CreateContext(), CancellationToken.None);

        var pe002 = Assert.Single(response.Rules, r => r.RuleId == "PE002");
        Assert.Equal("Unqualified object name", pe002.Name);
        Assert.Equal("Performance", pe002.Category);
        Assert.True(pe002.AutoFixable);                 // PE002 ships a concrete auto-fix
        Assert.False(string.IsNullOrWhiteSpace(pe002.Description));
    }

    [Fact]
    public async Task ListAnalysisRules_defaults_enable_every_rule_with_default_severity()
    {
        var registry = new RuleRegistry();
        var handler = CreateHandler(registry);

        var response = await handler.HandleAsync(new ListAnalysisRulesRequest(), CreateContext(), CancellationToken.None);

        // With a fresh AppSettings (no .casettings overrides) every rule is enabled and its
        // effective severity equals its built-in default.
        Assert.All(response.Rules, r =>
        {
            Assert.True(r.Enabled);
            Assert.Equal(r.DefaultSeverity, r.EffectiveSeverity);
        });
    }

    [Fact]
    public void Every_discovered_rule_has_a_catalog_entry()
    {
        // Guards against adding a rule without documenting it: every rule the registry discovers
        // must have an explicit RuleMetadataCatalog entry (else the dialog shows a bare id).
        var registry = new RuleRegistry();
        var uncatalogued = registry.AllRules
            .Where(r => !RuleMetadataCatalog.Contains(r.RuleId))
            .Select(r => r.RuleId)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(uncatalogued.Length == 0,
            "Rules missing a RuleMetadataCatalog entry: " + string.Join(", ", uncatalogued));
    }

    [Fact]
    public async Task ListAnalysisRules_roundtrips_through_router_as_message_133()
    {
        var registry = new RuleRegistry();
        var router = new RpcRouter();
        router.Register(CreateHandler(registry));
        var ctx = CreateContext();

        await using var transport = new InProcessTransport();
        transport.RequestReceived += (msg, ct) => router.RouteAsync(msg, ctx, ct);
        await transport.StartAsync(CancellationToken.None);

        var msg = new RpcMessage
        {
            MessageType = MessageTypes.ListAnalysisRules,
            RequestId = 1,
            Payload = MessagePackSerializer.Serialize(new ListAnalysisRulesRequest()),
        };

        var response = await transport.SendAsync(msg, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(MessageTypes.ListAnalysisRulesResult, response!.MessageType);
        var typed = MessagePackSerializer.Deserialize<ListAnalysisRulesResponse>(response.Payload!);
        Assert.True(typed.Success);
        Assert.Equal(registry.AllRules.Count, typed.Rules.Length);
    }
}
