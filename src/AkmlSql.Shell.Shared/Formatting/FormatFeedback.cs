#nullable enable
using AkmlSql.Shell.Shared.StatusBar;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Spec 040 (T109, STY-09, FR-034) — tells the user which style Format Document or Format
    /// Selection used: "Formatted with 'X'" in the status bar for a few seconds. When the style
    /// could not be loaded (the engine fell back to its defaults) the user is warned once instead,
    /// and nothing claims the style was applied. A failed format reports neither; its own notice
    /// (<see cref="FormatFailureNotifier.NotifyIfPreservedAsync"/>) covers it.
    /// </summary>
    internal static class FormatFeedback
    {
        internal const int StatusSeconds = 4;

        /// <summary>Call on the UI thread, after the formatted text was applied (or found unchanged).</summary>
        internal static void Report(string? profileName, bool success, string? fallbackWarning)
        {
            if (!success) return;

            if (!string.IsNullOrWhiteSpace(fallbackWarning))
            {
                FormatFailureNotifier.NotifyProfileFallbackOnce(fallbackWarning);
                return;
            }

            StatusBarManager.ShowTransient(
                string.IsNullOrWhiteSpace(profileName) ? "Formatted with the default style" : $"Formatted with '{profileName}'",
                StatusSeconds);
        }
    }
}
