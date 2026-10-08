#nullable enable
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
    /// Spec 040 (T057, HIS-01/HIS-04, FR-010/FR-014) — the preview gets the full text (fetched once
    /// per entry, without the list's loading state), and a row's Delete / Star act on that row's
    /// whole grouped query, not on whatever the selection holds.
    /// </summary>
    public class HistoryPreviewAndActionsTests
    {
        private readonly FakeRpcClientAccessor _rpc = new FakeRpcClientAccessor();
        private readonly List<HistoryActionRequest> _actions = new List<HistoryActionRequest>();
        private readonly List<string> _prompts = new List<string>();

        public HistoryPreviewAndActionsTests()
        {
            _rpc.Respond<HistorySearchRequest>(MessageTypes.HistorySearch, _ => new HistorySearchResponse
            {
                Success = true,
                TotalCount = 0,
                Entries = new HistoryEntryDto[0],
            });
            _rpc.Respond<HistoryActionRequest>(MessageTypes.HistoryAction, req =>
            {
                _actions.Add(req);
                return new HistoryActionResponse
                {
                    Success = true,
                    FullSqlText = "SELECT full text of " + req.EntryIds.FirstOrDefault(),
                    IsFavorite = true,
                    DeletedCount = 3,
                };
            });
        }

        private HistoryViewModel Model(bool grouping = true)
        {
            var settings = new AppSettings();
            settings.History.Deduplication = grouping;
            return new HistoryViewModel(_rpc)
            {
                SettingsProvider = () => settings,
                ConfirmPrompt = text => { _prompts.Add(text); return true; },
            };
        }

        [Fact]
        public async Task Preview_fetches_the_full_text_once_per_entry_without_the_loading_state()
        {
            var vm = Model();
            var entry = new HistoryEntryDto { Id = 7, SqlText = "SELECT full text of…" };
            var loadingSeen = false;
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(HistoryViewModel.IsLoading)) loadingSeen = true; };

            Assert.Equal("SELECT full text of 7", await vm.GetPreviewTextAsync(entry));
            Assert.Equal("SELECT full text of 7", await vm.GetPreviewTextAsync(entry));

            Assert.Single(_actions, a => a.Action == HistoryActions.GetFullSql);
            Assert.False(loadingSeen);
        }

        [Fact]
        public async Task Refresh_clears_the_preview_cache()
        {
            var vm = Model();
            var entry = new HistoryEntryDto { Id = 7 };

            await vm.GetPreviewTextAsync(entry);
            await vm.RunSearchAsync(resetOffset: true);
            await vm.GetPreviewTextAsync(entry);

            Assert.Equal(2, _actions.Count(a => a.Action == HistoryActions.GetFullSql));
        }

        [Fact]
        public async Task Row_delete_acts_on_that_rows_group_not_the_selection()
        {
            var vm = Model();
            vm.SelectedEntries.Add(new HistoryEntryDto { Id = 1 });
            vm.SelectedEntries.Add(new HistoryEntryDto { Id = 2 });
            var row = new HistoryEntryDto { Id = 9, TabTitle = "query-04" };

            await vm.DeleteEntryAsync(row);

            var delete = Assert.Single(_actions, a => a.Action == HistoryActions.Delete);
            Assert.Equal(new long[] { 9 }, delete.EntryIds);
            Assert.True(delete.GroupScope);
            Assert.Equal(new[] { "Remove 'query-04' and its history?" }, _prompts);
        }

        [Fact]
        public async Task Row_star_acts_on_that_rows_group_and_applies_the_returned_state()
        {
            var vm = Model();
            vm.SelectedEntries.Add(new HistoryEntryDto { Id = 1 });
            var row = new HistoryEntryDto { Id = 9, IsFavorite = false };

            await vm.ToggleFavoriteEntryAsync(row);

            var toggle = Assert.Single(_actions, a => a.Action == HistoryActions.ToggleFavorite);
            Assert.Equal(new long[] { 9 }, toggle.EntryIds);
            Assert.True(toggle.GroupScope);
            Assert.True(row.IsFavorite);
        }

        [Fact]
        public async Task Without_grouping_row_actions_are_per_entry()
        {
            var vm = Model(grouping: false);

            await vm.ToggleFavoriteEntryAsync(new HistoryEntryDto { Id = 9 });

            Assert.Null(Assert.Single(_actions).GroupScope);
        }

        [Fact]
        public async Task Declining_the_confirmation_deletes_nothing()
        {
            var vm = Model();
            vm.ConfirmPrompt = _ => false;

            await vm.DeleteEntryAsync(new HistoryEntryDto { Id = 9 });

            Assert.DoesNotContain(_actions, a => a.Action == HistoryActions.Delete);
        }
    }
}
