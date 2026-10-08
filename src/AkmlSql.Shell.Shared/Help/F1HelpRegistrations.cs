using System.Text.RegularExpressions;
using System.Threading;

namespace AkmlSql.Shell.Shared.Help
{
    /// <summary>
    /// Phase 10 (spec 019) / FR-104 — single registration hub for every AKML SQL
    /// UI surface's F1 help context key and documentation URL. Each user-story
    /// phase that adds a new dialog or tool window appends one
    /// <see cref="F1HelpListener.Register(string,string)"/> call here, so the
    /// list of all surfaces and their help targets is reviewable from one file
    /// instead of scattered across constructors.
    /// <para>
    /// Spec 040 (X-03, FR-062, research R26): every target is a page of the product docs site
    /// (<see cref="DocBase"/> + a slug from <c>doc/</c>, with an optional <c>#anchor</c>). The
    /// site test <c>F1SlugTests</c> reads this file as text and checks that every
    /// <c>topics/…</c> literal names a published document and, when it has one, a real heading
    /// anchor — so keep each topic a plain string literal.
    /// </para>
    /// <para>
    /// <see cref="RegisterAll"/> is idempotent; <see cref="F1HelpListener.Register"/> is itself
    /// idempotent too.
    /// </para>
    /// </summary>
    internal static class F1HelpRegistrations
    {
        /// <summary>Base of every help URL: the product docs site. A topic is appended as-is.</summary>
        internal const string DocBase = "https://akml.khamis.work/docs/";

        /// <summary>F1 target of the Format Styles window (contracts/ui.md §3).</summary>
        internal const string FormatStylesTopic = "topics/formatting#edit-styles-with-live-preview";

        /// <summary>F1 target of the SQL History tool window (contracts/ui.md §3).</summary>
        internal const string SqlHistoryTopic = "topics/sql-history";

        /// <summary>F1 target of the Options window when no page is selected.</summary>
        internal const string OptionsTopic = "topics/options";

        // A docs topic: a lower-case site slug ("topics/options") with an optional anchor
        // ("#suggestions-behavior"). Context keys ("akmlsql.dialog.safety") never match: their
        // dots are not slug characters.
        private static readonly Regex TopicPattern = new Regex(
            @"^[a-z0-9-]+(/[a-z0-9-]+)*(#[a-z0-9_.-]+)?$",
            RegexOptions.CultureInvariant);

        // 0 = not initialized, 1 = initialized. Interlocked.CompareExchange so that
        // multiple package-init paths do not re-register.
        private static int _initialized;

        /// <summary>
        /// Register every UI surface's help context key on the given listener instance. Safe to
        /// call multiple times — the underlying registry is idempotent and the
        /// <see cref="_initialized"/> latch short-circuits subsequent calls.
        /// <para>
        /// IMPORTANT: this method MUST take the listener as a parameter (rather
        /// than reaching through <see cref="F1HelpListener.Default"/>) because
        /// it is invoked from inside <c>F1HelpListener</c>'s type initializer.
        /// Reading <see cref="F1HelpListener.Default"/> at that point returns
        /// the still-null backing field — the assignment happens only after the
        /// initializer returns. Passing the instance explicitly avoids the
        /// static-init cycle.
        /// </para>
        /// </summary>
        public static void RegisterAll(F1HelpListener listener)
        {
            if (listener == null)
            {
                return;
            }
            if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
            {
                return;
            }

            // URLs are absolute https URLs: F1HelpListener.Open() calls
            // Process.Start({UseShellExecute=true}), and a relative path would resolve against the
            // host's working directory (SSMS's Release\Common7\IDE\).

            // Suggestions — Column Picker + Wildcard-Tab
            listener.Register("akmlsql.completion.column-picker", DocUrl("topics/intellisense"));
            listener.Register("akmlsql.completion.wildcard-tab", DocUrl("topics/intellisense"));

            // Code analysis — issues window + lightbulb popup
            listener.Register("akmlsql.window.analysis-issues", DocUrl("topics/static-analysis#where-results-appear"));
            listener.Register("akmlsql.popup.lightbulb-details", DocUrl("topics/static-analysis#turn-a-rule-off"));

            // Execution warnings
            listener.Register("akmlsql.dialog.safety", DocUrl("topics/options#suggestions-warnings-highlighting"));

            // Tab colour — right-click menu + environments editor
            listener.Register("akmlsql.menu.tab-color", DocUrl("topics/options#queries-color"));
            listener.Register("akmlsql.dialog.environment-color-editor", DocUrl("topics/options#queries-color"));

            // Refactoring — Smart Rename dialog
            listener.Register("akmlsql.dialog.smart-rename", DocUrl("topics/refactoring#smart-rename"));

            // Formatting — Format Styles window
            listener.Register("akmlsql.editor.profile-3col", DocUrl(FormatStylesTopic));

            // SQL History tool window
            listener.Register("akmlsql.window.sql-history", DocUrl(SqlHistoryTopic));

            // Options window
            listener.Register("akmlsql.dialog.options", DocUrl(OptionsTopic));

            // AI feature surfaces
            listener.Register("akmlsql.window.ai-history", DocUrl("topics/ai-assistance"));
            listener.Register("akmlsql.adornment.ai-selection-icon", DocUrl("topics/ai-assistance"));
        }

        /// <summary>
        /// The docs URL for <paramref name="topic"/> (a slug with an optional anchor, e.g.
        /// <c>topics/options#general</c>), or <c>null</c> when it is not a docs topic.
        /// </summary>
        internal static string? TopicUrl(string? topic) =>
            topic != null && TopicPattern.IsMatch(topic) ? DocBase + topic : null;

        private static string DocUrl(string topic) => DocBase + topic;
    }
}
