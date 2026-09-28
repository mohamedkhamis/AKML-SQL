#nullable enable
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Snippets
{
    /// <summary>
    /// Spec 040 (OPT-01) — the snippet switches in Options, decided in one place:
    /// Snippets › "Enable snippets" is the master switch, and Suggestions › Behavior "Show
    /// snippets in the completion list" decides whether snippets appear in the suggestions box.
    /// </summary>
    internal static class SnippetGate
    {
        /// <summary>Snippet items appear in the suggestions box.</summary>
        public static bool ShouldOfferSnippets(AppSettings s) =>
            s.Snippets.Enabled && s.IntelliSense.SnippetsInCompletion;

        /// <summary>Typing a snippet shortcut and pressing Tab expands it.</summary>
        public static bool ExpansionEnabled(AppSettings s) => s.Snippets.Enabled;

        /// <summary>An expanded snippet is formatted with the active style.</summary>
        public static bool FormatOnExpand(AppSettings s) => s.Snippets.FormatOnExpand;
    }
}
