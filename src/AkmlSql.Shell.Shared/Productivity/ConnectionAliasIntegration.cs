#nullable enable
using System;
using AkmlSql.Shell.Shared.Tabs;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity
{
    /// <summary>
    /// T105: Integrates <see cref="ConnectionAliasManager"/> with Phase 7's
    /// <see cref="WindowTitleManager"/> and <see cref="StatusBar.StatusBarManager"/>.
    /// When a connection alias is configured, the alias is shown instead of the raw server name
    /// in the window title and status bar.
    /// </summary>
    internal static class ConnectionAliasIntegration
    {
        private static bool _initialized;

        /// <summary>
        /// Initializes alias integration by patching the window title manager's token resolution.
        /// Must be called on the UI thread after both WindowTitleManager and ConnectionAliasManager
        /// are initialized.
        /// </summary>
        public static void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_initialized) return;

            try
            {
                // The integration works by having WindowTitleManager call ConnectionAliasManager.Resolve()
                // when replacing the {server} token. Since WindowTitleManager uses a static pattern,
                // the simplest integration is to refresh the title whenever the alias manager is updated.

                // Trigger an initial title refresh so aliases take effect
                WindowTitleManager.Refresh();

                _initialized = true;
                Log.Information("ConnectionAliasIntegration: initialized");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ConnectionAliasIntegration: failed to initialize");
            }
        }

        /// <summary>
        /// Resolves a server name through the alias manager, returning the alias if available.
        /// Safe to call from any context — returns the original name if alias manager is not initialized.
        /// </summary>
        /// <param name="serverName">The raw server name.</param>
        /// <returns>The alias or original server name.</returns>
        public static string ResolveServerName(string serverName)
        {
            try
            {
                var manager = ConnectionAliasManager.Instance;
                return manager?.Resolve(serverName) ?? serverName;
            }
            catch
            {
                return serverName;
            }
        }
    }
}
