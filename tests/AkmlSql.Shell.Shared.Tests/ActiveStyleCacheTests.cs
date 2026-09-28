#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Formatting;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T093, STY-08) — the style list behind the Active Style menu: the active mark comes
    /// from <c>Formatter.ActiveProfile</c>, refreshes are throttled to one per 5 s, and a failed
    /// refresh keeps the last list.
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class ActiveStyleCacheTests : AppDataIsolatedTest
    {
        public ActiveStyleCacheTests() : base("akmlsql-activestylecache-test-") { }

        private DateTime _now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

        private static void SetActive(string name)
        {
            var settings = ConfigManager.Load();
            settings.Formatter.ActiveProfile = name;
            ConfigManager.Save(settings);
        }

        private static FakeRpcClientAccessor Engine(params (string Name, bool BuiltIn)[] styles)
        {
            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.ProfileList, new ProfileListResponse
            {
                Profiles = styles.Select(s => new ProfileInfo { Name = s.Name, IsBuiltIn = s.BuiltIn }).ToArray(),
            });
            return fake;
        }

        private static int Refreshes(FakeRpcClientAccessor fake) =>
            fake.Requests.Count(r => r.MessageType == MessageTypes.ProfileList);

        [Fact]
        public async Task Refresh_marks_the_configured_style_active_and_lists_yours_first()
        {
            SetActive("khamis style");
            var cache = new ActiveStyleCache(Engine(("Default", true), ("Khamis Style", true), ("Mine", false)), () => _now);

            await cache.RefreshAsync();

            Assert.Equal(new[] { "Mine", "Default", "Khamis Style" }, cache.Styles.Select(s => s.Name));
            Assert.Equal("Khamis Style", cache.Styles.Single(s => s.IsActive).Name);
            Assert.Equal("Yours", cache.Styles[0].Source);
            Assert.Equal("Built-in", cache.Styles[1].Source);
        }

        [Fact]
        public async Task Refresh_requests_are_throttled_to_one_per_five_seconds()
        {
            SetActive("Default");
            var fake = Engine(("Default", true));
            var cache = new ActiveStyleCache(fake, () => _now);

            cache.RequestRefresh();
            cache.RequestRefresh();
            _now = _now.AddSeconds(4.9);
            cache.RequestRefresh();
            await cache.WhenIdleAsync();
            Assert.Equal(1, Refreshes(fake));

            _now = _now.AddSeconds(0.2);
            cache.RequestRefresh();
            await cache.WhenIdleAsync();
            Assert.Equal(2, Refreshes(fake));
        }

        [Fact]
        public async Task Changed_fires_once_per_completed_refresh()
        {
            SetActive("Default");
            var cache = new ActiveStyleCache(Engine(("Default", true)), () => _now);
            var changed = 0;
            cache.Changed += (_, _) => changed++;

            await cache.RefreshAsync();
            await cache.RefreshAsync();

            Assert.Equal(2, changed);
        }

        [Fact]
        public async Task A_failed_refresh_keeps_the_last_list_and_stays_quiet()
        {
            SetActive("Default");
            var fake = Engine(("Default", true), ("Mine", false));
            var cache = new ActiveStyleCache(fake, () => _now);
            await cache.RefreshAsync();
            var changed = 0;
            cache.Changed += (_, _) => changed++;

            fake.Throw(MessageTypes.ProfileList, new TimeoutException("engine busy"));
            await cache.RefreshAsync();

            Assert.Equal(new[] { "Mine", "Default" }, cache.Styles.Select(s => s.Name));
            Assert.Equal(0, changed);
        }

        [Fact]
        public async Task A_disconnected_engine_keeps_the_last_list()
        {
            SetActive("Default");
            var fake = Engine(("Default", true));
            var cache = new ActiveStyleCache(fake, () => _now);
            await cache.RefreshAsync();

            fake.IsConnected = false;
            await cache.RefreshAsync();

            Assert.Single(cache.Styles);
            Assert.Equal(1, Refreshes(fake));
        }
    }
}
