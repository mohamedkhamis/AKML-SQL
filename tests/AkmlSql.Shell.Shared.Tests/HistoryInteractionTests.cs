#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.History;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T118, HIS-07/HIS-09) — SQL History searches as you type (250 ms after the last key),
    /// selects the first result, refreshes live without losing the selection, offers Advanced search
    /// with removable filter chips (remembered only when asked), acts on the row a menu was opened
    /// on, and recovers by itself when the engine comes back.
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class HistoryInteractionTests : AppDataIsolatedTest
    {
        public HistoryInteractionTests() : base("akmlsql-history-interaction-test-") { }

        /// <summary>A scheduler the test runs by hand: each Schedule call is recorded until run or cancelled.</summary>
        private sealed class ManualScheduler
        {
            public sealed class Item : IDisposable
            {
                public TimeSpan Delay;
                public Action Action = () => { };
                public bool Cancelled;
                public void Dispose() => Cancelled = true;
            }

            public List<Item> Items { get; } = new List<Item>();

            public IDisposable Schedule(TimeSpan delay, Action action)
            {
                var item = new Item { Delay = delay, Action = action };
                Items.Add(item);
                return item;
            }

            public IEnumerable<Item> Pending => Items.Where(i => !i.Cancelled);

            /// <summary>Runs every pending item (once each).</summary>
            public void RunPending()
            {
                foreach (var item in Pending.ToList())
                {
                    item.Cancelled = true;
                    item.Action();
                }
            }
        }

        private static HistoryEntryDto Entry(long id, string key) =>
            new HistoryEntryDto { Id = id, SqlText = "SELECT " + id, SessionKey = key, TabTitle = "q" + id, ExecutedAt = "2026-09-28T10:00:00.0000000Z" };

        private static (HistoryViewModel Vm, FakeRpcClientAccessor Fake, ManualScheduler Scheduler) NewVm(params HistoryEntryDto[] entries)
        {
            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.HistorySearch, new HistorySearchResponse { Success = true, Entries = entries, TotalCount = entries.Length });
            fake.Respond(MessageTypes.HistoryAction, new HistoryActionResponse { Success = true, Servers = new[] { "(local)", "srv-a" }, Databases = new[] { "Northwind" } });
            var scheduler = new ManualScheduler();
            var vm = new HistoryViewModel(fake)
            {
                Scheduler = scheduler.Schedule,
                Clock = () => new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Local),
                SettingsProvider = () => new AppSettings(),
            };
            return (vm, fake, scheduler);
        }

        private static List<HistorySearchRequest> Searches(FakeRpcClientAccessor fake) =>
            fake.Requests.Where(r => r.MessageType == MessageTypes.HistorySearch).Select(r => (HistorySearchRequest)r.Payload!).ToList();

        [Fact]
        public void Typing_searches_once_250_ms_after_the_last_change()
        {
            var (vm, fake, scheduler) = NewVm(Entry(1, "a"));

            vm.SearchText = "o";
            vm.SearchText = "or";
            vm.SearchText = "orders";

            var pending = Assert.Single(scheduler.Pending);
            Assert.Equal(TimeSpan.FromMilliseconds(250), pending.Delay);
            Assert.Empty(Searches(fake));

            scheduler.RunPending();

            Assert.Equal("orders", Assert.Single(Searches(fake)).SearchText);
        }

        [Fact]
        public async Task A_search_selects_the_first_row()
        {
            var (vm, _, _) = NewVm(Entry(7, "a"), Entry(8, "b"));

            await vm.RunSearchAsync(resetOffset: true);

            Assert.Equal(7, vm.SelectedEntry?.Id);
        }

        [Fact]
        public async Task A_live_refresh_keeps_the_selected_query()
        {
            var (vm, fake, _) = NewVm(Entry(7, "a"), Entry(8, "b"));
            await vm.RunSearchAsync(resetOffset: true);
            vm.SelectedEntry = vm.Entries.Single(e => e.Id == 8);

            // A new run of query "b" is its row now (a new id), and a new query sorts first.
            fake.Respond(MessageTypes.HistorySearch, new HistorySearchResponse
            {
                Success = true, TotalCount = 3,
                Entries = new[] { Entry(9, "c"), Entry(10, "b"), Entry(7, "a") },
            });
            await vm.HandleHistoryRecordedAsync();

            Assert.Equal("b", vm.SelectedEntry?.SessionKey);
            Assert.Equal(3, vm.Entries.Count);
        }

        [Theory]
        [InlineData("week", 7)]
        [InlineData("month", 31)]
        [InlineData("3months", 92)]
        public async Task Advanced_search_periods_become_date_ranges(string period, int maxDays)
        {
            var (vm, fake, _) = NewVm(Entry(1, "a"));

            vm.AdvancedSearch.Period = period;
            await vm.ApplyAdvancedSearchAsync();

            var request = Searches(fake).Last();
            var from = DateTime.Parse(request.DateFrom!, null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime();
            var days = (new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Local) - from).TotalDays;
            Assert.InRange(days, 6.9, maxDays + 0.01);
            Assert.Null(request.DateTo);
        }

        [Fact]
        public async Task A_custom_period_uses_whole_days()
        {
            var (vm, fake, _) = NewVm(Entry(1, "a"));

            vm.AdvancedSearch.Period = "custom";
            vm.AdvancedSearch.From = new DateTime(2026, 9, 1);
            vm.AdvancedSearch.To = new DateTime(2026, 9, 10);
            vm.AdvancedSearch.Server = "srv-a";
            vm.AdvancedSearch.Starred = true;
            vm.AdvancedSearch.OpenOnly = true;
            await vm.ApplyAdvancedSearchAsync();

            var request = Searches(fake).Last();
            Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Local).ToUniversalTime(),
                DateTime.Parse(request.DateFrom!, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime());
            Assert.Equal(new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Local).AddTicks(-1).ToUniversalTime(),
                DateTime.Parse(request.DateTo!, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime());
            Assert.Equal("srv-a", request.Server);
            Assert.True(request.FavoritesOnly);
            Assert.True(request.IsOpen);
        }

        [Fact]
        public async Task Advanced_search_is_saved_only_when_remember_is_on()
        {
            var (vm, _, _) = NewVm(Entry(1, "a"));
            vm.SettingsProvider = ConfigManager.Load;

            vm.AdvancedSearch.Server = "srv-a";
            await vm.ApplyAdvancedSearchAsync();
            Assert.Null(ConfigManager.Load().History.AdvancedSearch);

            var settings = ConfigManager.Load();
            settings.History.RememberAdvancedSearch = true;
            ConfigManager.Save(settings);
            await vm.ApplyAdvancedSearchAsync();
            Assert.Equal("srv-a", ConfigManager.Load().History.AdvancedSearch?.Server);

            // A new view model starts from the remembered filters.
            var again = new HistoryViewModel(new FakeRpcClientAccessor()) { SettingsProvider = ConfigManager.Load };
            again.LoadRememberedAdvancedSearch();
            Assert.Equal("srv-a", again.AdvancedSearch.Server);
        }

        [Fact]
        public async Task Removing_a_chip_clears_only_that_filter_and_searches_again()
        {
            var (vm, fake, _) = NewVm(Entry(1, "a"));
            vm.AdvancedSearch.Server = "srv-a";
            vm.AdvancedSearch.Database = "Northwind";
            await vm.ApplyAdvancedSearchAsync();
            Assert.Equal(2, vm.ActiveFilterChips.Count);

            var before = Searches(fake).Count;
            var serverChip = vm.ActiveFilterChips.Single(c => c.Label.Contains("srv-a"));
            await vm.RemoveChipAsync(serverChip);

            var request = Searches(fake).Last();
            Assert.Equal(before + 1, Searches(fake).Count);
            Assert.Null(request.Server);
            Assert.Equal("Northwind", request.Database);
            Assert.Single(vm.ActiveFilterChips);
        }

        [Fact]
        public async Task Row_menu_actions_act_on_the_row_they_were_opened_on()
        {
            var (vm, fake, _) = NewVm(Entry(7, "a"), Entry(8, "b"));
            fake.Respond<HistoryActionRequest>(MessageTypes.HistoryAction, r => new HistoryActionResponse { Success = true, FullSqlText = "FULL " + r.EntryIds?.FirstOrDefault() });
            await vm.RunSearchAsync(resetOffset: true);
            Assert.Equal(7, vm.SelectedEntry?.Id);

            string? opened = null;
            vm.OpenInNewTabRequested += (sql, _, _, _) => opened = sql;
            vm.OpenInNewTabCommand.Execute(vm.Entries.Single(e => e.Id == 8));
            await Task.Delay(50);

            Assert.Equal("FULL 8", opened);
        }

        [Fact]
        public async Task A_disconnected_engine_is_checked_every_5_seconds_until_it_returns()
        {
            var (vm, fake, scheduler) = NewVm(Entry(1, "a"));
            fake.IsConnected = false;

            await vm.RunSearchAsync(resetOffset: true);
            Assert.True(vm.IsDisconnected);
            var probe = Assert.Single(scheduler.Pending);
            Assert.Equal(TimeSpan.FromSeconds(5), probe.Delay);

            scheduler.RunPending(); // still down: checks again later
            Assert.Single(scheduler.Pending);
            Assert.Empty(Searches(fake));

            fake.IsConnected = true;
            scheduler.RunPending();
            await Task.Delay(50);

            Assert.False(vm.IsDisconnected);
            Assert.Empty(scheduler.Pending);
            Assert.Single(Searches(fake));
        }

        [Fact]
        public async Task Retry_searches_again_at_once()
        {
            var (vm, fake, scheduler) = NewVm(Entry(1, "a"));
            fake.IsConnected = false;
            await vm.RunSearchAsync(resetOffset: true);
            fake.IsConnected = true;

            vm.RetryCommand.Execute(null);
            await Task.Delay(50);

            Assert.False(vm.IsDisconnected);
            Assert.Single(Searches(fake));
            Assert.Empty(scheduler.Pending);
        }
    }
}
