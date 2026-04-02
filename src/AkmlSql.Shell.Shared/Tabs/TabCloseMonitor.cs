#nullable enable
using System;
using AkmlSql.Core.Config;
using AkmlSql.Core.Models.Tabs;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Tabs
{
    /// <summary>
    /// Monitors document close events via DTE and captures the content and connection info
    /// of each closed editor tab into the <see cref="ClosedTabStack"/>.
    /// <para>
    /// Must be initialized on the UI thread during package startup.
    /// </para>
    /// </summary>
    internal static class TabCloseMonitor
    {
        private static DTE? _dte;
        private static DocumentEvents? _documentEvents;
        private static bool _initialized;

        /// <summary>
        /// Subscribes to <see cref="DocumentEvents.DocumentClosing"/> via the DTE automation model.
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
                    Log.Warning("TabCloseMonitor: DTE service unavailable; document close tracking disabled");
                    return;
                }

                // Load max capacity from settings
                try
                {
                    var settings = ConfigManager.Load();
                    var maxTabs = settings.Tabs?.MaxClosedTabs ?? 20;
                    ClosedTabStack.Instance.SetMaxCapacity(maxTabs);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "TabCloseMonitor: failed to load MaxClosedTabs setting; using default");
                }

                // Hold a strong reference to DocumentEvents to prevent GC
                _documentEvents = _dte.Events.DocumentEvents;
                _documentEvents.DocumentClosing += OnDocumentClosing;

                _initialized = true;
                Log.Information("TabCloseMonitor: initialized, listening for document close events");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "TabCloseMonitor: failed to initialize");
            }
        }

        /// <summary>
        /// Unsubscribes from document close events. Called during package disposal.
        /// </summary>
        public static void Shutdown()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_documentEvents != null)
            {
                try
                {
                    _documentEvents.DocumentClosing -= OnDocumentClosing;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "TabCloseMonitor: error detaching event handler");
                }

                _documentEvents = null;
            }

            _dte = null;
            _initialized = false;
        }

        /// <summary>
        /// Handles the DocumentClosing event. Captures the document content and pushes
        /// a <see cref="ClosedTabEntry"/> onto the <see cref="ClosedTabStack"/>.
        /// </summary>
        private static void OnDocumentClosing(Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (document == null) return;

                // Get the document content via the TextDocument interface
                var textDocument = document.Object("TextDocument") as TextDocument;
                if (textDocument == null)
                {
                    Log.Debug("TabCloseMonitor: document '{Name}' has no TextDocument; skipping",
                        document.Name);
                    return;
                }

                var editPoint = textDocument.StartPoint.CreateEditPoint();
                var content = editPoint.GetText(textDocument.EndPoint);

                // Skip empty documents
                if (string.IsNullOrWhiteSpace(content))
                {
                    Log.Debug("TabCloseMonitor: document '{Name}' is empty; skipping", document.Name);
                    return;
                }

                // Extract connection info if available (SSMS-specific properties)
                string? server = null;
                string? database = null;
                string? authType = null;

                try
                {
                    // SSMS documents may expose connection properties
                    var props = document.ProjectItem?.Properties;
                    if (props != null)
                    {
                        server = TryGetProperty(props, "Server");
                        database = TryGetProperty(props, "Database");
                        authType = TryGetProperty(props, "AuthenticationType");
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "TabCloseMonitor: unable to read connection properties for '{Name}'",
                        document.Name);
                }

                var entry = new ClosedTabEntry(
                    content: content,
                    filePath: document.FullName,
                    server: server,
                    database: database,
                    authType: authType,
                    closedAt: DateTime.UtcNow,
                    tabTitle: document.Name
                );

                ClosedTabStack.Instance.Push(entry);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "TabCloseMonitor: error capturing closed document");
            }
        }

        /// <summary>
        /// Safely reads a named property from a DTE Properties collection.
        /// Returns <c>null</c> if the property does not exist or cannot be read.
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
                // Property does not exist — expected for non-SSMS or non-connected documents
                return null;
            }
        }
    }
}
