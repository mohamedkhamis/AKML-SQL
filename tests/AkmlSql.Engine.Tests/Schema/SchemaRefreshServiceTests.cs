using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using Xunit;

namespace AkmlSql.Engine.Tests.Schema;

/// <summary>
/// Spec 042 (FR-043): a refresh that arrives while a load of the cache is running (the connection's own Phase A/B, a reload after
/// eviction) is not lost. It marks the cache stale and is carried out once that load has ended; however many arrive during one load,
/// the catalog is read again once; a cache with no load running is refreshed at once whatever phase it was left in; and a refresh
/// whose session has moved on leaves the cache it marked alone.
/// </summary>
public sealed class SchemaRefreshServiceTests
{
    private sealed class FakeProvider : ISchemaMetadataProvider
    {
        private int _phaseA;
        private int _phaseB;

        public int PhaseACalls => Volatile.Read(ref _phaseA);
        public int PhaseBCalls => Volatile.Read(ref _phaseB);
        public DatabaseKind Kind => DatabaseKind.PostgreSql;

        public Task PopulatePhaseAAsync(DatabaseCache cache, string connectionString, CancellationToken ct)
        {
            Interlocked.Increment(ref _phaseA);
            cache.Phase = PopulationPhase.PhaseA;
            return Task.CompletedTask;
        }

        public Task PopulatePhaseBAsync(DatabaseCache cache, string connectionString, CancellationToken ct)
        {
            Interlocked.Increment(ref _phaseB);
            cache.Phase = PopulationPhase.Complete;
            return Task.CompletedTask;
        }

        public Task<List<string>> ListDatabasesAsync(string connectionString, CancellationToken ct) => Task.FromResult(new List<string>());
    }

    private sealed record Rig(SchemaRefreshService Service, SessionManager Sessions, SchemaCacheManager Caches, DatabaseCache Cache, FakeProvider Provider)
    {
        /// <summary>Another load of the cache begins (the connection's own, say): it holds the population claim until it ends.</summary>
        public void LoadBegins() => Assert.True(Caches.TryClaimPopulation(Cache));

        public void LoadEnds() => Caches.ReleasePopulation(Cache);

        public void Refresh() => Service.Refresh(new RefreshRequest { SessionId = "s" });
    }

    private static Rig Build()
    {
        var sessions = new SessionManager();
        sessions.UpdateSession(new ConnectionInfo
        {
            SessionId = "s", ConnectionString = "Host=x;Database=d;Username=u;Password=p", DatabaseName = "d", Kind = (int)DatabaseKind.PostgreSql,
        });
        var caches = new SchemaCacheManager { KindOf = sessions.KindOf };
        var cache = caches.GetOrCreateCache("s", "d");
        var provider = new FakeProvider();
        var service = new SchemaRefreshService(sessions, caches, new SchemaMetadataService(), _ => provider)
        {
            SettlePollInterval = TimeSpan.FromMilliseconds(10),
            SettleTimeout = TimeSpan.FromSeconds(5),
        };
        return new Rig(service, sessions, caches, cache, provider);
    }

    private static async Task EventuallyAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(condition(), what);
    }

    [Fact]
    public async Task A_refresh_that_arrives_while_a_load_runs_is_carried_out_once_the_load_has_ended()
    {
        var rig = Build();
        rig.Cache.Phase = PopulationPhase.PhaseA;                  // Phase A done, Phase B in flight
        rig.LoadBegins();

        rig.Refresh();

        Assert.True(rig.Cache.IsStale);
        Assert.Equal(0, rig.Provider.PhaseACalls);                 // nothing is cleared under the running load

        rig.LoadEnds();
        await EventuallyAsync(() => rig.Provider.PhaseACalls == 1, "the deferred refresh did not run");
        await EventuallyAsync(() => rig.Provider.PhaseBCalls == 1 && rig.Cache.Phase == PopulationPhase.Complete, "the deferred refresh did not finish");
        await EventuallyAsync(() => !rig.Caches.IsPopulating(rig.Cache), "the refresh did not release its claim");
        Assert.False(rig.Cache.IsStale);
    }

    [Fact]
    public async Task Several_refreshes_during_one_load_read_the_catalog_again_once()
    {
        var rig = Build();
        rig.LoadBegins();

        rig.Refresh();
        rig.Refresh();
        rig.Refresh();
        rig.LoadEnds();

        await EventuallyAsync(() => rig.Provider.PhaseACalls >= 1, "no refresh ran");
        await Task.Delay(200);
        Assert.Equal(1, rig.Provider.PhaseACalls);
    }

    [Theory]
    [InlineData(PopulationPhase.Complete)]
    [InlineData(PopulationPhase.PhaseA)]          // where a failed Phase B leaves a cache: no load is running, so it is refreshed at once
    [InlineData(PopulationPhase.NotLoaded)]
    public async Task A_cache_with_no_load_running_is_refreshed_at_once_whatever_its_phase(PopulationPhase phase)
    {
        var rig = Build();
        rig.Cache.Phase = phase;

        rig.Refresh();

        await EventuallyAsync(() => rig.Provider.PhaseACalls == 1 && rig.Provider.PhaseBCalls == 1, "the refresh did not run");
        Assert.False(rig.Cache.IsStale);
        Assert.Equal(PopulationPhase.Complete, rig.Cache.Phase);
    }

    [Fact]
    public async Task A_load_that_never_ends_drops_the_deferred_refresh_and_a_later_refresh_still_works()
    {
        var rig = Build();
        rig.Service.SettleTimeout = TimeSpan.FromMilliseconds(100);
        rig.LoadBegins();

        rig.Refresh();
        await Task.Delay(400);
        Assert.Equal(0, rig.Provider.PhaseACalls);
        Assert.True(rig.Cache.IsStale);                            // still marked, for whoever looks next

        rig.LoadEnds();                                            // long after the deadline: the dropped refresh must not wake up
        await Task.Delay(100);
        Assert.Equal(0, rig.Provider.PhaseACalls);

        rig.Refresh();                                             // a new one is not blocked by the dropped one
        await EventuallyAsync(() => rig.Provider.PhaseACalls == 1, "a later refresh did not run");
    }

    [Fact]
    public async Task A_session_that_moved_to_another_database_while_waiting_leaves_the_marked_cache_alone()
    {
        var rig = Build();
        rig.LoadBegins();
        rig.Refresh();

        rig.Sessions.UpdateSession(new ConnectionInfo
        {
            SessionId = "s", ConnectionString = "Host=x;Database=e;Username=u;Password=p", DatabaseName = "e", Kind = (int)DatabaseKind.PostgreSql,
        });
        rig.LoadEnds();
        await Task.Delay(300);

        Assert.Equal(0, rig.Provider.PhaseACalls);                 // the new database's own load is its own business
        Assert.True(rig.Cache.IsStale);
    }

    [Fact]
    public async Task A_load_still_running_on_a_cache_removed_with_its_session_does_not_hold_up_the_new_caches_refresh()
    {
        // Spec 043 follow-up (review, round 4): claims and deferred refreshes were keyed by "{session}:{db}". After RemoveSession
        // (the tab moved to another server) the old cache's load still held the key, so a refresh of the new cache under the same
        // key was deferred behind a load that was not its own, and swallowed by the waiter still polling the old cache.
        var rig = Build();
        rig.LoadBegins();                                          // the old server's load
        rig.Refresh();                                             // deferred behind it
        Assert.True(rig.Cache.IsStale);

        rig.Caches.RemoveSession("s");
        var fresh = rig.Caches.GetOrCreateCache("s", "d");
        Assert.NotSame(rig.Cache, fresh);

        rig.Refresh();                                             // the new cache's: no load of it is running
        await EventuallyAsync(() => rig.Provider.PhaseACalls == 1 && fresh.Phase == PopulationPhase.Complete, "the new cache was not refreshed");
        await EventuallyAsync(() => !rig.Caches.IsPopulating(fresh), "the refresh did not release its claim");
        Assert.True(rig.Caches.IsPopulating(rig.Cache));           // the old load's claim is still its own

        rig.LoadEnds();                                            // the old load ends; its waiter finds the session on another cache
        await Task.Delay(200);
        Assert.Equal(1, rig.Provider.PhaseACalls);
        Assert.False(rig.Caches.IsPopulating(fresh));
    }
}
