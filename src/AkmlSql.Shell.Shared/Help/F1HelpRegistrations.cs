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
    /// <see cref="EnsureInitialized"/> is idempotent and is called once from each
    /// host's <c>AkmlSqlPackage.Initialize</c> after <c>LoggerFactory.Initialize</c>.
    /// Calling it again does no harm — <see cref="F1HelpListener.Register"/> is
    /// itself idempotent.
    /// </para>
    /// </summary>
    internal static class F1HelpRegistrations
    {
        // 0 = not initialized, 1 = initialized. Interlocked.CompareExchange so that
        // multiple package-init paths (e.g. SSMS 22 + VS 2022 in the same process,
        // never happens in practice but is theoretically possible) do not re-register.
        private static int _initialized;

        /// <summary>
        /// Register every Phase 10 UI surface's help context key on the given
        /// listener instance. Safe to call multiple times — the underlying
        /// registry is idempotent and the <see cref="_initialized"/> latch
        /// short-circuits subsequent calls.
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

            // Surfaces shipped before Phase 10 (US1 safety dialog, schema progress,
            // history, snippets, profile editor, etc.) are registered by their
            // respective spec phases — not duplicated here.

            // ── Phase 10 / spec 019 user-story registrations ────────────────────
            // URLs are absolute https GitHub URLs. F1HelpListener.Open() calls
            // Process.Start({UseShellExecute=true}) which resolves the path against
            // the host's CWD — for SSMS that's the Release\Common7\IDE\ install
            // directory, where "doc/..." would not resolve. Absolute https URLs
            // route through the system browser regardless of CWD.

            // US2 — Column Picker + Wildcard-Tab
            listener.Register("akmlsql.completion.column-picker", DocUrl("SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_Core.md"));
            listener.Register("akmlsql.completion.wildcard-tab", DocUrl("SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_Core.md"));

            // US3 — Code Analysis Issues window + lightbulb popup
            listener.Register("akmlsql.window.analysis-issues", DocUrl("analysis-rules.md"));
            listener.Register("akmlsql.popup.lightbulb-details", DocUrl("analysis-rules.md"));

            // US4 — Right-click tab color
            listener.Register("akmlsql.menu.tab-color", DocUrl("SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_Core.md"));

            // US6 — Command Palette (4-source aggregation)
            listener.Register("akmlsql.window.command-palette", DocUrl("SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_Core.md"));

            // US7 — Script nav + Browse Open Tabs + F1 help
            listener.Register("akmlsql.window.summarize-script", DocUrl("SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_Core.md"));
            listener.Register("akmlsql.window.find-unused-variables", DocUrl("SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_Core.md"));
            listener.Register("akmlsql.popup.browse-open-tabs", DocUrl("SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_Core.md"));

            // US8 — Find Invalid Objects
            listener.Register("akmlsql.window.find-invalid-objects", DocUrl("SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_Core.md"));

            // US10 — Smart Rename dialog
            listener.Register("akmlsql.dialog.smart-rename", DocUrl("SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_Core.md"));

            // US12 — Theme refresh + Options Dialog Phase 3
            listener.Register("akmlsql.dialog.environment-color-editor", DocUrl("SQL-PROMPT/SQL-Prompt-Option/SQL_Prompt_Options_Dialog.md"));
            listener.Register("akmlsql.editor.profile-3col", DocUrl("formatting.md"));

            // US13 — AI feature surfaces
            listener.Register("akmlsql.window.ai-history", DocUrl("SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_AI.md"));
            listener.Register("akmlsql.adornment.ai-selection-icon", DocUrl("SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_AI.md"));
        }

        // Base for every doc URL. Branch defaults to `master` per the existing
        // example in F1HelpListener's class docstring. Switch to a tag (e.g.,
        // `v1.0`) once a release exists to make URLs immutable.
        private const string DocBase = "https://github.com/mohamedkhamis/AKML-SQL/blob/master/doc/";

        private static string DocUrl(string relativePath) => DocBase + relativePath;
    }
}
