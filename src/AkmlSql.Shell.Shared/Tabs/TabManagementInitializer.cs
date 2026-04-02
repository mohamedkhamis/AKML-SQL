#nullable enable
using System;
using AkmlSql.Core.Config;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Tabs
{
    /// <summary>
    /// Entry point for Phase 7 tab management features. Initializes tab coloring and
    /// tooltip enrichment when the settings allow.
    /// <para>
    /// Call <see cref="Initialize"/> from the package's <c>InitializeAsync</c> method
    /// after switching to the UI thread, e.g.:
    /// <code>
    /// await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
    /// // ... existing command registrations ...
    /// TabManagementInitializer.Initialize(this);
    /// </code>
    /// </para>
    /// </summary>
    internal static class TabManagementInitializer
    {
        private static bool _initialized;

        /// <summary>
        /// Initializes all tab management sub-systems. Must be called on the UI thread.
        /// Safe to call multiple times — subsequent calls are no-ops.
        /// </summary>
        /// <param name="package">The <see cref="AsyncPackage"/> hosting the extension.</param>
        public static void Initialize(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_initialized) return;
            _initialized = true;

            try
            {
                var settings = ConfigManager.Load();

                // ── Tab Coloring (T052) ──────────────────────────────────────────
                if (settings.Tabs.ColoringEnabled)
                {
                    // Load environment rules from config (T051).
                    EnvironmentDetector.Initialize();

                    // Subscribe to window activation events and apply tab colours (T052).
                    TabColoringManager.Initialize(package);
                }
                else
                {
                    Log.Information("TabManagementInitializer: tab coloring is disabled");
                }

                // ── Tab Tooltips (T053) ──────────────────────────────────────────
                // Tooltip enrichment is always active when tabs feature is used,
                // independent of the coloring toggle.
                TabTooltipProvider.Initialize(package);

                // ── Closed Tab Tracking (T075 — US6) ─────────────────────────────
                // Subscribes to document close events to capture content for
                // later restoration via RestoreClosedTabCommand.
                TabCloseMonitor.Initialize(package);

                // ── Custom Window Title (T091 — US10) ────────────────────────────
                // Replaces SSMS window title with tokens ({server}, {database}, etc.)
                if (!string.IsNullOrWhiteSpace(settings.Tabs.CustomWindowTitle))
                {
                    WindowTitleManager.Initialize(package);
                }

                Log.Information("TabManagementInitializer: initialization complete");
            }
            catch (Exception ex)
            {
                // Non-critical — do not prevent the rest of the package from loading.
                Log.Error(ex, "TabManagementInitializer: initialization failed");
            }
        }
    }
}
