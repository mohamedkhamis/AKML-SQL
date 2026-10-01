#nullable enable
using System.Collections.Generic;
using System.Linq;
using AkmlSql.Shell.Shared.Commands;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T173, X-01, FR-060, FR-034, SC-015) — the AKML SQL menu table (contracts/ui.md §2):
    /// 12 top-level entries in the contract's order, Help ending with Check for Updates then About,
    /// every placed command registered, and the commands with no handler left out. Also the decisions
    /// the DTE builder takes on what a bar already holds: the menu bar (version marker, rebuilt once)
    /// and the SQL editor's context menu (exactly one Format Document and one Active Style, whatever
    /// earlier SSMS starts left there).
    /// </summary>
    public sealed class AkmlMenuTableTests
    {
        private static AkmlMenuEntry TopLevel(string caption) =>
            AkmlMenuTable.Entries.Single(e => e.Caption == caption);

        [Fact]
        public void The_menu_has_12_top_level_entries_in_contract_order()
        {
            Assert.Equal(
                new[]
                {
                    "Format Document", "Format Selection", "Active Style", "Formatting",
                    "Refactor", "Navigate",
                    "SQL History", "Tabs",
                    "AI", "Tools",
                    "Options…", "Help",
                },
                AkmlMenuTable.Entries.Select(e => e.Caption).ToArray());
        }

        [Fact]
        public void Separators_split_the_top_level_groups()
        {
            Assert.Equal(
                new[] { "Refactor", "SQL History", "AI", "Options…" },
                AkmlMenuTable.Entries.Where(e => e.BeginGroup).Select(e => e.Caption).ToArray());
        }

        [Fact]
        public void The_submenus_are_the_contract_ones_and_none_is_empty()
        {
            var submenus = AkmlMenuTable.Entries.Where(e => e.IsSubmenu).ToList();
            Assert.Equal(
                new[] { "Active Style", "Formatting", "Refactor", "Navigate", "Tabs", "AI", "Tools", "Help" },
                submenus.Select(e => e.Caption).ToArray());
            Assert.All(submenus, s => Assert.NotEmpty(s.Children));
            // One level of nesting only: a submenu holds commands.
            Assert.All(submenus.SelectMany(s => s.Children), c => Assert.False(c.IsSubmenu));
        }

        [Fact]
        public void Top_level_commands_are_format_history_and_options()
        {
            Assert.Equal(CommandIds.CmdFormatDocument, TopLevel("Format Document").CommandId);
            Assert.Equal(CommandIds.CmdFormatSelection, TopLevel("Format Selection").CommandId);
            Assert.Equal(CommandIds.CmdHistoryPanel, TopLevel("SQL History").CommandId);
            Assert.Equal(CommandIds.CmdOptions, TopLevel("Options…").CommandId);
        }

        [Fact]
        public void Help_ends_with_Check_for_Updates_then_About()
        {
            var help = TopLevel("Help").Children;
            var checkForUpdates = help[help.Count - 2];
            var about = help[help.Count - 1];

            Assert.Equal(CommandIds.CmdCheckUpdate, checkForUpdates.CommandId);
            Assert.Equal("Check for Updates", checkForUpdates.Caption);
            Assert.True(checkForUpdates.BeginGroup);
            Assert.Equal(CommandIds.CmdAbout, about.CommandId);
            Assert.Equal("About AKML SQL", about.Caption);
        }

        [Fact]
        public void Every_command_in_the_table_is_registered()
        {
            var missing = AkmlMenuTable.AllCommandIds(AkmlMenuTable.Entries)
                .Concat(AkmlMenuTable.AllCommandIds(AkmlMenuTable.EditorContextEntries))
                .Where(id => !RegisteredCommands.Ids.Contains(id))
                .Select(id => $"0x{id:X4}")
                .ToList();
            Assert.Empty(missing);
        }

        [Theory]
        [InlineData(CommandIds.CmdTextToSql)]
        [InlineData(CommandIds.CmdAiOptimize)]
        [InlineData(CommandIds.CmdAiIndexAnalysis)]
        [InlineData(CommandIds.CmdCrudGeneration)]
        [InlineData(CommandIds.CmdGridFind)]
        [InlineData(CommandIds.CmdSplitTable)]
        public void Commands_with_no_handler_are_not_placed(int commandId)
        {
            Assert.DoesNotContain(commandId, AkmlMenuTable.AllCommandIds(AkmlMenuTable.Entries));
        }

        [Fact]
        public void No_command_is_placed_twice()
        {
            var ids = AkmlMenuTable.AllCommandIds(AkmlMenuTable.Entries).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        [Fact]
        public void Active_Style_lists_the_30_slots_then_Edit_Styles()
        {
            var children = TopLevel("Active Style").Children;

            Assert.Equal(
                Enumerable.Range(CommandIds.CmdActiveStyleSlot0, CommandIds.ActiveStyleSlotCount)
                    .Concat(new[] { CommandIds.CmdEditStyles }),
                children.Select(c => c.CommandId));
            // A slot's text is its style's name (set when the menu opens), so the table sets none.
            Assert.All(children.Take(CommandIds.ActiveStyleSlotCount), c => Assert.Null(c.Caption));
            Assert.Equal("Edit Styles…", children.Last().Caption);
            Assert.True(children.Last().BeginGroup);
        }

        [Fact]
        public void Only_the_AI_submenu_depends_on_AI_being_enabled()
        {
            Assert.Equal(new[] { "AI" }, AkmlMenuTable.Entries.Where(e => e.AiOnly).Select(e => e.Caption).ToArray());
            Assert.Equal(
                new[] { CommandIds.CmdAiChatPanel, CommandIds.CmdAiExplain, CommandIds.CmdAiFix },
                TopLevel("AI").Children.Select(c => c.CommandId).ToArray());
        }

        [Fact]
        public void The_editor_context_menu_gets_Format_Document_then_the_same_Active_Style_submenu()
        {
            var entries = AkmlMenuTable.EditorContextEntries;

            Assert.Equal(2, entries.Count);
            Assert.Equal(CommandIds.CmdFormatDocument, entries[0].CommandId);
            Assert.Equal("Format Document", entries[0].Caption);
            Assert.Same(TopLevel("Active Style"), entries[1]);
        }

        // ---- Menu bar: the version marker -------------------------------------------------------

        [Fact]
        public void A_menu_bar_without_AKML_SQL_gets_the_menu()
        {
            var plan = AkmlMenuTable.PlanMenuBar(new (string?, string?)[] { ("&File", null), ("&Edit", null), ("&Window", null) });

            Assert.True(plan.Add);
            Assert.Empty(plan.Remove);
        }

        [Fact]
        public void The_current_menu_is_kept_as_it_is()
        {
            var plan = AkmlMenuTable.PlanMenuBar(new (string?, string?)[] { ("&File", null), ("AKML SQL", AkmlMenuTable.Marker), ("&Window", null) });

            Assert.False(plan.Add);
            Assert.Equal(1, plan.Keep);
            Assert.Empty(plan.Remove);
        }

        [Fact]
        public void An_older_menu_without_the_marker_is_removed_and_rebuilt()
        {
            var plan = AkmlMenuTable.PlanMenuBar(new (string?, string?)[] { ("&File", null), ("AKML SQL", null), ("&Window", null) });

            Assert.True(plan.Add);
            Assert.Equal(new[] { 1 }, plan.Remove);
        }

        [Fact]
        public void Extra_AKML_SQL_menus_go_and_the_marked_one_stays()
        {
            var plan = AkmlMenuTable.PlanMenuBar(new (string?, string?)[]
            {
                ("AKML SQL", "something-else"), ("&Tools", null), ("AKML &SQL", AkmlMenuTable.Marker), ("AKML SQL", AkmlMenuTable.Marker),
            });

            Assert.False(plan.Add);
            Assert.Equal(2, plan.Keep);
            Assert.Equal(new[] { 0, 3 }, plan.Remove);
        }

        // ---- SQL editor context menu: one Format Document, one Active Style ----------------------

        private static (string?, bool)[] Visible(params string?[] captions) =>
            captions.Select(c => (c, true)).ToArray();

        [Fact]
        public void The_doubled_Format_Document_seen_in_SSMS_22_is_removed()
        {
            // The SQL editor's menu after a second start: Format Document was added again each start
            // (Command.AddControl is persistent) while the Active Style popup is rebuilt each time.
            var plans = AkmlMenuTable.PlanEditorContext(Visible(
                "Query &Options...", "Format Document", "Active Style", "Format Document"));

            Assert.Equal(1, plans[0].Keep);
            Assert.Equal(new[] { 3 }, plans[0].Remove);
            Assert.False(plans[0].Add);
            Assert.Equal(2, plans[1].Keep);
            Assert.Empty(plans[1].Remove);
            Assert.False(plans[1].Add);
        }

        [Fact]
        public void A_later_start_adds_only_what_is_missing()
        {
            // Format Document survived the restart (persistent); the Active Style popup did not.
            var plans = AkmlMenuTable.PlanEditorContext(Visible("Query &Options...", "Format Document"));

            Assert.False(plans[0].Add);
            Assert.Empty(plans[0].Remove);
            Assert.True(plans[1].Add);
        }

        [Fact]
        public void A_menu_with_neither_gets_both()
        {
            var plans = AkmlMenuTable.PlanEditorContext(Visible("Cu&t", "&Copy", "&Paste", "Query &Options..."));

            Assert.True(plans[0].Add);
            Assert.True(plans[1].Add);
            Assert.All(plans, p => Assert.Empty(p.Remove));
        }

        [Fact]
        public void A_visible_copy_is_kept_over_a_hidden_one()
        {
            var plans = AkmlMenuTable.PlanEditorContext(new (string?, bool)[]
            {
                ("Format Document", false), ("Format Document", true), ("Active Style", false),
            });

            Assert.Equal(1, plans[0].Keep);
            Assert.Equal(new[] { 0 }, plans[0].Remove);
            Assert.False(plans[0].Reveal);
            // Only a hidden Active Style: keep it and show it rather than add another.
            Assert.Equal(2, plans[1].Keep);
            Assert.True(plans[1].Reveal);
        }

        [Fact]
        public void Captions_match_without_accelerators_or_shortcut_text()
        {
            Assert.Equal("Format Document", AkmlMenuTable.NormalizeCaption("&Format Document\tCtrl+K, Y"));
            Assert.Equal("AKML SQL", AkmlMenuTable.NormalizeCaption(" AKML &SQL "));
            Assert.Equal("", AkmlMenuTable.NormalizeCaption(null));

            var plans = AkmlMenuTable.PlanEditorContext(Visible("&Format Document\tCtrl+K, Y", "Active &Style"));
            Assert.Equal(0, plans[0].Keep);
            Assert.Equal(1, plans[1].Keep);
        }
    }
}
