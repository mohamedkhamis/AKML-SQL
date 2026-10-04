#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.History;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T123, HIS-11/HIS-12, T185) — SQL History from the keyboard: the list's keys act on
    /// the selected query, Esc in the search box clears the text and then the filters, the row star
    /// and ⋯ are named focusable buttons, every icon-only toolbar control is named, and the row and
    /// toolbar menus hold SQL Prompt's items. Built headlessly (never shown).
    /// </summary>
    /// <remarks>No <c>await Task.Yield()</c>: an [StaFact] has no synchronization context. The fake
    /// client answers synchronously, so the commands' async work completes inside Execute. The
    /// control may read settings, so it runs with its own AppData root.</remarks>
    [Collection("AkmlSql AppData isolation")]
    public sealed class HistoryKeyboardTests : AppDataIsolatedTest
    {
        public HistoryKeyboardTests() : base("akmlsql-history-keyboard-test-") { }

        private sealed class Harness
        {
            public HistoryViewModel Vm = null!;
            public HistoryToolWindowControl Control = null!;
            public FakeRpcClientAccessor Fake = null!;
            public HistoryEntryDto Entry = null!;
            public string? Opened, Copied, Notified, RenamePrompt;
            public bool Asked;

            public List<HistoryActionRequest> Actions(int action) =>
                Fake.Requests.Select(r => r.Payload).OfType<HistoryActionRequest>().Where(a => a.Action == action).ToList();
        }

        private static Harness Build(bool open = false)
        {
            var h = new Harness { Fake = new FakeRpcClientAccessor() };
            h.Fake.Respond<HistoryActionRequest>(MessageTypes.HistoryAction,
                r => new HistoryActionResponse { Success = true, FullSqlText = "FULL " + r.EntryIds?.FirstOrDefault(), IsFavorite = true });
            h.Fake.Respond(MessageTypes.HistorySearch, new HistorySearchResponse { Success = true, Entries = Array.Empty<HistoryEntryDto>() });

            h.Vm = new HistoryViewModel(h.Fake)
            {
                SettingsProvider = () => new AppSettings(),
                Scheduler = (_, _) => new NoOp(),
                FindOpenDocument = _ => null,
            };
            h.Control = new HistoryToolWindowControl(h.Vm);

            // Hooks after the control, which sets its own.
            h.Vm.OpenDocument = (sql, _, _, _) => h.Opened = sql;
            h.Vm.SetClipboard = text => h.Copied = text;
            h.Vm.Notify = text => h.Notified = text;
            h.Vm.ConfirmPrompt = _ => { h.Asked = true; return true; };
            h.Vm.PromptRename = current => { h.RenamePrompt = current; return "Monthly totals"; };

            h.Entry = new HistoryEntryDto { Id = 7, SqlText = "SELECT 7", TabTitle = "q7", SessionKey = "a", IsOpen = open, ExecutedAt = "2026-09-28T10:00:00.0000000Z" };
            h.Vm.Entries.Add(h.Entry);
            h.Vm.SelectedEntry = h.Entry;
            return h;
        }

        private sealed class NoOp : IDisposable { public void Dispose() { } }

        [StaFact]
        public void Enter_opens_the_query()
        {
            var h = Build();
            Assert.True(h.Control.HandleListKey(Key.Enter, ModifierKeys.None));
            Assert.Equal("FULL 7", h.Opened);
        }

        [StaFact]
        public void Delete_removes_after_asking()
        {
            var h = Build();
            Assert.True(h.Control.HandleListKey(Key.Delete, ModifierKeys.None));
            Assert.True(h.Asked);
            Assert.Equal(7, Assert.Single(h.Actions(HistoryActions.Delete)).EntryIds.Single());
        }

        [StaFact]
        public void F2_renames_a_closed_query()
        {
            var h = Build(open: false);
            Assert.True(h.Control.HandleListKey(Key.F2, ModifierKeys.None));
            Assert.Equal("q7", h.RenamePrompt);
            Assert.Equal("Monthly totals", Assert.Single(h.Actions(HistoryActions.Rename)).NewName);
        }

        [StaFact]
        public void F2_while_the_list_reloads_renames_once_it_has_loaded()
        {
            // F2 right after Space (which reloads the list) was dropped without a word.
            var h = Build(open: false);
            h.Vm.IsLoading = true;

            Assert.True(h.Control.HandleListKey(Key.F2, ModifierKeys.None));
            Assert.Null(h.RenamePrompt);

            h.Vm.IsLoading = false;
            Assert.Equal("q7", h.RenamePrompt);
            Assert.Single(h.Actions(HistoryActions.Rename));

            h.Vm.IsLoading = true;
            h.Vm.IsLoading = false;   // once only
            Assert.Single(h.Actions(HistoryActions.Rename));
        }

        [StaFact]
        public void F2_is_refused_while_the_query_is_open()
        {
            var h = Build(open: true);
            Assert.True(h.Control.HandleListKey(Key.F2, ModifierKeys.None));
            Assert.Null(h.RenamePrompt);
            Assert.Empty(h.Actions(HistoryActions.Rename));
            Assert.Contains("Close it to rename", h.Notified);
        }

        [StaFact]
        public void Ctrl_C_copies_the_SQL()
        {
            var h = Build();
            Assert.True(h.Control.HandleListKey(Key.C, ModifierKeys.Control));
            Assert.Equal("FULL 7", h.Copied);
        }

        [StaFact]
        public void Space_stars_the_query()
        {
            var h = Build();
            Assert.True(h.Control.HandleListKey(Key.Space, ModifierKeys.None));
            Assert.Single(h.Actions(HistoryActions.ToggleFavorite));
        }

        [StaFact]
        public void Other_keys_are_left_to_the_list()
        {
            var h = Build();
            Assert.False(h.Control.HandleListKey(Key.Down, ModifierKeys.None));
            Assert.False(h.Control.HandleListKey(Key.C, ModifierKeys.None));
        }

        [StaFact]
        public void Esc_in_the_search_box_clears_the_text_then_the_filters()
        {
            var h = Build();
            h.Vm.SearchText = "orders";
            h.Vm.FavoritesOnly = true;

            Assert.True(h.Control.HandleSearchKey(Key.Escape));
            Assert.Equal(string.Empty, h.Vm.SearchText);
            Assert.True(h.Vm.FavoritesOnly);

            Assert.True(h.Control.HandleSearchKey(Key.Escape));
            Assert.False(h.Vm.FavoritesOnly);
        }

        [StaFact]
        public void The_row_star_and_menu_are_named_focusable_buttons()
        {
            var h = Build();
            var template = h.Control.QueryItemTemplate!;
            template.Seal(); // WPF seals a template on first use; LoadContent needs it sealed
            var row = (DependencyObject)template.LoadContent();

            var buttons = Descendants(row).OfType<Button>().ToList();
            var star = Assert.Single(buttons, b => AutomationProperties.GetName(b) == "Star query");
            var menu = Assert.Single(buttons, b => AutomationProperties.GetName(b) == "Query actions");
            Assert.True(star.Focusable);
            Assert.True(menu.Focusable);
        }

        [StaFact]
        public void Every_icon_only_toolbar_control_is_named()
        {
            var h = Build();
            var names = Descendants(h.Control).OfType<Button>().Select(AutomationProperties.GetName).ToList();

            foreach (var expected in new[]
            {
                "Refresh", "Show starred queries only", "Show open queries only", "Show closed queries only",
                "Filter by server or database", "More actions", "Clear search",
            })
                Assert.Contains(expected, names);
        }

        [StaFact]
        public void The_menus_hold_SQL_Prompts_items()
        {
            var h = Build();

            var rowMenu = h.Control.FillRowMenu(h.Entry);
            var items = rowMenu.Items.OfType<MenuItem>().ToList();
            Assert.Equal(new[]
            {
                "Open query", "Copy SQL", "Re-execute", "Rename query", "Compare…",
                "Remove query and its history", "Remove queries older than this…",
            }, items.Select(i => (string)i.Header).ToArray());
            Assert.All(items, i => Assert.Same(h.Entry, i.CommandParameter));

            Assert.Equal(new[] { "Export…", "Clear history…" }, h.Control.ToolbarMenuHeaders.ToArray());
        }

        [StaFact]
        public void Focus_goes_back_to_the_list_only_when_a_refresh_dropped_it()
        {
            // Space (star) refreshed the list, the focused row went, focus fell to the window and
            // the next key (F2) did nothing. Focus that moved somewhere the user chose stays there.
            var history = new Border();
            var search = new TextBox();
            var host = new Grid();
            host.Children.Add(history);
            var editor = new TextBox();
            var panel = new StackPanel();
            panel.Children.Add(search);
            history.Child = panel;

            Assert.True(HistoryToolWindowControl.FocusFellBack(null, history));
            Assert.True(HistoryToolWindowControl.FocusFellBack(history, history));
            Assert.True(HistoryToolWindowControl.FocusFellBack(host, history));
            Assert.False(HistoryToolWindowControl.FocusFellBack(search, history));
            Assert.False(HistoryToolWindowControl.FocusFellBack(editor, history));
        }

        [StaFact]
        public void The_list_selects_the_row_the_view_model_selects()
        {
            var h = Build();
            var list = Descendants(h.Control).OfType<ListView>().Single(l => AutomationProperties.GetName(l) == "Queries");
            Assert.Same(h.Entry, list.SelectedItem);

            // A new search selects its first row in the view model (HIS-09); the list follows.
            var next = new HistoryEntryDto { Id = 8, SqlText = "SELECT 8", TabTitle = "q8", SessionKey = "b", ExecutedAt = "2026-09-28T11:00:00.0000000Z" };
            h.Vm.Entries.Add(next);
            h.Vm.SelectedEntry = next;
            Assert.Same(next, list.SelectedItem);
            Assert.Same(next, h.Vm.SelectedEntry);
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                yield return node;
                foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                    stack.Push(child);
            }
        }
    }
}
