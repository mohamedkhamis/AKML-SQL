#nullable enable
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
    /// Spec 040 (T055, HIS-03, FR-013) — scrolling reaches every matching entry. The old rule
    /// added the offset to an already-accumulated list, so "more" turned false after two pages.
    /// </summary>
    public class HistoryPagingTests
    {
        private static FakeRpcClientAccessor ServerWith(int total)
        {
            var rpc = new FakeRpcClientAccessor();
            rpc.Respond<HistorySearchRequest>(MessageTypes.HistorySearch, req => new HistorySearchResponse
            {
                Success = true,
                TotalCount = total,
                Entries = Enumerable.Range(req.Offset, System.Math.Max(0, System.Math.Min(req.Limit, total - req.Offset)))
                    .Select(i => new HistoryEntryDto { Id = i + 1, SqlText = "SELECT " + i })
                    .ToArray(),
            });
            return rpc;
        }

        private static HistoryViewModel Model(FakeRpcClientAccessor rpc) =>
            new HistoryViewModel(rpc) { SettingsProvider = () => new AppSettings() };

        [Fact]
        public async Task More_stays_true_until_every_match_is_loaded()
        {
            var vm = Model(ServerWith(250));

            await vm.RunSearchAsync(resetOffset: true);
            Assert.Equal(100, vm.Entries.Count);
            Assert.True(vm.HasMoreEntries);

            await vm.RunSearchAsync(resetOffset: false);
            Assert.Equal(200, vm.Entries.Count);
            Assert.True(vm.HasMoreEntries);

            await vm.RunSearchAsync(resetOffset: false);
            Assert.Equal(250, vm.Entries.Count);
            Assert.False(vm.HasMoreEntries);
        }

        [Fact]
        public async Task A_short_page_ends_paging()
        {
            var vm = Model(ServerWith(40));

            await vm.RunSearchAsync(resetOffset: true);

            Assert.Equal(40, vm.Entries.Count);
            Assert.False(vm.HasMoreEntries);
        }
    }
}
