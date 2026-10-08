#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using AkmlSql.Shell.Shared.Formatting;
using AkmlSql.Shell.Shared.Help;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T177, X-03, FR-062) — F1 opens the right docs topic: the Options window follows
    /// the selected page (contracts/ui.md §1 "Help topic"), the Format Styles window opens the
    /// style-editor section (§3), and <see cref="HelpBinding"/> routes
    /// <see cref="ApplicationCommands.Help"/> to <see cref="F1HelpListener"/>. The browser is
    /// never launched: <see cref="F1HelpListener.OpenOverride"/> records what would have opened.
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class HelpRoutingTests : AppDataIsolatedTest
    {
        public HelpRoutingTests() : base("akmlsql-helprouting-test-") { }

        /// <summary>Page key → F1 topic: a page's own heading, a section's heading under its page.</summary>
        private static readonly Dictionary<string, string> ContractTopics = new()
        {
            ["General"] = "topics/options#general",
            ["SuggestionsBehavior"] = "topics/options#suggestions-behavior",
            ["IntelliSense"] = "topics/options#completion",
            ["CompletionPolish"] = "topics/options#tooltips",
            ["JoinOptions"] = "topics/options#join-conditions",
            ["SuggestionsLists"] = "topics/options#suggestions-lists-connections",
            ["SuggestionTypes"] = "topics/options#types-of-suggestion",
            ["ConnectionScope"] = "topics/options#connections",
            ["ConnectionsMemory"] = "topics/options#sql-server-auth-connections",
            ["Snippets"] = "topics/options#snippets",
            ["Safety"] = "topics/options#suggestions-warnings-highlighting",
            ["InsertedCode"] = "topics/options#inserted-code",
            ["InsertOptions"] = "topics/options#objects-statements",
            ["Qualification"] = "topics/options#qualification",
            ["Aliases"] = "topics/options#aliases",
            ["SpecialCharacters"] = "topics/options#special-characters",
            ["Formatting"] = "topics/options#format",
            ["ResultsExecution"] = "topics/options#queries-results-execution",
            ["Grid"] = "topics/options#query-results",
            ["Execution"] = "topics/options#execution",
            ["History"] = "topics/options#queries-history",
            ["Tabs & UI"] = "topics/options#queries-color",
            ["EditorAll"] = "topics/options#editor",
            ["Editor"] = "topics/options#productivity",
            ["Refactoring"] = "topics/options#refactoring",
            ["Navigation"] = "topics/options#navigation",
            ["Code Analysis"] = "topics/options#code-analysis",
            ["AI Assistance"] = "topics/options#ai-assistance",
        };

        [StaFact]
        public void Every_page_carries_the_contract_help_topic()
        {
            var builders = PageBuilders(new SettingsWindow(new AppSettings { Theme = "Light" }));

            Assert.Equal(ContractTopics.Keys.OrderBy(k => k), builders.Keys.OrderBy(k => k));
            foreach (var pair in ContractTopics)
                Assert.Equal(pair.Value, builders[pair.Key].HelpTopic);
        }

        [StaFact]
        public void The_options_windows_topic_is_the_selected_pages_topic_for_every_page()
        {
            var dialog = new SettingsWindow(new AppSettings { Theme = "Light" });
            var window = dialog.TestBuildWindowForRenderTest();
            var builders = PageBuilders(dialog);
            var select = typeof(SettingsWindow).GetMethod("SelectTreeLeafByPageKey", BindingFlags.Instance | BindingFlags.NonPublic)!;

            try
            {
                foreach (var pair in builders)
                {
                    // A section's key selects the page that shows it; F1 opens that page's topic.
                    Assert.True((bool)select.Invoke(dialog, new object[] { pair.Key })!, $"no tree leaf for '{pair.Key}'");
                    var shown = dialog.ShownPageKey(pair.Key);
                    Assert.Equal(shown, dialog.CurrentPageKey);
                    Assert.Equal(builders[shown].HelpTopic, dialog.CurrentHelpTopic);
                }
            }
            finally
            {
                window.Close();
            }
        }

        [StaFact]
        public void F1_in_the_options_window_opens_the_selected_pages_topic()
        {
            var dialog = new SettingsWindow(new AppSettings { Theme = "Light" });
            var window = dialog.TestBuildWindowForRenderTest("History");
            var opened = new List<string>();
            F1HelpListener.OpenOverride = opened.Add;
            using var source = new HwndSource(new HwndSourceParameters("akml-help-routing-test"));
            try
            {
                var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.F1) { RoutedEvent = Keyboard.KeyDownEvent };
                typeof(SettingsWindow).GetMethod("OnWindowKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(dialog, new object[] { window, args });

                Assert.True(args.Handled);
                Assert.Equal(new[] { "topics/options#queries-history" }, opened);
            }
            finally
            {
                F1HelpListener.OpenOverride = null;
                window.Close();
            }
        }

        [StaFact]
        public void The_format_styles_window_opens_the_style_editor_topic()
        {
            var window = NewStylesWindow();
            var opened = new List<string>();
            F1HelpListener.OpenOverride = opened.Add;
            try
            {
                Assert.Equal("topics/formatting#edit-styles-with-live-preview", window.CurrentHelpTopic);
                Assert.True(window.HasHelpButton, "the title-bar ? button must be shown");

                // F1 reaches the window as ApplicationCommands.Help (its built-in F1 gesture).
                Assert.True(ApplicationCommands.Help.CanExecute(null, window));
                ApplicationCommands.Help.Execute(null, window);

                Assert.Equal(new[] { "topics/formatting#edit-styles-with-live-preview" }, opened);
            }
            finally
            {
                F1HelpListener.OpenOverride = null;
                window.GetType().GetField("_closeConfirmed", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(window, true);
                window.Close();
            }
        }

        [StaFact]
        public void HelpBinding_routes_the_help_command_to_the_attached_key()
        {
            var inner = new TextBox();
            var host = new Border { Child = inner };
            var topic = "topics/snippets";
            HelpBinding.Attach(host, () => topic);
            var opened = new List<string>();
            F1HelpListener.OpenOverride = opened.Add;
            try
            {
                // Raised on a child, the command bubbles up to the binding on its host.
                Assert.True(ApplicationCommands.Help.CanExecute(null, inner));
                ApplicationCommands.Help.Execute(null, inner);

                // The key is read when help is asked for, not when the binding is attached.
                topic = "topics/sql-history";
                ApplicationCommands.Help.Execute(null, host);

                Assert.Equal(new[] { "topics/snippets", "topics/sql-history" }, opened);
            }
            finally
            {
                F1HelpListener.OpenOverride = null;
            }
        }

        [StaFact]
        public void Topics_resolve_to_the_docs_site_and_unknown_keys_open_nothing()
        {
            var opened = new List<string>();
            F1HelpListener.OpenOverride = opened.Add;
            try
            {
                Assert.Equal("https://akml.khamis.work/docs/topics/options#general", F1HelpRegistrations.TopicUrl("topics/options#general"));
                Assert.Null(F1HelpRegistrations.TopicUrl("akmlsql.no-such-surface"));
                Assert.Null(F1HelpRegistrations.TopicUrl(null));

                Assert.False(F1HelpListener.Default.Open("akmlsql.no-such-surface"));
                Assert.Empty(opened);

                // Registered context keys now point at the docs site, never at GitHub files.
                foreach (var key in new[] { "akmlsql.dialog.smart-rename", "akmlsql.editor.profile-3col", "akmlsql.window.sql-history" })
                    Assert.StartsWith(F1HelpRegistrations.DocBase, F1HelpListener.Default.TryResolve(key));
                Assert.Equal(F1HelpRegistrations.DocBase + "topics/formatting#edit-styles-with-live-preview",
                    F1HelpListener.Default.TryResolve("akmlsql.editor.profile-3col"));
            }
            finally
            {
                F1HelpListener.OpenOverride = null;
            }
        }

        private static Dictionary<string, IPageBuilder> PageBuilders(SettingsWindow dialog) =>
            (Dictionary<string, IPageBuilder>)typeof(SettingsWindow)
                .GetField("_pageBuilders", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;

        /// <summary>
        /// Outside VS/SSMS, DialogWindow's first construction can fail once while it looks up
        /// IVsSettingsManager (a XamlParseException); the next construction succeeds.
        /// </summary>
        private static FormatStylesEditorWindow NewStylesWindow()
        {
            var vm = new FormatStylesEditorViewModel(new FakeRpcClientAccessor());
            try { return new FormatStylesEditorWindow(vm); }
            catch (System.Windows.Markup.XamlParseException) { return new FormatStylesEditorWindow(vm); }
        }
    }
}
