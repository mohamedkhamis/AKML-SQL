#nullable enable
using System;
using System.Text;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Tabs
{
    /// <summary>
    /// Extends document tab tooltips to display connection metadata: server name,
    /// database name, user name, and connection time. Modifies the WPF tooltip of
    /// document tab headers when windows are activated.
    /// </summary>
    internal static class TabTooltipProvider
    {
        private static DTE2? _dte;
        private static WindowEvents? _windowEvents;
        private static bool _initialized;

        /// <summary>
        /// Subscribes to DTE window activation events to update document tab tooltips.
        /// Must be called on the UI thread during package initialization.
        /// </summary>
        /// <param name="package">The async package instance.</param>
        public static void Initialize(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_initialized) return;

            try
            {
                _dte = (DTE2?)Package.GetGlobalService(typeof(DTE));
                if (_dte == null)
                {
                    Log.Warning("TabTooltipProvider: DTE service not available");
                    return;
                }

                // Keep a strong reference so the COM event sink is not garbage-collected.
                _windowEvents = _dte.Events.WindowEvents;
                _windowEvents.WindowActivated += OnWindowActivated;

                _initialized = true;
                Log.Information("TabTooltipProvider: initialized and listening for window activations");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "TabTooltipProvider: failed to initialize");
            }
        }

        /// <summary>
        /// Fired when a VS/SSMS window receives focus. Updates the tooltip for document windows.
        /// </summary>
        private static void OnWindowActivated(EnvDTE.Window gotFocus, EnvDTE.Window lostFocus)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (gotFocus == null || gotFocus.Kind != "Document")
                    return;

                var connectionInfo = GetConnectionInfo(gotFocus);
                if (connectionInfo == null)
                    return;

                ApplyTooltip(gotFocus, connectionInfo);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TabTooltipProvider: error updating tooltip");
            }
        }

        /// <summary>
        /// Retrieves connection metadata for the active document window.
        /// </summary>
        private static ConnectionTooltipInfo? GetConnectionInfo(EnvDTE.Window window)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // Parse connection details from the window caption as a baseline.
                var caption = window.Caption;
                if (string.IsNullOrEmpty(caption))
                    return null;

                var info = new ConnectionTooltipInfo();

                // SSMS caption format: "SQLQuery1.sql - SERVERNAME.DatabaseName - UserName"
                var parts = caption.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var part in parts)
                {
                    var trimmed = part.Trim();

                    // Skip file name segments.
                    if (trimmed.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        info.FileName = trimmed;
                        continue;
                    }

                    // Check for "Server.Database" pattern.
                    var dotIndex = trimmed.IndexOf('.');
                    if (dotIndex > 0 && dotIndex < trimmed.Length - 1 &&
                        string.IsNullOrEmpty(info.ServerName))
                    {
                        var lastDot = trimmed.LastIndexOf('.');
                        info.ServerName   = trimmed.Substring(0, lastDot);
                        info.DatabaseName = trimmed.Substring(lastDot + 1);
                        continue;
                    }

                    // Remaining segment could be the user name.
                    if (string.IsNullOrEmpty(info.UserName) &&
                        !string.IsNullOrWhiteSpace(trimmed))
                    {
                        info.UserName = trimmed;
                    }
                }

                // TODO: SSMS-specific connection context retrieval.
                // In SSMS, richer connection info (exact connect time, authentication type,
                // application name, etc.) can be obtained via:
                //   - ServiceCache.ScriptFactory.GetCurrentlyActiveFrameInfo()
                //   - UIConnectionInfo from the SSMS ServiceProvider
                //   - IVsWindowFrame properties for SSMS connection metadata
                // These APIs are SSMS-internal and require SSMS-specific assembly references.

                // Only return info if we extracted at least a server name.
                return !string.IsNullOrEmpty(info.ServerName) ? info : null;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TabTooltipProvider: could not extract connection info");
                return null;
            }
        }

        /// <summary>
        /// Applies a rich tooltip to the document tab header via WPF visual tree walking.
        /// </summary>
        private static void ApplyTooltip(EnvDTE.Window window, ConnectionTooltipInfo info)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var tooltipText = BuildTooltipText(info);

                // TODO: Walk the WPF visual tree to find the tab header for this document window.
                //
                // General approach:
                //   1. Get the IVsWindowFrame for this window
                //   2. Get the WPF FrameworkElement from the frame
                //   3. Walk up the visual tree to find the DocumentTabItem / TabItem
                //   4. Set the ToolTip property to the formatted connection info string
                //      (or a richer WPF ToolTip with styled TextBlocks for each property)
                //
                // The exact WPF element hierarchy varies between VS/SSMS versions.
                // See TabColoringManager for notes on element type discovery.

                Log.Debug("TabTooltipProvider: would set tooltip for {Caption}: {Tooltip}",
                    window.Caption, tooltipText);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TabTooltipProvider: failed to apply tooltip");
            }
        }

        /// <summary>
        /// Formats connection metadata into a multi-line tooltip string.
        /// </summary>
        private static string BuildTooltipText(ConnectionTooltipInfo info)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(info.FileName))
                sb.AppendLine(info.FileName);

            if (!string.IsNullOrEmpty(info.ServerName))
                sb.Append("Server: ").AppendLine(info.ServerName);

            if (!string.IsNullOrEmpty(info.DatabaseName))
                sb.Append("Database: ").AppendLine(info.DatabaseName);

            if (!string.IsNullOrEmpty(info.UserName))
                sb.Append("User: ").AppendLine(info.UserName);

            if (info.ConnectedAt.HasValue)
                sb.Append("Connected: ").AppendLine(info.ConnectedAt.Value.ToString("yyyy-MM-dd HH:mm:ss"));

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Holds connection metadata extracted from a document window for tooltip display.
        /// </summary>
        private sealed class ConnectionTooltipInfo
        {
            public string? FileName     { get; set; }
            public string? ServerName   { get; set; }
            public string? DatabaseName { get; set; }
            public string? UserName     { get; set; }
            public DateTimeOffset? ConnectedAt { get; set; }
        }
    }
}
