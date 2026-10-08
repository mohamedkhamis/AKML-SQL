#nullable enable
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
            new CombinedControls(Sections.Select(s => (s.Key, s.Build(panel, ctx))).ToList());
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

    /// <summary>Suggestions › Behavior: completion behaviour, the definition box and join conditions.</summary>
    internal sealed class SuggestionsBehaviorPage : CombinedPage
    {
        public SuggestionsBehaviorPage(IPageBuilder intelliSense, IPageBuilder tooltips, IPageBuilder joinConditions)
            : base(intelliSense, tooltips, joinConditions) { }

        public override string Key       => "SuggestionsBehavior";
        public override string Display   => "Suggestions › Behavior";
        public override string Title     => "Behavior";
        public override string HelpTopic => "topics/options#suggestions-behavior";
        public override string Help      => "How the suggestions box behaves: when it opens, what it shows beside each item, the definition box, FK-assisted JOINs and join conditions, aliases and commit keys.";
    }

    /// <summary>Suggestions › Lists &amp; connections: what is listed, from where, and snippets.</summary>
    internal sealed class SuggestionsListsPage : CombinedPage
    {
        public SuggestionsListsPage(IPageBuilder types, IPageBuilder connections, IPageBuilder credentials, IPageBuilder snippets)
            : base(types, connections, credentials, snippets) { }

        public override string Key       => "SuggestionsLists";
        public override string Display   => "Suggestions › Lists & connections";
        public override string Title     => "Lists & connections";
        public override string HelpTopic => "topics/options#suggestions-lists-connections";
        public override string Help      => "What the suggestions box lists and where it comes from: system objects, keywords and column scope; the databases, schemas and linked servers it draws on; SQL Server-auth connections; and snippets.";
    }

    /// <summary>Inserted code: how code inserted from suggestions is written.</summary>
    internal sealed class InsertedCodePage : CombinedPage
    {
        public InsertedCodePage(IPageBuilder statements, IPageBuilder qualification, IPageBuilder aliases, IPageBuilder specialCharacters)
            : base(statements, qualification, aliases, specialCharacters) { }

        public override string Key       => "InsertedCode";
        public override string Display   => "Inserted code";
        public override string Title     => "Inserted code";
        public override string HelpTopic => "topics/options#inserted-code";
        public override string Help      => "How code inserted from suggestions is written: INSERT and EXEC expansion, schema prefixes, table aliases, and the brackets, parentheses and closing characters added as you type.";
    }

    /// <summary>Queries › Results &amp; execution: the results grid and query execution.</summary>
    internal sealed class ResultsExecutionPage : CombinedPage
    {
        public ResultsExecutionPage(IPageBuilder results, IPageBuilder execution)
            : base(results, execution) { }

        public override string Key       => "ResultsExecution";
        public override string Display   => "Queries › Results & execution";
        public override string Title     => "Results & execution";
        public override string HelpTopic => "topics/options#queries-results-execution";
        public override string Help      => "The results grid — statistics, NULL highlighting, row numbers, Excel export — and query execution: the timer, multi-database mode and the long-running notification.";
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
