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
    /// Spec 040 (HIS-12) — a refresh (a run in another tab, a draft, a closed tab) rebuilds the rows.
    /// It kept only the first selected one, so rows picked for Delete or Compare dropped to one.
    /// </summary>
    public class HistorySelectionRefreshTests
    {
        private static HistoryViewModel Model(FakeRpcClientAccessor rpc) =>
            new HistoryViewModel(rpc) { SettingsProvider = () => new AppSettings() };

        /// <summary>Answers every search with fresh rows (new objects, as the engine sends them).</summary>
        private static FakeRpcClientAccessor Server(System.Func<HistoryEntryDto[]> rows)
        {
            var rpc = new FakeRpcClientAccessor();
            rpc.Respond<HistorySearchRequest>(MessageTypes.HistorySearch, _ => new HistorySearchResponse
            {
                Success = true,
                TotalCount = rows().Length,
                Entries = rows(),
            });
            return rpc;
        }

        private static HistoryEntryDto Row(long id, string session) =>
            new HistoryEntryDto { Id = id, SessionKey = session, SqlText = "SELECT " + id };

        [Fact]
        public async Task Several_selected_rows_stay_selected_after_a_refresh()
        {
            var vm = Model(Server(() => new[] { Row(1, "a"), Row(2, "b"), Row(3, "c"), Row(4, "d") }));
            await vm.RunSearchAsync(resetOffset: true);
            vm.UpdateSelectedEntries(new List<HistoryEntryDto> { vm.Entries[1], vm.Entries[3] });
            IReadOnlyList<HistoryEntryDto>? restored = null;
            vm.SelectionRestored += rows => restored = rows;

            await vm.RefreshKeepingSelectionAsync();

            Assert.Equal(new long[] { 2, 4 }, vm.SelectedEntries.Select(e => e.Id).ToArray());
            Assert.Equal(2, vm.SelectedEntry!.Id);
            Assert.NotNull(restored);
            Assert.Equal(new long[] { 2, 4 }, restored!.Select(e => e.Id).ToArray());
            Assert.All(restored, e => Assert.Contains(e, vm.Entries));   // the new rows, not the old objects
        }

        [Fact]
        public async Task A_selected_row_replaced_by_a_new_run_is_kept_by_its_session()
        {
            var newest = 2L;
            var vm = Model(Server(() => new[] { Row(1, "a"), Row(newest, "b"), Row(3, "c") }));
            await vm.RunSearchAsync(resetOffset: true);
            vm.UpdateSelectedEntries(new List<HistoryEntryDto> { vm.Entries[0], vm.Entries[1] });

            newest = 9;   // query "b" ran again: its row is now entry 9
            await vm.RefreshKeepingSelectionAsync();

            Assert.Equal(new long[] { 1, 9 }, vm.SelectedEntries.Select(e => e.Id).ToArray());
        }

        [Fact]
        public async Task One_selected_row_is_kept_without_a_multi_restore()
        {
            var vm = Model(Server(() => new[] { Row(1, "a"), Row(2, "b") }));
            await vm.RunSearchAsync(resetOffset: true);
            vm.UpdateSelectedEntries(new List<HistoryEntryDto> { vm.Entries[1] });
            var raised = false;
            vm.SelectionRestored += _ => raised = true;

            await vm.RefreshKeepingSelectionAsync();

            Assert.Equal(2, vm.SelectedEntry!.Id);
            Assert.False(raised);
        }
    }
}
