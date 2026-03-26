#nullable enable
using System;
using System.Windows.Media;
using AkmlSql.Core.Config;
using AkmlSql.Core.Models.Tabs;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Tabs
{
    /// <summary>
    /// Monitors document window activations and applies background colour and environment
    /// labels to WPF document tab headers based on the connected server environment.
    /// <para>
    /// Colour mapping is driven by <see cref="EnvironmentDetector"/> which evaluates
    /// <see cref="ColoringRule"/> entries from <c>config.json</c>.
    /// </para>
    /// </summary>
    internal static class TabColoringManager
    {
        private static DTE2? _dte;
        private static WindowEvents? _windowEvents;
        private static bool _initialized;

        /// <summary>
        /// Subscribes to DTE window activation events. Must be called on the UI thread
        /// during package initialization.
        /// </summary>
        /// <param name="package">The async package instance (used to retrieve DTE).</param>
        public static void Initialize(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_initialized) return;

            try
            {
                // Guard: ensure coloring is enabled before subscribing to events
                var settings = ConfigManager.Load();
                if (!settings.Tabs.ColoringEnabled)
                {
                    Log.Information("TabColoringManager: tab coloring is disabled, skipping initialization");
                    return;
                }

                _dte = (DTE2?)Package.GetGlobalService(typeof(DTE));
                if (_dte == null)
                {
                    Log.Warning("TabColoringManager: DTE service not available");
                    return;
                }

                // Keep a strong reference to WindowEvents so it is not garbage-collected.
                _windowEvents = _dte.Events.WindowEvents;
                _windowEvents.WindowActivated += OnWindowActivated;

                _initialized = true;
                Log.Information("TabColoringManager: initialized and listening for window activations");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "TabColoringManager: failed to initialize");
            }
        }

        /// <summary>
        /// Fired whenever a VS/SSMS window receives focus. If the window is a document
        /// window, we detect the connected server and apply tab colouring.
        /// </summary>
        private static void OnWindowActivated(Window gotFocus, Window lostFocus)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // Only process document windows (Kind == "Document").
                if (gotFocus == null || gotFocus.Kind != "Document")
                    return;

                // Guard: check if coloring is enabled.
                var settings = ConfigManager.Load();
                if (!settings.Tabs.ColoringEnabled)
                {
                    ClearTabColor(gotFocus);
                    return;
                }

                // Detect the connected server name from the active connection context.
                var serverName = GetActiveServerName(gotFocus);
                if (string.IsNullOrWhiteSpace(serverName))
                {
                    ClearTabColor(gotFocus);
                    return;
                }

                // Match against environment rules.
                var rule = EnvironmentDetector.Match(serverName);
                if (rule != null)
                {
                    ApplyTabColor(gotFocus, rule);
                }
                else
                {
                    ClearTabColor(gotFocus);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "TabColoringManager: error processing window activation");
            }
        }

        /// <summary>
        /// Attempts to retrieve the connected SQL Server instance name from the active
        /// document's connection context.
        /// </summary>
        /// <remarks>
        /// In SSMS, the connection context is typically available through the document's
        /// <c>IVsWindowFrame</c> properties or via SSMS-specific service interfaces.
        /// This is a best-effort extraction that works across VS and SSMS hosts.
        /// </remarks>
        private static string? GetActiveServerName(Window window)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // Approach 1: Try to get the server name from the window caption.
                // SSMS window captions are typically in the format: "Query - ServerName.DatabaseName"
                // or "SQLQuery1.sql - ServerName.DatabaseName - UserName"
                var caption = window.Caption;
                if (!string.IsNullOrEmpty(caption))
                {
                    var serverName = ParseServerNameFromCaption(caption);
                    if (!string.IsNullOrEmpty(serverName))
                        return serverName;
                }

                // TODO: Approach 2: SSMS-specific connection context retrieval.
                // In SSMS, connection info can be obtained through:
                //   - IVsWindowFrame -> GetProperty(WindowFrameProperties) for SSMS connection data
                //   - ScriptFactory.GetCurrentlyActiveFrameInfo() (SSMS internal API)
                //   - UIConnectionInfo from the SSMS ServiceProvider
                // These APIs vary between SSMS 20/21/22 and require SSMS-specific assembly references.
                // Implementation will be completed when SSMS connection service integration is available.

                // TODO: Approach 3: For VS (non-SSMS), check SQL Server Object Explorer connection
                // context via IVsDataConnection or similar VS data services.
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TabColoringManager: could not extract server name from window");
            }

            return null;
        }

        /// <summary>
        /// Parses a server name from an SSMS-style window caption.
        /// Expected formats:
        /// <list type="bullet">
        ///   <item><c>"SQLQuery1.sql - SERVERNAME.master - sa"</c></item>
        ///   <item><c>"Query - SERVERNAME.DatabaseName"</c></item>
        ///   <item><c>"SERVERNAME.DatabaseName - SQLQuery1.sql"</c></item>
        /// </list>
        /// </summary>
        private static string? ParseServerNameFromCaption(string caption)
        {
            if (string.IsNullOrEmpty(caption))
                return null;

            // SSMS typically uses " - " as a delimiter in captions.
            var parts = caption.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                var trimmed = part.Trim();

                // Look for segments containing a dot that look like "ServerName.DatabaseName".
                // Skip segments that end with common file extensions.
                if (trimmed.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    continue;

                var dotIndex = trimmed.IndexOf('.');
                if (dotIndex > 0 && dotIndex < trimmed.Length - 1)
                {
                    // Extract the part before the first dot as the server name.
                    // For "SERVER.database.windows.net.master", we want "SERVER.database.windows.net"
                    // For "SERVERNAME.master", we want "SERVERNAME"
                    // Heuristic: if the segment after the last dot looks like a database name
                    // (doesn't contain dots itself after splitting), take everything before the last dot.
                    var lastDot = trimmed.LastIndexOf('.');
                    var possibleServer = trimmed.Substring(0, lastDot);
                    if (!string.IsNullOrWhiteSpace(possibleServer))
                        return possibleServer;
                }
            }

            return null;
        }

        /// <summary>
        /// Applies the environment rule's colour to the document tab header via WPF visual tree walking.
        /// </summary>
        private static void ApplyTabColor(Window window, EnvironmentRule rule)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // Convert hex color string to WPF brush.
                var brush = CreateBrushFromHex(rule.Color);
                if (brush == null) return;

                // TODO: Walk the WPF visual tree to find the tab header for this document window.
                //
                // The exact WPF element hierarchy varies between VS 2019/2022/2026 and SSMS 20/21/22.
                // General approach:
                //   1. Get the IVsWindowFrame for this window
                //   2. Get the WPF FrameworkElement from the frame
                //   3. Walk up the visual tree to find the DocumentTabItem / TabItem
                //   4. Set the Background property on the tab header
                //   5. Optionally add/update a TextBlock for the environment label
                //
                // Known element types in VS shell:
                //   - Microsoft.VisualStudio.PlatformUI.Shell.Controls.DocumentTabItem (VS 2022+)
                //   - Microsoft.VisualStudio.PlatformUI.TabItem (VS 2019)
                //
                // For SSMS-specific tab headers, the type hierarchy may differ.
                // This requires runtime discovery via GetType().Name checks rather than
                // compile-time type references, since the shell assembly types differ per target.

                Log.Debug("TabColoringManager: would apply color {Color} ({Label}) to tab for {Caption}",
                    rule.Color, rule.Label, window.Caption);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "TabColoringManager: failed to apply tab color");
            }
        }

        /// <summary>
        /// Removes any previously applied environment colour from a document tab.
        /// </summary>
        private static void ClearTabColor(Window window)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // TODO: Walk the WPF visual tree to find and reset the tab header for this window.
                // Reset Background to the default theme brush and remove any environment label TextBlock.
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TabColoringManager: failed to clear tab color");
            }
        }

        /// <summary>
        /// Converts a hex colour string (e.g. <c>"#FF4444"</c>) to a frozen <see cref="SolidColorBrush"/>.
        /// Returns <c>null</c> if parsing fails.
        /// </summary>
        private static SolidColorBrush? CreateBrushFromHex(string hex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex))
                    return null;

                // Ensure the hex string starts with '#'.
                if (!hex.StartsWith("#", StringComparison.Ordinal))
                    hex = "#" + hex;

                var color = (Color)ColorConverter.ConvertFromString(hex);

                // Use semi-transparent for tab backgrounds so text remains readable.
                var tabColor = Color.FromArgb(60, color.R, color.G, color.B);

                var brush = new SolidColorBrush(tabColor);
                brush.Freeze(); // Thread-safe after freezing.
                return brush;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "TabColoringManager: invalid hex color '{Hex}'", hex);
                return null;
            }
        }
    }
}
