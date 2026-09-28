#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T010, OPT-01, FR-001–003, research R1) — every setting shown in Options changes
    /// the product. The allow-list below is every searchable row that is allowed to exist; a row
    /// added without being wired (or without updating this list) fails here. Later stories that
    /// add, rename or move rows update this list in the same change.
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class OptionsLiveSettingsTests
    {
        /// <summary>(PageKey, Label) of every row the Options window may show.</summary>
        internal static readonly (string PageKey, string Label)[] AllowList =
        {
            ("General", "Theme"),
            ("General", "Check for updates automatically"),
            ("General", "Send anonymous error reports"),
            ("General", "Configuration file"),
            ("General", "Log directory"),
            ("General", "Version"),
            ("IntelliSense", "Enable IntelliSense"),
            ("IntelliSense", "Auto-trigger completions while typing"),
            ("IntelliSense", "Trigger after dot"),
            ("IntelliSense", "Enable fuzzy matching"),
            ("IntelliSense", "Maximum suggestions"),
            ("IntelliSense", "Trigger delay (ms)"),
            ("IntelliSense", "Show column data types"),
            ("IntelliSense", "Show nullability info"),
            ("IntelliSense", "Show PK/FK indicators"),
            ("IntelliSense", "Make popups transparent when Ctrl is held"),
            ("IntelliSense", "JOIN clause assistance"),
            ("IntelliSense", "Tables Alias"),
            ("IntelliSense", "Disable native SSMS IntelliSense"),
            ("IntelliSense", "Commit with Space"),
            ("IntelliSense", "Commit with Dot"),
            ("IntelliSense", "Show snippets in the completion list"),
            ("SuggestionTypes", "List system objects"),
            ("SuggestionTypes", "Show keywords in suggestions"),
            ("SuggestionTypes", "Suggest columns from"),
            ("CompletionPolish", "Show the object definition box"),
            ("Aliases", "Include the AS keyword"),
            ("Aliases", "Custom alias map"),
            ("Aliases", "Prefixes to ignore"),
            ("ConnectionScope", "Limit databases to"),
            ("ConnectionScope", "Limit schemas to"),
            ("ConnectionScope", "Include linked-server objects in suggestions"),
            ("ConnectionsMemory", "Use SQL Server-auth credentials for IntelliSense"),
            ("ConnectionsMemory", "Saved SQL passwords"),
            ("Qualification", "Qualify object names with schema"),
            ("SpecialCharacters", "Bracket identifiers"),
            ("SpecialCharacters", "Add parentheses ( ) when inserting a function or data type"),
            ("SpecialCharacters", "Automatically insert the corresponding closing character"),
            ("SpecialCharacters", "Single quotation mark ( ' )"),
            ("SpecialCharacters", "Double quotation mark ( \" )"),
            ("SpecialCharacters", "Comment mark ( */ )"),
            ("SpecialCharacters", "Parenthesis )"),
            ("SpecialCharacters", "Square bracket ]"),
            ("InsertOptions", "Insert column names"),
            ("InsertOptions", "Insert default values as comments"),
            ("InsertOptions", "Convert positional parameters to named"),
            ("JoinOptions", "Use matching column names when no FK exists"),
            ("JoinOptions", "Related"),
            ("Formatting", "Active style"),
            ("Formatting", "Edit formatting styles"),
            ("Formatting", "Show active style in status bar"),
            ("Formatting", "Enable SQL formatter"),
            ("Formatting", "Create backups before formatting"),
            ("Snippets", "Enable snippets"),
            ("Snippets", "Format after expansion"),
            ("Snippets", "Team folder"),
            ("Code Analysis", "Enable code analysis"),
            ("Code Analysis", "Analyze while typing"),
            ("Code Analysis", "Show in Error List"),
            ("Code Analysis", "Rules"),
            ("Code Analysis", "Per-project config"),
            ("Code Analysis", "Inline suppression"),
            ("Code Analysis", "Disable a rule"),
            ("Refactoring", "Include comments in rename scope"),
            ("History", "Enable SQL history recording"),
            ("History", "Group repeated runs of the same query"),
            ("History", "Retention (days)"),
            ("History", "Max entries"),
            ("History", "Disable automatic history trimming"),
            ("Tabs & UI", "Enable environment-based tab coloring"),
            ("Tabs & UI", "Use gradient colors"),
            ("Tabs & UI", "Enable session recovery"),
            ("Tabs & UI", "Auto-save interval (seconds)"),
            ("Tabs & UI", "Restore on startup"),
            ("Tabs & UI", "Max closed tabs to remember"),
            ("Tabs & UI", "Custom window title template"),
            ("Safety", "Production server warning"),
            ("Safety", "DELETE without WHERE"),
            ("Safety", "UPDATE without WHERE"),
            ("Safety", "DROP confirmation"),
            ("Safety", "TRUNCATE confirmation"),
            ("Safety", "Enable transaction reminder"),
            ("Safety", "Reminder interval (seconds)"),
            ("AI Assistance", "AI agents"),
            ("AI Assistance", "Name"),
            ("AI Assistance", "Enabled"),
            ("AI Assistance", "AI Provider"),
            ("AI Assistance", "Model"),
            ("AI Assistance", "API Key"),
            ("AI Assistance", "Test connection"),
            ("AI Assistance", "Endpoint URL"),
            ("AI Assistance", "Agent for the chat panel"),
            ("AI Assistance", "Agent for text-to-SQL"),
            ("AI Assistance", "Agent for Explain SQL"),
            ("AI Assistance", "Agent for Fix errors"),
            ("AI Assistance", "Agent for Optimize queries"),
            ("AI Assistance", "Agent for index suggestions"),
            ("AI Assistance", "Agent for inline ghost text"),
            ("AI Assistance", "Add to fallback order"),
            ("AI Assistance", "Fallback order"),
            ("AI Assistance", "Privacy mode"),
            ("AI Assistance", "Consent to cloud AI data sharing"),
            ("AI Assistance", "Max response tokens"),
            ("AI Assistance", "Temperature (x10)"),
            ("AI Assistance", "Timeout (seconds)"),
            ("AI Assistance", "Retries"),
            ("AI Assistance", "Natural language to SQL"),
            ("AI Assistance", "Explain SQL"),
            ("AI Assistance", "Fix errors"),
            ("AI Assistance", "Optimize queries"),
            ("AI Assistance", "Index suggestions"),
            ("AI Assistance", "Inline ghost text"),
            ("AI Assistance", "Auto-fix on error"),
            ("Grid", "Aggregate statistics"),
            ("Grid", "Highlight NULL cells"),
            ("Grid", "Row numbers"),
            ("Grid", "Save 15+ digit numbers as text"),
            ("Editor", "Highlight occurrences"),
            ("Editor", "Bracket matching"),
            ("Editor", "Sticky scroll"),
            ("Editor", "Code minimap"),
            ("Execution", "Execution timer"),
            ("Execution", "Multi-database execution"),
            ("Execution", "Notification threshold"),
        };

        /// <summary>Rows that changed nothing and must stay hidden until they work.</summary>
        private static readonly string[] HiddenLabels =
        {
            "Keyword casing", "List all database columns after SELECT", "Freeze headers", "Encrypt at rest",
            "Record failed executions", "Format on paste", "Format on save", "Format on delimiter",
            "Confirm before bulk format", "Validate formatting preserves semantics", "Respect --noformat regions",
            "Named regions", "Show preview before applying", "Rename scope", "Chat panel",
            "Include string literals in rename scope", "Format after refactoring", "Analyze on save",
            "Document Outline", "Go to Definition", "Peek Definition", "Find All References", "Object Search",
            "Show object descriptions in tooltips", "Highlight the active parameter in signature help",
            "Decrypt encrypted procedures and functions", "Temp-table IntelliSense", "Column Picker default sort",
            "Max cached databases", "Lazy-load column metadata", "Persist cache to disk",
            "Qualify columns with table name or alias", "Show in IntelliSense completions", "Filter by SQL context",
            "Track usage for ranking", "Personal folder",
            "Ghost-text AI completion", "Parallel schema cache", "Shared snippet sync", "Labs notice",
            "Auto-refresh schema cache", "Refresh interval (seconds)", "Detect DDL changes",
        };

        private static List<(string PageKey, string Label)> SearchIndex(SettingsWindow dialog)
        {
            var f = typeof(SettingsWindow).GetField("_searchIndex", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var result = new List<(string, string)>();
            foreach (var entry in (IEnumerable)f.GetValue(dialog)!)
            {
                var t = entry.GetType();
                result.Add(((string)t.GetProperty("PageKey")!.GetValue(entry)!, (string)t.GetProperty("Label")!.GetValue(entry)!));
            }
            return result;
        }

        [StaFact]
        public void Every_shown_setting_is_on_the_allow_list()
        {
            var dialog = new SettingsWindow(new AppSettings());
            _ = dialog.TestBuildWindowForRenderTest();

            var shown = SearchIndex(dialog);
            var unexpected = shown.Except(AllowList).ToList();
            var missing = AllowList.Except(shown).ToList();

            Assert.True(unexpected.Count == 0, "Rows not on the allow-list: " + string.Join("; ", unexpected));
            Assert.True(missing.Count == 0, "Allow-listed rows no longer shown: " + string.Join("; ", missing));
        }

        [StaFact]
        public void Dead_settings_are_hidden()
        {
            var dialog = new SettingsWindow(new AppSettings());
            _ = dialog.TestBuildWindowForRenderTest();

            var labels = SearchIndex(dialog).Select(e => e.Label).ToList();
            foreach (var hidden in HiddenLabels)
                Assert.DoesNotContain(hidden, labels);
        }

        [StaFact]
        public void The_database_and_labs_pages_are_gone()
        {
            var dialog = new SettingsWindow(new AppSettings());
            var window = dialog.TestBuildWindowForRenderTest();

            var tags = LogicalTree.Descendants<TreeViewItem>(window).Select(i => i.Tag as string).ToList();
            Assert.DoesNotContain("Schema Cache", tags);
            Assert.DoesNotContain("Labs", tags);
        }
    }
}
