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
    /// Spec 040 (T119, HIS-14) — restore on start: "always" reopens up to the maximum (reconnecting
    /// when set), "prompt" asks and reopens only the chosen queries, "never" and a switched-off
    /// session recovery reopen nothing.
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class HistoryRestoreServiceTests : AppDataIsolatedTest
    {
        public HistoryRestoreServiceTests() : base("akmlsql-history-restore-test-") { }

        private sealed class Run
        {
            public HistoryRestoreService Service = null!;
            public FakeRpcClientAccessor Fake = null!;
            public List<(HistoryEntryDto Entry, bool Reconnect)> Opened = new List<(HistoryEntryDto, bool)>();
            public IReadOnlyList<HistoryEntryDto>? Prompted;
        }

        private static Run Build(Action<AppSettings> configure, Func<IReadOnlyList<HistoryEntryDto>, IReadOnlyList<HistoryEntryDto>>? prompt = null)
        {
            var run = new Run { Fake = new FakeRpcClientAccessor() };
            run.Fake.Respond<HistoryActionRequest>(MessageTypes.HistoryAction, r => new HistoryActionResponse
            {
                Success = true,
                Entries = r.EntryIds.Select(id => new HistoryEntryDto
                {
                    Id = id, SqlText = "SELECT " + id, TabTitle = "q" + id, Server = "(local)", Database = "Northwind", SessionKey = "k" + id,
                }).ToArray(),
            });
            var settings = new AppSettings();
            configure(settings);
            run.Service = new HistoryRestoreService(run.Fake)
            {
                SettingsProvider = () => settings,
                Opener = (entry, reconnect) => run.Opened.Add((entry, reconnect)),
                Prompt = entries =>
                {
                    run.Prompted = entries;
                    return prompt?.Invoke(entries) ?? Array.Empty<HistoryEntryDto>();
                },
            };
            return run;
        }

        private static readonly long[] Ids = { 5, 4, 3, 2, 1 };

        [Fact]
        public async Task Always_reopens_up_to_the_maximum_with_the_reconnect_flag()
        {
            var run = Build(s =>
            {
                s.Tabs.RestoreOnStartup = "always";
                s.History.RestoreMaxQueries = 3;
                s.History.ReconnectRestoredQueries = false;
            });

            Assert.Equal(3, await run.Service.RestoreAsync(Ids));

            Assert.Equal(new long[] { 5, 4, 3 }, run.Opened.Select(o => o.Entry.Id).ToArray());
            Assert.All(run.Opened, o => Assert.False(o.Reconnect));
            Assert.Null(run.Prompted);
            var request = (HistoryActionRequest)run.Fake.Requests.Single().Payload!;
            Assert.Equal(HistoryActions.GetEntries, request.Action);
        }

        [Fact]
        public async Task Prompt_asks_and_reopens_only_the_chosen_queries()
        {
            var run = Build(s => s.Tabs.RestoreOnStartup = "prompt", entries => entries.Where(e => e.Id % 2 == 1).ToList());

            Assert.Equal(3, await run.Service.RestoreAsync(Ids));

            Assert.Equal(5, run.Prompted?.Count);
            Assert.Equal(new long[] { 5, 3, 1 }, run.Opened.Select(o => o.Entry.Id).ToArray());
            Assert.All(run.Opened, o => Assert.True(o.Reconnect)); // on by default
        }

        [Fact]
        public async Task Not_now_reopens_nothing()
        {
            var run = Build(s => s.Tabs.RestoreOnStartup = "prompt");
            Assert.Equal(0, await run.Service.RestoreAsync(Ids));
            Assert.Empty(run.Opened);
        }

        [Fact]
        public async Task Never_reopens_nothing_and_asks_nothing()
        {
            var run = Build(s => s.Tabs.RestoreOnStartup = "never");

            Assert.Equal(0, await run.Service.RestoreAsync(Ids));

            Assert.Empty(run.Opened);
            Assert.Null(run.Prompted);
            Assert.Empty(run.Fake.Requests);
        }

        [Fact]
        public async Task Switched_off_session_recovery_disables_restore()
        {
            var run = Build(s =>
            {
                s.Tabs.SessionRecovery = false;
                s.Tabs.RestoreOnStartup = "always";
            });

            Assert.Equal(0, await run.Service.RestoreAsync(Ids));

            Assert.Empty(run.Opened);
            Assert.Empty(run.Fake.Requests);
        }

        [Fact]
        public async Task Nothing_to_restore_asks_nothing()
        {
            var run = Build(s => s.Tabs.RestoreOnStartup = "prompt");
            Assert.Equal(0, await run.Service.RestoreAsync(Array.Empty<long>()));
            Assert.Null(run.Prompted);
        }

        [Theory]
        [InlineData("always", "Always")]
        [InlineData("Never", "Never")]
        [InlineData("prompt", "Prompt")]
        [InlineData(null, "Prompt")]
        [InlineData("bogus", "Prompt")]
        public void Restore_mode_reads_the_setting(string? value, string expected)
            => Assert.Equal(expected, HistoryRestoreService.ModeOf(value).ToString());

        [StaFact]
        public void The_dialog_lists_every_query_checked()
        {
            var entries = new[]
            {
                new HistoryEntryDto { Id = 1, TabTitle = "q1", Server = "(local)", Database = "Northwind", ExecutedAt = "2026-09-28T10:00:00.0000000Z" },
                new HistoryEntryDto { Id = 2, TabTitle = "q2" },
            };
            var dialog = new RestoreQueriesDialog(entries);

            Assert.Equal("AKML SQL – Restore queries", dialog.Title);
            Assert.Equal(2, dialog.Boxes.Count);
            Assert.All(dialog.Boxes, b => Assert.True(b.IsChecked));

            dialog.Boxes[0].IsChecked = false;
            Assert.Equal(new long[] { 2 }, dialog.Checked.Select(e => e.Id).ToArray());
            Assert.True(dialog.RestoreButton.IsEnabled);

            dialog.Boxes[1].IsChecked = false;
            Assert.False(dialog.RestoreButton.IsEnabled);
            Assert.StartsWith("(local) · Northwind · ", RestoreQueriesDialog.DetailFor(entries[0]));
            dialog.Close();
        }
    }
}
