#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Tabs;
using AkmlSql.Shell.Shared.Commands;
using AkmlSql.Shell.Shared.Tabs;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T121, HIS-14) — Ctrl+Shift+T reopens the newest tab closed in this session; with
    /// none, the newest closed query in SQL History (so it also works after SSMS restarts).
    /// </summary>
    public sealed class RestoreClosedTabFallbackTests
    {
        private sealed class Run
        {
            public ClosedTabRestorer Restorer = null!;
            public FakeRpcClientAccessor Fake = null!;
            public ClosedTabEntry? OpenedTab;
            public HistoryEntryDto? OpenedFromHistory;
            public string? Notified;
        }

        private static Run Build(ClosedTabStack stack, params HistoryEntryDto[] closed)
        {
            var run = new Run { Fake = new FakeRpcClientAccessor() };
            run.Fake.Respond(MessageTypes.HistorySearch, new HistorySearchResponse { Success = true, Entries = closed, TotalCount = closed.Length });
            run.Fake.Respond<HistoryActionRequest>(MessageTypes.HistoryAction,
                r => new HistoryActionResponse { Success = true, FullSqlText = "FULL " + r.EntryIds.First() });
            run.Restorer = new ClosedTabRestorer(run.Fake, stack)
            {
                OpenClosedTab = e => run.OpenedTab = e,
                OpenFromHistory = e => run.OpenedFromHistory = e,
                Notify = text => run.Notified = text,
            };
            return run;
        }

        [Fact]
        public async Task A_tab_closed_in_this_session_comes_first_and_history_is_not_asked()
        {
            var stack = new ClosedTabStack();
            stack.Push(new ClosedTabEntry("SELECT 1", null, null, null, null, DateTime.UtcNow, "SQLQuery1.sql"));
            var run = Build(stack, new HistoryEntryDto { Id = 9, TabTitle = "q9" });

            Assert.True(await run.Restorer.RestoreAsync());

            Assert.Equal("SELECT 1", run.OpenedTab?.Content);
            Assert.Null(run.OpenedFromHistory);
            Assert.Empty(run.Fake.Requests);
        }

        [Fact]
        public async Task With_no_closed_tab_the_newest_closed_query_in_history_reopens()
        {
            var run = Build(new ClosedTabStack(),
                new HistoryEntryDto { Id = 9, TabTitle = "Monthly totals", SqlText = "SELECT …", Server = "(local)", SessionKey = "k9" });

            Assert.True(await run.Restorer.RestoreAsync());

            var search = (HistorySearchRequest)run.Fake.Requests.First(r => r.MessageType == MessageTypes.HistorySearch).Payload!;
            Assert.False(search.IsOpen);
            Assert.Equal(1, search.Limit);
            Assert.Equal(0, search.Offset);
            Assert.Equal("FULL 9", run.OpenedFromHistory?.SqlText);
            Assert.Equal("k9", run.OpenedFromHistory?.SessionKey);
            Assert.Equal("Restored 'Monthly totals' from SQL History.", run.Notified);
        }

        [Fact]
        public async Task With_nothing_closed_anywhere_nothing_opens()
        {
            var run = Build(new ClosedTabStack());

            Assert.False(await run.Restorer.RestoreAsync());

            Assert.Null(run.OpenedFromHistory);
            Assert.NotNull(run.Notified);
        }

        [Fact]
        public async Task Without_the_engine_only_the_session_stack_is_used()
        {
            var run = Build(new ClosedTabStack());
            run.Fake.IsConnected = false;

            Assert.False(run.Restorer.CanRestore);
            Assert.False(await run.Restorer.RestoreAsync());
            Assert.Empty(run.Fake.Requests);
        }
    }
}
