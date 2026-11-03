#nullable enable
using System.Linq;
using System.Windows.Controls;
using AkmlSql.Shell.Shared.Editor.Completion;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// UI scan 2026-11-03 (S1) — in a Northwind query window, typing <c>select * from dbo</c> opened
    /// the suggestions box with TABLES first (dbo.Orders pre-selected) and SCHEMAS (dbo) last, so the
    /// dot that followed committed the table: <c>dbo.Orders.</c>. The typed word's exact match scored
    /// the same as every "dbo."-prefixed table (0 = prefix) and the engine's SortPriority (tables
    /// 100 minus a row-count bonus, schemas 300) decided. An exact match of the typed word now ranks
    /// ahead of prefix / fuzzy matches, and a dot never commits an item whose owner is the typed word.
    /// </summary>
    public class CompletionExactMatchTests
    {
        internal static CompletionItemModel Table(string name, int priority) => new()
        {
            DisplayText = "dbo." + name,
            InsertText = "dbo." + name,
            FilterText = name,
            SourceObject = "dbo." + name,
            SecondaryText = "Table",
            ObjectType = 0,
            SortPriority = priority,
        };

        internal static CompletionItemModel Schema(string name) => new()
        {
            DisplayText = name,
            InsertText = name,
            SecondaryText = "Schema",
            ObjectType = 7,
            SortPriority = 300,
        };

        /// <summary>The engine's FROM-clause reply for Northwind, in the engine's order.</summary>
        internal static CompletionItemModel[] NorthwindFromList() => new[]
        {
            Table("Orders", 67),
            Table("Products", 81),
            Table("OrderDetails", 70),
            Table("sysdiagrams", 100),
            Schema("dbo"),
        };

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void The_exact_match_of_the_typed_word_scores_ahead_of_a_prefix_match(bool prefixOnly)
        {
            var schema = Schema("dbo");
            var table = Table("Orders", 67);

            Assert.True(schema.IsExactMatch("dbo"));
            Assert.False(table.IsExactMatch("dbo"));
            Assert.True(schema.FilterScore("dbo", prefixOnly) < table.FilterScore("dbo", prefixOnly),
                $"schema {schema.FilterScore("dbo", prefixOnly)} vs table {table.FilterScore("dbo", prefixOnly)}");
        }

        [Fact]
        public void An_exact_match_is_case_insensitive()
        {
            Assert.True(Schema("dbo").IsExactMatch("DBO"));
            Assert.True(Schema("Sales").IsExactMatch("sales"));
        }

        [Fact]
        public void The_filter_text_of_a_qualified_name_counts_as_its_exact_match()
        {
            // "dbo.Orders" is matched on its object name (FilterText = "Orders").
            var table = Table("Orders", 67);
            var details = Table("OrderDetails", 70);

            Assert.True(table.IsExactMatch("orders"));
            Assert.False(details.IsExactMatch("orders"));
            Assert.True(table.FilterScore("Orders") < details.FilterScore("Orders"));
        }

        [Fact]
        public void An_empty_filter_is_never_an_exact_match()
        {
            Assert.False(Schema("dbo").IsExactMatch(string.Empty));
            Assert.Equal(300, Schema("dbo").FilterScore(string.Empty));
        }

        [Fact]
        public void The_existing_prefix_camel_and_substring_scores_are_unchanged()
        {
            var item = new CompletionItemModel { DisplayText = "ProductCategory" };

            Assert.Equal(0, item.FilterScore("Prod"));
            Assert.Equal(50, item.FilterScore("PC"));
            Assert.Equal(100, item.FilterScore("Cat"));
        }

        // ── dot-commit: the schema is committed, never a table of that schema ──────────────

        [Fact]
        public void A_dot_after_the_schema_name_commits_the_schema_and_never_a_table_of_it()
        {
            Assert.True(Schema("dbo").ShouldCommitBeforeDot("dbo"));
            Assert.False(Table("Orders", 67).ShouldCommitBeforeDot("dbo"));
            Assert.False(Table("Orders", 67).ShouldCommitBeforeDot("DBO"));
        }

        [Fact]
        public void A_dot_after_a_table_prefix_still_commits_the_table()
        {
            Assert.True(Table("Orders", 67).ShouldCommitBeforeDot("Ord"));
            Assert.True(Table("Orders", 67).ShouldCommitBeforeDot("Orders"));
            Assert.True(Table("Orders", 67).ShouldCommitBeforeDot("o"));
        }

        [Fact]
        public void The_owner_is_read_from_the_source_object_so_a_bracketed_insert_text_is_recognised()
        {
            var bracketed = Table("Order Details", 70);
            bracketed.DisplayText = "dbo.Order Details";
            bracketed.InsertText = "[dbo].[Order Details]";

            Assert.False(bracketed.ShouldCommitBeforeDot("dbo"));
        }

        [Fact]
        public void An_unqualified_item_is_always_committed_before_a_dot()
        {
            var keyword = new CompletionItemModel { DisplayText = "FROM", InsertText = "FROM", ObjectType = 3 };
            var alias = new CompletionItemModel { DisplayText = "o", InsertText = "o", ObjectType = 10 };

            Assert.True(keyword.ShouldCommitBeforeDot("fro"));
            Assert.True(alias.ShouldCommitBeforeDot("o"));
        }

        /// <summary>Review of the S1 fix: an alias-qualified column shows "p.UnitPrice" but its source object is the table
        /// ("dbo.Products"); typing <c>p</c> + <c>.</c> is qualifying by the alias, so the dot must not commit it either.</summary>
        [Fact]
        public void A_dot_after_an_alias_does_not_commit_a_column_displayed_under_that_alias()
        {
            var column = new CompletionItemModel
            {
                DisplayText = "p.UnitPrice",
                InsertText = "p.UnitPrice",
                FilterText = "UnitPrice",
                SourceObject = "dbo.Products",
                SecondaryText = "money, NULL",
                ObjectType = 2,
                SortPriority = 30,
            };

            Assert.False(column.ShouldCommitBeforeDot("p"));
            Assert.False(column.ShouldCommitBeforeDot("dbo"));
            Assert.True(column.ShouldCommitBeforeDot("Unit"));
        }
    }

    /// <summary>
    /// The same scenario through the suggestions box: filtering the engine's list on the typed word.
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class CompletionPopupExactMatchTests
    {
        private static AkmlCompletionPopup Open(string typed, bool prefixOnly = false)
        {
            var popup = new AkmlCompletionPopup { PrefixOnly = prefixOnly };
            popup.SetItems(CompletionExactMatchTests.NorthwindFromList());
            popup.SetFilter(typed);
            return popup;
        }

        [StaFact]
        public void Typing_dbo_pre_selects_the_dbo_schema_ahead_of_its_tables()
        {
            var popup = Open("dbo");

            var selected = popup.GetSelectedItem();
            Assert.NotNull(selected);
            Assert.Equal(7, selected!.ObjectType);
            Assert.Equal("dbo", selected.InsertText);
        }

        [StaFact]
        public void Typing_dbo_with_fuzzy_matching_off_pre_selects_the_dbo_schema_too()
        {
            var popup = Open("dbo", prefixOnly: true);

            var selected = popup.GetSelectedItem();
            Assert.NotNull(selected);
            Assert.Equal("dbo", selected!.InsertText);
        }

        [StaFact]
        public void Typing_dbo_lists_the_schemas_group_before_the_tables_group()
        {
            var popup = Open("dbo");

            // The ListBox holds header rows (a Grid with the upper-cased group name) and item rows;
            // the first header must be the group the exact match belongs to.
            var listBox = LogicalTree.Descendants<ListBox>(popup).Single();
            var headers = listBox.Items.OfType<Grid>()
                .SelectMany(g => g.Children.OfType<TextBlock>())
                .Select(t => t.Text)
                .Where(t => t == "SCHEMAS" || t == "TABLES")
                .ToArray();

            Assert.Equal(new[] { "SCHEMAS", "TABLES" }, headers);

            // Down from the schema lands on the best-ranked table, so the tables are still one key away.
            popup.MoveSelection(1);
            Assert.Equal("dbo.Orders", popup.GetSelectedItem()!.InsertText);
        }

        [StaFact]
        public void Typing_a_table_prefix_still_pre_selects_a_table_with_the_tables_group_first()
        {
            var popup = Open("Ord");

            var selected = popup.GetSelectedItem();
            Assert.NotNull(selected);
            Assert.Equal(0, selected!.ObjectType);
            Assert.Equal("dbo.Orders", selected.InsertText);
        }

        [StaFact]
        public void Typing_the_whole_table_name_pre_selects_that_table_over_a_longer_one()
        {
            var popup = Open("Orders");

            Assert.Equal("dbo.Orders", popup.GetSelectedItem()!.InsertText);
        }
    }
}
