#nullable enable
using System;
using AkmlSql.Core.Config;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Tabs
{
    /// <summary>
    /// Manages the SSMS/VS main window title, replacing tokens like <c>{server}</c>,
    /// <c>{database}</c>, and <c>{user}</c> with actual connection information from the
    /// active document.
    /// <para>
    /// Token format (from <see cref="TabSettings.CustomWindowTitle"/>):
    /// <list type="bullet">
    ///   <item><c>{server}</c> — current SQL Server instance name</item>
    ///   <item><c>{database}</c> — current database context</item>
    ///   <item><c>{user}</c> — current login user name</item>
    ///   <item><c>{file}</c> — active document file name</item>
    /// </list>
    /// </para>
    /// </summary>
    internal static class WindowTitleManager
    {
        private static DTE? _dte;
        private static WindowEvents? _windowEvents;
        private static DocumentEvents? _documentEvents;
        private static string _titleTemplate = "{server} - {database} - SSMS";
        private static string? _originalCaption;
        private static bool _initialized;

        /// <summary>
        /// Initializes the window title manager and subscribes to document/window change events.
        /// Must be called on the UI thread.
        /// </summary>
        /// <param name="package">The hosting <see cref="AsyncPackage"/>.</param>
        public static void Initialize(Package package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_initialized) return;

            try
            {
                _dte = (DTE)Package.GetGlobalService(typeof(DTE));
                if (_dte == null)
                {
                    Log.Warning("WindowTitleManager: DTE service unavailable");
                    return;
                }

                // Save the original caption for restoration on shutdown
                _originalCaption = _dte.MainWindow.Caption;

                // Load the title template from settings
                try
                {
                    var settings = ConfigManager.Load();
                    var template = settings.Tabs?.CustomWindowTitle;
                    if (!string.IsNullOrWhiteSpace(template))
                    {
                        _titleTemplate = template;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "WindowTitleManager: failed to load CustomWindowTitle setting");
                }

                // Subscribe to events that indicate a document or window change
                _windowEvents = _dte.Events.WindowEvents;
                _windowEvents.WindowActivated += OnWindowActivated;

                _documentEvents = _dte.Events.DocumentEvents;
                _documentEvents.DocumentOpened += OnDocumentOpened;

                _initialized = true;
                Log.Information("WindowTitleManager: initialized with template '{Template}'", _titleTemplate);

                // Apply initial title
                UpdateTitle();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "WindowTitleManager: failed to initialize");
            }
        }

        /// <summary>
        /// Restores the original window caption and unsubscribes from events.
        /// </summary>
        public static void Shutdown()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (_windowEvents != null)
                {
                    _windowEvents.WindowActivated -= OnWindowActivated;
                    _windowEvents = null;
                }

                if (_documentEvents != null)
                {
                    _documentEvents.DocumentOpened -= OnDocumentOpened;
                    _documentEvents = null;
                }

                // Restore original caption
                if (_dte?.MainWindow != null && _originalCaption != null)
                {
                    _dte.MainWindow.Caption = _originalCaption;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "WindowTitleManager: error during shutdown");
            }

            _dte = null;
            _initialized = false;
        }

        /// <summary>
        /// Manually triggers a title update. Can be called when connection info changes.
        /// Must be called on the UI thread.
        /// </summary>
        public static void Refresh()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            UpdateTitle();
        }

        // ----- Event handlers -----

        private static void OnWindowActivated(Window gotFocus, Window lostFocus)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            UpdateTitle();
        }

        private static void OnDocumentOpened(Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            UpdateTitle();
        }

        // ----- Core logic -----

        /// <summary>
        /// Reads the current connection context and applies the title template.
        /// </summary>
        private static void UpdateTitle()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (_dte?.MainWindow == null) return;

                var server = "(not connected)";
                var database = "(none)";
                var user = "";
                var fileName = "";

                // Try to extract connection info from the active document
                try
                {
                    var activeDoc = _dte.ActiveDocument;
                    if (activeDoc != null)
                    {
                        fileName = activeDoc.Name ?? "";

                        // Try to get connection properties from SSMS document
                        var props = activeDoc.ProjectItem?.Properties;
                        if (props != null)
                        {
                            var srv = TryGetProperty(props, "Server");
                            if (!string.IsNullOrEmpty(srv)) server = srv!;

                            var db = TryGetProperty(props, "Database");
                            if (!string.IsNullOrEmpty(db)) database = db!;

                            var usr = TryGetProperty(props, "UserName");
                            if (!string.IsNullOrEmpty(usr)) user = usr!;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "WindowTitleManager: unable to read active document properties");
                }

                // Replace tokens in the template (case-insensitive)
                var title = ReplaceTokenIgnoreCase(_titleTemplate, "{server}", server);
                title = ReplaceTokenIgnoreCase(title, "{database}", database);
                title = ReplaceTokenIgnoreCase(title, "{user}", user);
                title = ReplaceTokenIgnoreCase(title, "{file}", fileName);

                // Only update if the title actually changed
                if (!string.Equals(_dte.MainWindow.Caption, title, StringComparison.Ordinal))
                {
                    _dte.MainWindow.Caption = title;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "WindowTitleManager: failed to update window title");
            }
        }

        /// <summary>
        /// Safely reads a named property from a DTE Properties collection.
        /// </summary>
        private static string? TryGetProperty(Properties properties, string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var prop = properties.Item(name);
                var value = prop?.Value?.ToString();
                return string.IsNullOrEmpty(value) ? null : value;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Case-insensitive token replacement compatible with .NET Framework 4.7.2.
        /// </summary>
        private static string ReplaceTokenIgnoreCase(string source, string token, string replacement)
        {
            var idx = source.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return source;

            return source.Substring(0, idx) + replacement + source.Substring(idx + token.Length);
        }
    }
}
