using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Handlers.Analysis;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis;

/// <summary>
/// The session scope: a rule switched off for as long as the IDE is open, without a directive in
/// the script or an entry in config.json. Covers the store, the way
/// <see cref="AnalysisEngine"/> applies it, and the IPC handler behind the quick fix.
/// </summary>
public sealed class SessionSuppressionTests
{
    // PE002 fires on unqualified table references and needs no schema cache.
    private const string SqlWithPe002 = "SELECT Id FROM Orders";
    private const string TargetRule = "PE002";

    // -- the store ------------------------------------------------------------

    [Fact]
    public void Store_StartsEmpty()
    {
        var store = new SessionSuppressionStore();

        Assert.Equal(0, store.Count);
        Assert.Empty(store.Snapshot());
        Assert.False(store.IsSuppressed("PE001"));
    }

    [Fact]
    public void Store_AddAndRemoveRoundTrip()
    {
        var store = new SessionSuppressionStore();

        store.Add("PE001");
        Assert.True(store.IsSuppressed("PE001"));

        store.Remove("PE001");
        Assert.False(store.IsSuppressed("PE001"));
    }

    [Fact]
    public void Store_IsCaseInsensitiveAndTrims()
    {
        var store = new SessionSuppressionStore();

        store.Add("  pe001 ");

        Assert.True(store.IsSuppressed("PE001"));
        Assert.Equal(["PE001"], store.Snapshot());
    }

    [Fact]
    public void Store_IgnoresBlankRuleIds()
    {
        var store = new SessionSuppressionStore();

        store.Add(null);
        store.Add("");
        store.Add("   ");

        Assert.Equal(0, store.Count);
        Assert.False(store.IsSuppressed(null));
    }

    [Fact]
    public void Store_AddIsIdempotent()
    {
        var store = new SessionSuppressionStore();

        store.Add("PE001");
        store.Add("PE001");

        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Store_SnapshotIsSortedAndOwnedByTheCaller()
    {
        var store = new SessionSuppressionStore();
        store.Add("SE002");
        store.Add("BP004");
        store.Add("PE001");

        var snapshot = store.Snapshot();
        Assert.Equal(["BP004", "PE001", "SE002"], snapshot);

        // Mutating the snapshot must not reach back into the store.
        snapshot[0] = "MUTATED";
        Assert.True(store.IsSuppressed("BP004"));
    }

    [Fact]
    public void Store_ClearRemovesEverything()
    {
        var store = new SessionSuppressionStore();
        store.Add("PE001");
        store.Add("BP004");

        store.Clear();

        Assert.Equal(0, store.Count);
    }

    // -- how the engine applies it -------------------------------------------

    [Fact]
    public async Task SessionSuppressedRule_IsAbsentFromResults()
    {
        var store = new SessionSuppressionStore();
        var engine = NewEngine(store, out var loader);
        using (loader)
        {
            var request = NewRequest();
            var settings = new CodeAnalysisSettings { Enabled = true };

            var before = await engine.AnalyzeAsync(request, 160, null, settings, CancellationToken.None);
            Assert.Contains(before.Issues, i => i.RuleId == TargetRule);

            store.Add(TargetRule);

            var after = await engine.AnalyzeAsync(request, 160, null, settings, CancellationToken.None);
            Assert.DoesNotContain(after.Issues, i => i.RuleId == TargetRule);
        }
    }

    [Fact]
    public async Task LiftingASessionSuppression_BringsTheFindingBack_WithoutClearingTheBatchCache()
    {
        // The engine filters session suppressions AFTER the batch cache, so an un-suppress takes
        // effect on the very next pass even though the cached batch still holds the diagnostic.
        var store = new SessionSuppressionStore();
        var engine = NewEngine(store, out var loader);
        using (loader)
        {
            var request = NewRequest();
            var settings = new CodeAnalysisSettings { Enabled = true };

            store.Add(TargetRule);
            var suppressed = await engine.AnalyzeAsync(request, 160, null, settings, CancellationToken.None);
            Assert.DoesNotContain(suppressed.Issues, i => i.RuleId == TargetRule);

            store.Remove(TargetRule);
            var restored = await engine.AnalyzeAsync(request, 160, null, settings, CancellationToken.None);
            Assert.Contains(restored.Issues, i => i.RuleId == TargetRule);
        }
    }

    [Fact]
    public async Task SessionSuppression_LeavesOtherRulesReporting()
    {
        var store = new SessionSuppressionStore();
        var engine = NewEngine(store, out var loader);
        using (loader)
        {
            // Two different findings in one script.
            var request = NewRequest("SELECT Id FROM Orders\nGO\nDELETE FROM dbo.Orders");
            var settings = new CodeAnalysisSettings { Enabled = true };

            var before = await engine.AnalyzeAsync(request, 160, null, settings, CancellationToken.None);
            Assert.Contains(before.Issues, i => i.RuleId == TargetRule);
            Assert.Contains(before.Issues, i => i.RuleId == "PE003");

            store.Add(TargetRule);

            var after = await engine.AnalyzeAsync(request, 160, null, settings, CancellationToken.None);
            Assert.DoesNotContain(after.Issues, i => i.RuleId == TargetRule);
            Assert.Contains(after.Issues, i => i.RuleId == "PE003");
        }
    }

    [Fact]
    public async Task AnEngineWithNoStore_BehavesExactlyAsBefore()
    {
        // The CLI analyzer and the web edition construct AnalysisEngine without a store.
        var parser = new TsqlParserService();
        var registry = new RuleRegistry();
        using var loader = new CaSettingsLoader();
        var engine = new AnalysisEngine(parser, registry, loader);

        var result = await engine.AnalyzeAsync(
            NewRequest(), 160, null, new CodeAnalysisSettings { Enabled = true }, CancellationToken.None);

        Assert.Contains(result.Issues, i => i.RuleId == TargetRule);
    }

    // -- the IPC handler ------------------------------------------------------

    [Fact]
    public async Task Handler_AddRemoveClearAndList()
    {
        var store = new SessionSuppressionStore();
        var handler = new SessionSuppressionHandler(store);
        var ctx = NewContext();

        var added = await handler.HandleAsync(
            new SessionSuppressionRequest { RuleId = "PE001", Action = SessionSuppressionActions.Add },
            ctx, CancellationToken.None);
        Assert.True(added.Success);
        Assert.Equal(["PE001"], added.SuppressedRules);

        await handler.HandleAsync(
            new SessionSuppressionRequest { RuleId = "BP004", Action = SessionSuppressionActions.Add },
            ctx, CancellationToken.None);

        var listed = await handler.HandleAsync(
            new SessionSuppressionRequest { Action = SessionSuppressionActions.List },
            ctx, CancellationToken.None);
        Assert.Equal(["BP004", "PE001"], listed.SuppressedRules);

        var removed = await handler.HandleAsync(
            new SessionSuppressionRequest { RuleId = "PE001", Action = SessionSuppressionActions.Remove },
            ctx, CancellationToken.None);
        Assert.Equal(["BP004"], removed.SuppressedRules);

        var cleared = await handler.HandleAsync(
            new SessionSuppressionRequest { Action = SessionSuppressionActions.Clear },
            ctx, CancellationToken.None);
        Assert.True(cleared.Success);
        Assert.Empty(cleared.SuppressedRules);
    }

    [Fact]
    public async Task Handler_RejectsAnUnknownActionWithoutChangingAnything()
    {
        var store = new SessionSuppressionStore();
        store.Add("PE001");
        var handler = new SessionSuppressionHandler(store);

        var response = await handler.HandleAsync(
            new SessionSuppressionRequest { RuleId = "BP004", Action = 99 },
            NewContext(), CancellationToken.None);

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        Assert.Equal(["PE001"], response.SuppressedRules);
    }

    [Fact]
    public async Task Handler_TreatsAnEmptyPayloadAsAList()
    {
        // AllowsEmptyPayload is true, so a null-ish request must not throw.
        var store = new SessionSuppressionStore();
        store.Add("PE001");

        var response = await new SessionSuppressionHandler(store).HandleAsync(
            new SessionSuppressionRequest(), NewContext(), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(["PE001"], response.SuppressedRules);
    }

    // -- helpers --------------------------------------------------------------

    private static AnalysisEngine NewEngine(SessionSuppressionStore store, out CaSettingsLoader loader)
    {
        loader = new CaSettingsLoader();
        return new AnalysisEngine(new TsqlParserService(), new RuleRegistry(), loader, store);
    }

    private static CodeAnalysisRequest NewRequest(string sql = SqlWithPe002) => new()
    {
        SessionId = "session-suppression-test",
        RequestId = "r1",
        DocumentText = sql,
    };

    private static RpcContext NewContext() => new()
    {
        Sessions = new SessionManager(),
        SchemaCache = new SchemaCacheManager(),
        Logger = Log.Logger,
        SettingsLoader = () => new AppSettings(),
    };
}
