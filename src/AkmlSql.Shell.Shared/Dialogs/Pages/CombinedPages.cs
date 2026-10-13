#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// An Options page made of several smaller pages, shown one after another as its sections —
    /// each section keeps its own group headers, rows, Load/Save/Reset and tests. Many of SQL
    /// Prompt's pages held one to three settings; combining them leaves twelve pages in the tree.
    /// The section pages stay registered under their own keys, so a link to one opens this page.
    /// </summary>
    internal abstract class CombinedPage : IPageBuilder
    {
        protected CombinedPage(params IPageBuilder[] sections) => Sections = sections;

        /// <summary>The pages shown as this page's sections, top to bottom.</summary>
        public IReadOnlyList<IPageBuilder> Sections { get; }

        public abstract string Key { get; }
        public abstract string Display { get; }
        public abstract string Title { get; }
        public abstract string HelpTopic { get; }
        public abstract string Help { get; }

        public IPageControls Build(StackPanel panel, PageContext ctx) =>
            new CombinedControls(BuildSections(panel, ctx));

        /// <summary>
        /// Builds every section into <paramref name="panel"/>, top to bottom, and returns each
        /// section's controls under its key. <paramref name="beforeSection"/> runs before each
        /// section builds — the host uses it to attribute the section's search entries.
        /// </summary>
        public virtual IReadOnlyList<(string Key, IPageControls Controls)> BuildSections(
            StackPanel panel, PageContext ctx, Action<IPageBuilder>? beforeSection = null)
        {
            var built = new List<(string Key, IPageControls Controls)>();
            foreach (var section in Sections)
            {
                beforeSection?.Invoke(section);
                built.Add((section.Key, section.Build(panel, ctx)));
            }
            return built;
        }
    }

    /// <summary>
    /// A <see cref="CombinedPage"/> whose sections sit on in-page tabs (the AI page's idiom,
    /// design B of the 2026-10 Options redesign): one <see cref="OptionsTabStrip"/> tab per
    /// group, each section built into its tab's panel. Sections keep their keys, controls and
    /// search entries; a searched row on a hidden tab reveals its tab through the strip.
    /// </summary>
    internal abstract class TabbedPage : CombinedPage
    {
        protected TabbedPage(params (string Label, IPageBuilder[] Sections)[] tabs)
            : base(tabs.SelectMany(t => t.Sections).ToArray())
        {
            Tabs = tabs.Select(t => (t.Label, (IReadOnlyList<IPageBuilder>)t.Sections)).ToList();
        }

        /// <summary>The tabs, in order, each with the sections it shows.</summary>
        public IReadOnlyList<(string Label, IReadOnlyList<IPageBuilder> Sections)> Tabs { get; }

        /// <summary>The strip the last build created (test seam).</summary>
        public OptionsTabStrip? Strip { get; private set; }

        /// <summary>
        /// Runs after each section builds, in order — a page overrides it to hand one section's
        /// control (a master switch) to a section on another tab before that one builds.
        /// </summary>
        protected virtual void OnSectionBuilt(IPageBuilder section, IPageControls controls) { }

        public override IReadOnlyList<(string Key, IPageControls Controls)> BuildSections(
            StackPanel panel, PageContext ctx, Action<IPageBuilder>? beforeSection = null)
        {
            var strip = new OptionsTabStrip(ctx.Theme);
            var panels = Tabs.Select(t => strip.AddTab(t.Label)).ToList();
            strip.AddTo(panel);
            Strip = strip;

            var built = new List<(string Key, IPageControls Controls)>();
            for (var i = 0; i < Tabs.Count; i++)
            {
                foreach (var section in Tabs[i].Sections)
                {
                    beforeSection?.Invoke(section);
                    var controls = section.Build(panels[i], ctx);
                    OnSectionBuilt(section, controls);
                    built.Add((section.Key, controls));
                }
            }
            return built;
        }
    }

    /// <summary>The controls of a <see cref="CombinedPage"/>: each section's, in order.</summary>
    internal sealed class CombinedControls : IPageControls
    {
        public CombinedControls(IReadOnlyList<(string Key, IPageControls Controls)> sections) => Sections = sections;

        public IReadOnlyList<(string Key, IPageControls Controls)> Sections { get; }

        public void Load(AppSettings settings)
        {
            foreach (var (_, controls) in Sections) controls.Load(settings);
        }

        public void Save(AppSettings settings)
        {
            foreach (var (_, controls) in Sections) controls.Save(settings);
        }

        public void Reset(AppSettings defaults)
        {
            foreach (var (_, controls) in Sections) controls.Reset(defaults);
        }
    }

    /// <summary>
    /// Suggestions › Behavior: two tabs — Completion (the IntelliSense rows and the definition
    /// box) and JOIN assistance (the JOIN rows, greyed by Completion's Enable IntelliSense).
    /// </summary>
    internal sealed class SuggestionsBehaviorPage : TabbedPage
    {
        private readonly JoinAssistancePage _joinAssistance;

        public SuggestionsBehaviorPage(IPageBuilder intelliSense, IPageBuilder joinAssistance)
            : base(("Completion", new[] { intelliSense }), ("JOIN assistance", new[] { joinAssistance }))
        {
            _joinAssistance = (JoinAssistancePage)joinAssistance;
        }

        public override string Key       => "SuggestionsBehavior";
        public override string Display   => "Suggestions › Behavior";
        public override string Title     => "Behavior";
        public override string HelpTopic => "topics/options#suggestions-behavior";
        public override string Help      => "How the suggestions box behaves: when it opens, what it shows beside each item, the definition box, commit keys and snippets — and, on the JOIN assistance tab, FK-assisted JOINs, aliases and join conditions.";

        // The Completion tab builds first; its master switch greys the JOIN tab's rows.
        protected override void OnSectionBuilt(IPageBuilder section, IPageControls controls)
        {
            if (controls is IntelliSenseControls intelliSense)
                _joinAssistance.MasterSwitch = intelliSense.Enabled;
        }
    }

    /// <summary>Suggestions › Lists &amp; scope: what the suggestions box lists and where it looks.</summary>
    internal sealed class SuggestionsListsPage : CombinedPage
    {
        public SuggestionsListsPage(IPageBuilder types, IPageBuilder scope)
            : base(types, scope) { }

        public override string Key       => "SuggestionsLists";
        public override string Display   => "Suggestions › Lists & scope";
        public override string Title     => "Lists & scope";
        public override string HelpTopic => "topics/options#suggestions-lists-connections";
        public override string Help      => "What the suggestions box lists and where it looks: system objects, keywords and column scope; the databases, schemas and linked servers it draws on. Snippets have their own page; SQL Server-auth connections are on General.";
    }

    /// <summary>General: appearance, updates and telemetry, SQL Server connections, paths and version.</summary>
    internal sealed class GeneralAllPage : CombinedPage
    {
        public GeneralAllPage(IPageBuilder general, IPageBuilder sqlServerConnections)
            : base(general, sqlServerConnections) { }

        public override string Key       => "GeneralAll";
        public override string Display   => "General";
        public override string Title     => "General";
        public override string HelpTopic => "topics/options#general";
        public override string Help      => "The AKML SQL dialog theme, automatic update checks and anonymous error reporting; how SQL Server-auth windows get IntelliSense and the passwords stored for them; the configuration file, log directory and installed version.";
    }

    /// <summary>
    /// Queries › History: two tabs — Recording (with storage) and Restoring queries, whose rows
    /// grey with Recording's Enable SQL history.
    /// </summary>
    internal sealed class HistoryAllPage : TabbedPage
    {
        private readonly HistoryRestorePage _restore;

        public HistoryAllPage(IPageBuilder recording, IPageBuilder restoring)
            : base(("Recording", new[] { recording }), ("Restoring queries", new[] { restoring }))
        {
            _restore = (HistoryRestorePage)restoring;
        }

        public override string Key       => "HistoryAll";
        public override string Display   => "Queries › History";
        public override string Title     => "History";
        public override string HelpTopic => "topics/options#queries-history";
        public override string Help      => "Recording of executed SQL statements to a local history database, how long that history is kept, and — on the Restoring queries tab — how the queries open when SSMS closed are restored on the next start.";

        protected override void OnSectionBuilt(IPageBuilder section, IPageControls controls)
        {
            if (controls is HistoryControls history)
                _restore.MasterSwitch = history.Enabled;
        }
    }

    /// <summary>
    /// Inserted code: three tabs — Statements (INSERT/EXEC expansion with schema qualification,
    /// bracket policy and function parentheses), Aliases, and Closing characters.
    /// </summary>
    internal sealed class InsertedCodePage : TabbedPage
    {
        public InsertedCodePage(IPageBuilder statements, IPageBuilder qualification, IPageBuilder aliases, IPageBuilder specialCharacters)
            : base(("Statements", new[] { statements, qualification }), ("Aliases", new[] { aliases }), ("Closing characters", new[] { specialCharacters })) { }

        public override string Key       => "InsertedCode";
        public override string Display   => "Inserted code";
        public override string Title     => "Inserted code";
        public override string HelpTopic => "topics/options#inserted-code";
        public override string Help      => "How code inserted from suggestions is written: INSERT and EXEC expansion, schema prefixes, brackets and function parentheses on the Statements tab; table aliases; and the closing characters added as you type.";
    }

    /// <summary>
    /// Queries › Execution &amp; warnings: two tabs — Results &amp; execution (the grid and
    /// execution sections) and Warnings (the execution warnings and the transaction reminder,
    /// once under Suggestions).
    /// </summary>
    internal sealed class ResultsExecutionPage : TabbedPage
    {
        public ResultsExecutionPage(IPageBuilder results, IPageBuilder execution, IPageBuilder warnings)
            : base(("Results & execution", new[] { results, execution }), ("Warnings", new[] { warnings })) { }

        public override string Key       => "ResultsExecution";
        public override string Display   => "Queries › Execution & warnings";
        public override string Title     => "Execution & warnings";
        public override string HelpTopic => "topics/options#queries-results-execution";
        public override string Help      => "The results grid — statistics, NULL highlighting, row numbers, Excel export — and query execution: the timer, multi-database mode and the long-running notification. The Warnings tab holds the confirmations before risky statements run and the open-transaction reminder.";
    }

    /// <summary>Editor: productivity aids, rename scope and where the navigation commands are.</summary>
    internal sealed class EditorAllPage : CombinedPage
    {
        public EditorAllPage(IPageBuilder productivity, IPageBuilder refactoring, IPageBuilder navigation)
            : base(productivity, refactoring, navigation) { }

        public override string Key       => "EditorAll";
        public override string Display   => "Editor";
        public override string Title     => "Editor";
        public override string HelpTopic => "topics/options#editor";
        public override string Help      => "Editor aids — occurrence highlighting, bracket matching, sticky scroll, the minimap — what Safe Rename includes, and where the navigation commands are.";
    }
}
