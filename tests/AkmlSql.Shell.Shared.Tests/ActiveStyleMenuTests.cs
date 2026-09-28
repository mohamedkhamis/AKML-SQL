#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Formatting;
using AkmlSql.Shell.Shared.StatusBar;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T089, STY-08, FR-033) — the Active Style menu: slot N shows style N, checked when
    /// active, hidden when there is no style N; at most 30 slots; choosing one makes it active,
    /// updates the list at once and refreshes it from the engine.
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class ActiveStyleMenuTests : AppDataIsolatedTest
    {
        public ActiveStyleMenuTests() : base("akmlsql-activestylemenu-test-") { }

        private static (ActiveStyleCache Cache, FakeRpcClientAccessor Fake) Cache(int count, string active)
        {
            var settings = ConfigManager.Load();
            settings.Formatter.ActiveProfile = active;
            ConfigManager.Save(settings);

            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.ProfileList, new ProfileListResponse
            {
                Profiles = Enumerable.Range(1, count).Select(i => new ProfileInfo { Name = $"Style {i:00}" }).ToArray(),
            });
            var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
            return (new ActiveStyleCache(fake, () => now), fake);
        }

        [Fact]
        public async Task Each_slot_shows_its_style_checked_when_active()
        {
            var (cache, _) = Cache(3, "Style 02");
            await cache.RefreshAsync();

            var slots = Enumerable.Range(0, ActiveStyleMenuCommands.SlotCount)
                .Select(i => ActiveStyleMenuCommands.SlotState(cache.Styles, i)).ToList();

            Assert.Equal(("Style 01", false, true), slots[0]);
            Assert.Equal(("Style 02", true, true), slots[1]);
            Assert.Equal(("Style 03", false, true), slots[2]);
            Assert.All(slots.Skip(3), s => Assert.False(s.Visible));
        }

        [Fact]
        public async Task More_than_30_styles_show_only_the_first_30()
        {
            var (cache, _) = Cache(34, "Style 01");
            await cache.RefreshAsync();

            Assert.Equal(30, ActiveStyleMenuCommands.SlotCount);
            var last = ActiveStyleMenuCommands.SlotState(cache.Styles, ActiveStyleMenuCommands.SlotCount - 1);
            Assert.Equal("Style 30", last.Text);
            Assert.True(last.Visible);
            Assert.False(ActiveStyleMenuCommands.SlotState(cache.Styles, ActiveStyleMenuCommands.SlotCount).Visible);
        }

        [Fact]
        public async Task Choosing_a_slot_makes_its_style_active_and_refreshes()
        {
            var (cache, fake) = Cache(3, "Style 01");
            await cache.RefreshAsync();
            var statusTexts = new List<string>();
            StatusBarManager.TextSinkOverride = statusTexts.Add;
            StatusBarManager.ResetForTests();
            try
            {
                Assert.True(ActiveStyleMenuCommands.ActivateSlot(2, cache));
                await cache.WhenIdleAsync();

                Assert.Equal("Style 03", ConfigManager.Load().Formatter.ActiveProfile);
                Assert.Equal("Style 03", cache.Styles.Single(s => s.IsActive).Name);   // at once
                Assert.Equal(2, fake.Requests.Count(r => r.MessageType == MessageTypes.ProfileList));
                Assert.Contains(statusTexts, t => t.Contains("Style 03"));
            }
            finally
            {
                StatusBarManager.TextSinkOverride = null;
                StatusBarManager.ResetForTests();
            }
        }

        [Fact]
        public async Task Choosing_an_empty_slot_changes_nothing()
        {
            var (cache, _) = Cache(2, "Style 01");
            await cache.RefreshAsync();

            Assert.False(ActiveStyleMenuCommands.ActivateSlot(5, cache));
            Assert.Equal("Style 01", ConfigManager.Load().Formatter.ActiveProfile);
        }
    }
}
