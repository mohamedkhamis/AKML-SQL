#nullable enable
using System;
using System.ComponentModel.Design;
using System.Threading;
using AkmlSql.Core.Config;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// T064: Shared <c>BeforeQueryStatus</c> handler for all AI commands.
    /// Hides AI menu items when AI is disabled or privacy mode is "disabled".
    /// Uses a timer-based cache to avoid reading config from disk on every query status poll.
    /// </summary>
    internal static class AiCommandVisibility
    {
        /// <summary>Cache duration in milliseconds (5 seconds).</summary>
        private const int CacheDurationMs = 5000;

        /// <summary>Cached visibility result.</summary>
        private static bool _cachedVisible;

        /// <summary>Timestamp (ticks) when the cache was last refreshed.</summary>
        private static long _cacheTimestamp;

        /// <summary>
        /// <c>BeforeQueryStatus</c> handler that shows or hides the command based on
        /// <see cref="AiSettings.Enabled"/> and <see cref="AiSettings.PrivacyMode"/>.
        /// Results are cached for <see cref="CacheDurationMs"/> to avoid disk I/O on each poll.
        /// </summary>
        public static void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            if (sender is OleMenuCommand cmd)
            {
                cmd.Visible = IsAiVisible();
            }
        }

        /// <summary>
        /// Returns <c>true</c> if AI commands should be visible, using a cached setting.
        /// </summary>
        private static bool IsAiVisible()
        {
            var now = Environment.TickCount;
            var cached = Interlocked.Read(ref _cacheTimestamp);

            // Check if cache is still valid (within CacheDurationMs)
            if (cached != 0 && Math.Abs(now - cached) < CacheDurationMs)
            {
                return _cachedVisible;
            }

            // Refresh the cache
            bool visible;
            try
            {
                var settings = ConfigManager.Load();
                visible = settings.Ai.Enabled &&
                          !string.Equals(settings.Ai.PrivacyMode, "disabled", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "AiCommandVisibility: failed to load settings, hiding AI commands");
                visible = false;
            }

            _cachedVisible = visible;
            Interlocked.Exchange(ref _cacheTimestamp, now);
            return visible;
        }

        /// <summary>
        /// Invalidates the cached visibility state, forcing a re-read on next query.
        /// Call this when AI settings change (e.g., after the options dialog closes).
        /// </summary>
        public static void InvalidateCache()
        {
            Interlocked.Exchange(ref _cacheTimestamp, 0);
        }
    }
}
