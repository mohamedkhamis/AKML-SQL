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
    /// Spec 040 (T122, HIS-10/HIS-12) — Open, Copy SQL and Re-execute use the version picked in the
    /// versions pane (else the query's full text); "Compare with current" compares a version with the
    /// current text; Open switches to the query's tab when it is open and no earlier version is picked.
    /// </summary>
    public sealed class HistoryVersionActionsTests
    {
        private sealed class Hooks
        {
            public string? Opened, OpenedServer, Copied, Executed, ExecutedServer, Activated;
            public HistoryCompareSide? Left, Right;
        }

        private static HistoryEntryDto Entry(long id, string key) => new HistoryEntryDto
        {
            Id = id, SqlText = "SELECT " + id, SessionKey = key, TabTitle = "q" + id,
            Server = "srv-a", Database = "Northwind", ExecutedAt = "2026-09-28T10:00:00.0000000Z",
        };

        private static (HistoryViewModel Vm, FakeRpcClientAccessor Fake, Hooks Hooks) NewVm(params HistoryEntryDto[] entries)
        {
            var fake = new FakeRpcClientAccessor();
            fake.Respond<HistoryActionRequest>(MessageTypes.HistoryAction,
                r => new HistoryActionResponse { Success = true, FullSqlText = "CURRENT " + r.EntryIds?.FirstOrDefault() });
            var hooks = new Hooks();
            var vm = new HistoryViewModel(fake)
            {
                SettingsProvider = () => new AppSettings(),
                FindOpenDocument = _ => null,
            };
            vm.OpenDocument = (sql, server, _, _) => { hooks.Opened = sql; hooks.OpenedServer = server; };
            vm.SetClipboard = text => hooks.Copied = text;
            vm.Execute = (sql, server, _) => { hooks.Executed = sql; hooks.ExecutedServer = server; };
            vm.ShowCompare = (left, right) => { hooks.Left = left; hooks.Right = right; };
            vm.ActivateDocument = document => { hooks.Activated = document; return true; };
            foreach (var e in entries) vm.Entries.Add(e);
            return (vm, fake, hooks);
        }

        private static int FullSqlRequests(FakeRpcClientAccessor fake) =>
            fake.Requests.Count(r => r.Payload is HistoryActionRequest a && a.Action == HistoryActions.GetFullSql);

        [Fact]
        public async Task A_selected_version_is_what_open_copy_and_reexecute_use()
        {
            var entry = Entry(7, "a");
            var (vm, _, hooks) = NewVm(entry);
            vm.SelectedEntry = entry;
            vm.SelectedVersion = new HistoryVersionDto { Id = 2, SqlText = "VERSION 2", Server = "srv-b" };

            await vm.OpenEntryAsync(entry);
            await vm.CopyEntryAsync(entry);
            await vm.ReExecuteEntryAsync(entry);

            Assert.Equal("VERSION 2", hooks.Opened);
            Assert.Equal("srv-b", hooks.OpenedServer);
            Assert.Equal("VERSION 2", hooks.Copied);
            Assert.Equal("VERSION 2", hooks.Executed);
            Assert.Equal("srv-b", hooks.ExecutedServer);
        }

        [Fact]
        public async Task Without_a_version_they_use_the_full_text()
        {
            var entry = Entry(7, "a");
            var (vm, _, hooks) = NewVm(entry);
            vm.SelectedEntry = entry;

            await vm.OpenEntryAsync(entry);
            await vm.CopyEntryAsync(entry);
            await vm.ReExecuteEntryAsync(entry);

            Assert.Equal("CURRENT 7", hooks.Opened);
            Assert.Equal("CURRENT 7", hooks.Copied);
            Assert.Equal("CURRENT 7", hooks.Executed);
            Assert.Equal("srv-a", hooks.ExecutedServer);
        }

        [Fact]
        public async Task A_version_belongs_to_the_row_it_was_picked_on()
        {
            var first = Entry(7, "a");
            var second = Entry(8, "b");
            var (vm, _, hooks) = NewVm(first, second);
            vm.SelectedEntry = first;
            vm.SelectedVersion = new HistoryVersionDto { SqlText = "VERSION 2" };

            // A row menu on another row uses that row's own text…
            await vm.CopyEntryAsync(second);
            Assert.Equal("CURRENT 8", hooks.Copied);

            // …and selecting another row forgets the version.
            vm.SelectedEntry = second;
            Assert.Null(vm.SelectedVersion);
        }

        [Fact]
        public async Task Compare_with_current_passes_the_version_and_the_current_text()
        {
            var entry = Entry(7, "a");
            var (vm, _, hooks) = NewVm(entry);
            vm.SelectedEntry = entry;
            var version = new HistoryVersionDto { SqlText = "VERSION 2", SavedAt = "2026-09-27T09:00:00.0000000Z" };

            vm.CompareWithCurrentCommand.Execute(version);
            await Task.Delay(20);

            Assert.Equal("VERSION 2", hooks.Left?.Text);
            Assert.Equal("CURRENT 7", hooks.Right?.Text);
            Assert.Equal("q7", hooks.Left?.Name);
            Assert.StartsWith("q7 (current) \u2014 ", hooks.Right?.Header);
        }

        [Fact]
        public async Task Open_switches_to_the_tab_of_an_open_query()
        {
            var entry = Entry(7, "a");
            var (vm, fake, hooks) = NewVm(entry);
            vm.FindOpenDocument = key => key == "a" ? @"C:\Queries\q7.sql" : null;
            vm.SelectedEntry = entry;

            await vm.OpenEntryAsync(entry);

            Assert.Equal(@"C:\Queries\q7.sql", hooks.Activated);
            Assert.Null(hooks.Opened);
            Assert.Equal(0, FullSqlRequests(fake));
        }

        [Fact]
        public async Task An_earlier_version_opens_in_a_new_tab_even_when_the_query_is_open()
        {
            var entry = Entry(7, "a");
            var (vm, _, hooks) = NewVm(entry);
            vm.FindOpenDocument = _ => @"C:\Queries\q7.sql";
            vm.SelectedEntry = entry;
            vm.SelectedVersion = new HistoryVersionDto { SqlText = "VERSION 2" };

            await vm.OpenEntryAsync(entry);

            Assert.Null(hooks.Activated);
            Assert.Equal("VERSION 2", hooks.Opened);
        }
    }
}
