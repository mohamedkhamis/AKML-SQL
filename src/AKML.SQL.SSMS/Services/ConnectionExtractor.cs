using System;
using System.Collections.Concurrent;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using AKML.SQL.Shared;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Serilog;

namespace AKML.SQL.SSMS.Services
{
    /// <summary>
    /// Extracts connection information from SSMS query windows.
    /// Implements Story 4.1: SSMS Connection Context Extraction.
    /// </summary>
    public class ConnectionExtractor : IDisposable
    {
        private static readonly ILogger _logger = Log.Logger;
        private static ConnectionExtractor? _instance;
        private static readonly object _lock = new object();

        private readonly ConcurrentDictionary<string, ConnectionContext> _documentConnections;
        private readonly ConcurrentDictionary<string, DateTime> _lastConnectionCheck;
        private bool _disposed;

        /// <summary>
        /// Gets the singleton instance of the ConnectionExtractor.
        /// </summary>
        public static ConnectionExtractor Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new ConnectionExtractor();
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Event raised when connection changes for a document.
        /// </summary>
        public event EventHandler<ConnectionChangedEventArgs>? ConnectionChanged;

        private ConnectionExtractor()
        {
            _documentConnections = new ConcurrentDictionary<string, ConnectionContext>(StringComparer.OrdinalIgnoreCase);
            _lastConnectionCheck = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            _logger.Debug("[ConnectionExtractor] Initialized");
        }

        /// <summary>
        /// Extracts connection info for a text view.
        /// </summary>
        /// <param name="textView">The text view to extract connection from.</param>
        /// <returns>The connection context, or null if not connected.</returns>
        public async Task<ConnectionContext?> ExtractConnectionAsync(IWpfTextView textView)
        {
            if (_disposed) return null;

            var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);

            try
            {
                var documentId = GetDocumentId(textView);
                _logger.Debug("[{CorrelationId}] Extracting connection for document: {DocumentId}",
                    correlationId, documentId);

                // Check if we have a recent cached connection
                if (TryGetCachedConnection(documentId, out var cachedContext))
                {
                    _logger.Debug("[{CorrelationId}] Using cached connection: {Server}/{Database}",
                        correlationId, cachedContext?.ServerName, cachedContext?.DatabaseName);
                    return cachedContext;
                }

                // Extract connection from SSMS
                var connection = await ExtractFromSsmsAsync(textView, correlationId);

                if (connection != null)
                {
                    // Cache the connection
                    var previousConnection = _documentConnections.TryGetValue(documentId, out var prev) ? prev : null;
                    _documentConnections[documentId] = connection;
                    _lastConnectionCheck[documentId] = DateTime.UtcNow;

                    // Raise event if connection changed
                    if (HasConnectionChanged(previousConnection, connection))
                    {
                        _logger.Information("[{CorrelationId}] Connection changed for {DocumentId}: {Server}/{Database}",
                            correlationId, documentId, connection.ServerName, connection.DatabaseName);

                        ConnectionChanged?.Invoke(this, new ConnectionChangedEventArgs(
                            documentId, previousConnection, connection));
                    }
                }

                return connection;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[{CorrelationId}] Error extracting connection", correlationId);
                return null;
            }
        }

        /// <summary>
        /// Gets the cached connection for a document.
        /// </summary>
        public ConnectionContext? GetConnection(string documentId)
        {
            return _documentConnections.TryGetValue(documentId, out var context) ? context : null;
        }

        /// <summary>
        /// Updates the database context after a USE statement.
        /// </summary>
        public void UpdateDatabaseContext(string documentId, string newDatabase)
        {
            if (_documentConnections.TryGetValue(documentId, out var context))
            {
                var previousContext = context.Clone();
                context.DatabaseName = newDatabase;

                _logger.Information("[ConnectionExtractor] Database changed for {DocumentId}: {OldDb} -> {NewDb}",
                    documentId, previousContext.DatabaseName, newDatabase);

                ConnectionChanged?.Invoke(this, new ConnectionChangedEventArgs(
                    documentId, previousContext, context));
            }
        }

        /// <summary>
        /// Removes connection tracking for a document.
        /// </summary>
        public void RemoveDocument(string documentId)
        {
            _documentConnections.TryRemove(documentId, out _);
            _lastConnectionCheck.TryRemove(documentId, out _);
            _logger.Debug("[ConnectionExtractor] Removed document: {DocumentId}", documentId);
        }

        /// <summary>
        /// Detects USE statement in text and updates context.
        /// </summary>
        public void DetectDatabaseChange(string documentId, string text, int changePosition)
        {
            // Look for USE statement near the change position
            var usePattern = "USE ";
            var startSearch = Math.Max(0, changePosition - 100);
            var endSearch = Math.Min(text.Length, changePosition + 100);
            var searchArea = text.Substring(startSearch, endSearch - startSearch);

            var useIndex = searchArea.IndexOf(usePattern, StringComparison.OrdinalIgnoreCase);
            if (useIndex >= 0)
            {
                var afterUse = searchArea.Substring(useIndex + usePattern.Length).TrimStart();
                var endOfDbName = afterUse.IndexOfAny(new[] { ' ', '\r', '\n', ';', '[' });
                if (endOfDbName < 0) endOfDbName = afterUse.Length;

                var dbName = afterUse.Substring(0, endOfDbName).Trim('[', ']', ' ');

                if (!string.IsNullOrEmpty(dbName))
                {
                    // Check if this is different from current
                    if (_documentConnections.TryGetValue(documentId, out var context) &&
                        !string.Equals(context.DatabaseName, dbName, StringComparison.OrdinalIgnoreCase))
                    {
                        UpdateDatabaseContext(documentId, dbName);
                    }
                }
            }
        }

        private async Task<ConnectionContext?> ExtractFromSsmsAsync(IWpfTextView textView, string correlationId)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // Try to get connection from SSMS via service provider
                // SSMS uses Microsoft.SqlServer.Management.QueryExecution for connection management
                var context = new ConnectionContext();

                // Method 1: Try to get from document properties
                if (textView.TextBuffer.Properties.TryGetProperty("ConnectionInfo", out object connInfo) &&
                    connInfo != null)
                {
                    ExtractFromConnectionInfo(connInfo, context);
                    if (context.IsConnected)
                    {
                        _logger.Debug("[{CorrelationId}] Extracted connection from buffer properties", correlationId);
                        return context;
                    }
                }

                // Method 2: Try to get from SSMS ServiceProvider
                var serviceProvider = AkmlSqlPackage.Instance;
                if (serviceProvider != null)
                {
                    var connection = await TryExtractFromServiceProviderAsync(serviceProvider, textView, correlationId);
                    if (connection != null)
                        return connection;
                }

                // Method 3: Parse connection from editor title/path
                var documentId = GetDocumentId(textView);
                if (TryParseConnectionFromDocumentId(documentId, context))
                {
                    _logger.Debug("[{CorrelationId}] Parsed connection from document ID", correlationId);
                    return context;
                }

                // Not connected
                _logger.Debug("[{CorrelationId}] No connection found for document", correlationId);
                return CreateDisconnectedContext();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "[{CorrelationId}] Error extracting from SSMS", correlationId);
                return CreateDisconnectedContext();
            }
        }

        private void ExtractFromConnectionInfo(object connInfo, ConnectionContext context)
        {
            // Use reflection to extract properties from SSMS connection info objects
            var type = connInfo.GetType();

            try
            {
                // Try common property names used by SSMS
                var serverProp = type.GetProperty("ServerName") ?? type.GetProperty("Server") ?? type.GetProperty("DataSource");
                var dbProp = type.GetProperty("DatabaseName") ?? type.GetProperty("Database") ?? type.GetProperty("InitialCatalog");
                var userProp = type.GetProperty("UserName") ?? type.GetProperty("UserId");
                var intSecProp = type.GetProperty("IntegratedSecurity") ?? type.GetProperty("UseIntegratedSecurity");

                if (serverProp != null)
                    context.ServerName = serverProp.GetValue(connInfo)?.ToString() ?? string.Empty;

                if (dbProp != null)
                    context.DatabaseName = dbProp.GetValue(connInfo)?.ToString() ?? string.Empty;

                if (userProp != null)
                    context.UserName = userProp.GetValue(connInfo)?.ToString();

                if (intSecProp != null)
                    context.IntegratedSecurity = (bool?)intSecProp.GetValue(connInfo) ?? false;

                if (!string.IsNullOrEmpty(context.ServerName))
                {
                    context.IsConnected = true;
                    context.LastConnected = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "[ConnectionExtractor] Error extracting from connection info object");
            }
        }

        private async Task<ConnectionContext?> TryExtractFromServiceProviderAsync(
            AsyncPackage serviceProvider, IWpfTextView textView, string correlationId)
        {
            try
            {
                // Try to get SSMS-specific services
                // Note: These types are from SSMS assemblies and may not be directly available
                // This is a fallback mechanism

                // Try IVsRunningDocumentTable to get document info
                var rdt = await serviceProvider.GetServiceAsync(typeof(Microsoft.VisualStudio.Shell.Interop.SVsRunningDocumentTable));
                if (rdt != null)
                {
                    // Document table can give us file path and potentially connection info
                    _logger.Debug("[{CorrelationId}] Got running document table service", correlationId);
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "[{CorrelationId}] Could not extract from service provider", correlationId);
                return null;
            }
        }

        private bool TryParseConnectionFromDocumentId(string documentId, ConnectionContext context)
        {
            // SSMS often includes server/database in document paths
            // Format examples:
            // - "SQLQuery1.sql - SERVER.database - user"
            // - "C:\...\query.sql"
            // - "untitled-XXXXXXXX"

            if (documentId.Contains(" - ") && documentId.Contains("."))
            {
                var parts = documentId.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var serverDb = parts[1];
                    var dotIndex = serverDb.IndexOf('.');
                    if (dotIndex > 0)
                    {
                        context.ServerName = serverDb.Substring(0, dotIndex);
                        context.DatabaseName = serverDb.Substring(dotIndex + 1);
                        context.IsConnected = true;
                        context.LastConnected = DateTime.UtcNow;
                        return true;
                    }
                }
            }

            return false;
        }

        private ConnectionContext CreateDisconnectedContext()
        {
            return new ConnectionContext
            {
                IsConnected = false,
                ServerName = string.Empty,
                DatabaseName = string.Empty
            };
        }

        private bool TryGetCachedConnection(string documentId, out ConnectionContext? context)
        {
            context = null;

            if (!_documentConnections.TryGetValue(documentId, out var cached))
                return false;

            if (!_lastConnectionCheck.TryGetValue(documentId, out var lastCheck))
                return false;

            // Cache is valid for 30 seconds
            if (DateTime.UtcNow - lastCheck < TimeSpan.FromSeconds(30))
            {
                context = cached;
                return true;
            }

            return false;
        }

        private bool HasConnectionChanged(ConnectionContext? previous, ConnectionContext? current)
        {
            if (previous == null && current == null) return false;
            if (previous == null || current == null) return true;

            return !string.Equals(previous.ServerName, current.ServerName, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(previous.DatabaseName, current.DatabaseName, StringComparison.OrdinalIgnoreCase);
        }

        private string GetDocumentId(IWpfTextView textView)
        {
            if (textView.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document) &&
                !string.IsNullOrEmpty(document.FilePath))
            {
                return document.FilePath;
            }

            return $"untitled-{textView.GetHashCode():X8}";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _documentConnections.Clear();
            _lastConnectionCheck.Clear();

            _logger.Debug("[ConnectionExtractor] Disposed");
        }
    }

    /// <summary>
    /// Represents connection context for a query window.
    /// </summary>
    public class ConnectionContext
    {
        public string ServerName { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = string.Empty;
        public string? UserName { get; set; }
        public bool IntegratedSecurity { get; set; } = true;
        public bool IsConnected { get; set; }
        public DateTime? LastConnected { get; set; }
        public string? ConnectionString { get; set; }

        /// <summary>
        /// Gets a cache key for this connection.
        /// </summary>
        public string CacheKey => $"{ServerName}|{DatabaseName}".ToLowerInvariant();

        /// <summary>
        /// Creates a clone of this context.
        /// </summary>
        public ConnectionContext Clone()
        {
            return new ConnectionContext
            {
                ServerName = ServerName,
                DatabaseName = DatabaseName,
                UserName = UserName,
                IntegratedSecurity = IntegratedSecurity,
                IsConnected = IsConnected,
                LastConnected = LastConnected,
                ConnectionString = ConnectionString
            };
        }

        /// <summary>
        /// Builds a connection string from this context.
        /// </summary>
        public string BuildConnectionString()
        {
            if (!string.IsNullOrEmpty(ConnectionString))
                return ConnectionString;

            var builder = new System.Data.SqlClient.SqlConnectionStringBuilder
            {
                DataSource = ServerName,
                InitialCatalog = DatabaseName,
                IntegratedSecurity = IntegratedSecurity,
                ConnectTimeout = 30,
                ApplicationName = AkmlConstants.ProductName
            };

            if (!IntegratedSecurity && !string.IsNullOrEmpty(UserName))
            {
                builder.UserID = UserName;
                // Password should be provided separately or retrieved securely
            }

            return builder.ConnectionString;
        }
    }

    /// <summary>
    /// Event args for connection change events.
    /// </summary>
    public class ConnectionChangedEventArgs : EventArgs
    {
        public ConnectionChangedEventArgs(string documentId, ConnectionContext? previous, ConnectionContext? current)
        {
            DocumentId = documentId;
            PreviousConnection = previous;
            CurrentConnection = current;
        }

        public string DocumentId { get; }
        public ConnectionContext? PreviousConnection { get; }
        public ConnectionContext? CurrentConnection { get; }
    }
}
