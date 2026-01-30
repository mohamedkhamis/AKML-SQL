using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AKML.SQL.Shared;
using Microsoft.VisualStudio.Text.Editor;
using Serilog;

namespace AKML.SQL.SSMS.Services
{
    /// <summary>
    /// Manages database connections across multiple query tabs.
    /// Implements Story 4.1: SSMS Connection Context Extraction.
    /// Coordinates connection tracking, tab switches, and metadata refresh.
    /// </summary>
    public class ConnectionManager : IDisposable
    {
        private static readonly ILogger _logger = Log.Logger;
        private static ConnectionManager? _instance;
        private static readonly object _lock = new object();

        private readonly ConnectionExtractor _connectionExtractor;
        private readonly ConcurrentDictionary<string, TabConnectionState> _tabStates;
        private string? _activeDocumentId;
        private bool _disposed;

        /// <summary>
        /// Gets the singleton instance of the ConnectionManager.
        /// </summary>
        public static ConnectionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new ConnectionManager();
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Event raised when the active connection changes.
        /// </summary>
        public event EventHandler<ActiveConnectionChangedEventArgs>? ActiveConnectionChanged;

        /// <summary>
        /// Event raised when metadata should be refreshed for a connection.
        /// </summary>
        public event EventHandler<MetadataRefreshRequestedEventArgs>? MetadataRefreshRequested;

        private ConnectionManager()
        {
            _connectionExtractor = ConnectionExtractor.Instance;
            _tabStates = new ConcurrentDictionary<string, TabConnectionState>(StringComparer.OrdinalIgnoreCase);

            // Subscribe to connection changes
            _connectionExtractor.ConnectionChanged += OnConnectionChanged;

            _logger.Debug("[ConnectionManager] Initialized");
        }

        /// <summary>
        /// Gets the active document's connection context.
        /// </summary>
        public ConnectionContext? ActiveConnection
        {
            get
            {
                if (_activeDocumentId == null) return null;
                return _connectionExtractor.GetConnection(_activeDocumentId);
            }
        }

        /// <summary>
        /// Gets the active document ID.
        /// </summary>
        public string? ActiveDocumentId => _activeDocumentId;

        /// <summary>
        /// Called when a document becomes active (tab switch).
        /// </summary>
        public async Task OnDocumentActivatedAsync(IWpfTextView textView)
        {
            if (_disposed) return;

            var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var documentId = GetDocumentId(textView);

            _logger.Debug("[{CorrelationId}] Document activated: {DocumentId}", correlationId, documentId);

            var previousDocumentId = _activeDocumentId;
            var previousConnection = ActiveConnection?.Clone();

            _activeDocumentId = documentId;

            // Extract or get cached connection
            var connection = await _connectionExtractor.ExtractConnectionAsync(textView);

            // Update tab state
            var state = GetOrCreateTabState(documentId);
            state.LastActivated = DateTime.UtcNow;
            state.Connection = connection;

            // Check if connection changed
            var connectionChanged = HasConnectionChanged(previousConnection, connection);

            if (connectionChanged)
            {
                _logger.Information("[{CorrelationId}] Active connection changed: {Server}/{Database}",
                    correlationId, connection?.ServerName, connection?.DatabaseName);

                ActiveConnectionChanged?.Invoke(this, new ActiveConnectionChangedEventArgs(
                    previousDocumentId, _activeDocumentId, previousConnection, connection));

                // Request metadata refresh if connected to a new server/database
                if (connection?.IsConnected == true)
                {
                    RequestMetadataRefresh(connection, correlationId);
                }
            }
        }

        /// <summary>
        /// Called when a document is closed.
        /// </summary>
        public void OnDocumentClosed(string documentId)
        {
            if (_disposed) return;

            _logger.Debug("[ConnectionManager] Document closed: {DocumentId}", documentId);

            _tabStates.TryRemove(documentId, out _);
            _connectionExtractor.RemoveDocument(documentId);

            if (_activeDocumentId == documentId)
            {
                _activeDocumentId = null;
            }
        }

        /// <summary>
        /// Called when text changes in a document (to detect USE statements).
        /// </summary>
        public void OnTextChanged(string documentId, string text, int changePosition)
        {
            if (_disposed) return;

            // Detect database change via USE statement
            _connectionExtractor.DetectDatabaseChange(documentId, text, changePosition);
        }

        /// <summary>
        /// Gets the connection for a specific document.
        /// </summary>
        public ConnectionContext? GetConnectionForDocument(string documentId)
        {
            return _connectionExtractor.GetConnection(documentId);
        }

        /// <summary>
        /// Gets statistics about managed connections.
        /// </summary>
        public ConnectionManagerStatistics GetStatistics()
        {
            var stats = new ConnectionManagerStatistics
            {
                TotalTrackedDocuments = _tabStates.Count,
                ActiveDocumentId = _activeDocumentId
            };

            foreach (var state in _tabStates.Values)
            {
                if (state.Connection?.IsConnected == true)
                {
                    stats.ConnectedDocuments++;

                    var key = state.Connection.CacheKey;
                    if (!stats.UniqueConnections.Contains(key))
                    {
                        stats.UniqueConnections.Add(key);
                    }
                }
            }

            return stats;
        }

        /// <summary>
        /// Forces a connection refresh for the active document.
        /// </summary>
        public async Task RefreshActiveConnectionAsync(IWpfTextView textView)
        {
            if (_disposed) return;

            var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            _logger.Information("[{CorrelationId}] Forcing connection refresh", correlationId);

            // Clear cached connection
            var documentId = GetDocumentId(textView);
            _connectionExtractor.RemoveDocument(documentId);

            // Re-extract
            await OnDocumentActivatedAsync(textView);
        }

        private void OnConnectionChanged(object? sender, ConnectionChangedEventArgs e)
        {
            var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            _logger.Debug("[{CorrelationId}] Connection changed event for: {DocumentId}", correlationId, e.DocumentId);

            // Update tab state
            if (_tabStates.TryGetValue(e.DocumentId, out var state))
            {
                state.Connection = e.CurrentConnection;
                state.LastConnectionChange = DateTime.UtcNow;
            }

            // If this is the active document, propagate the change
            if (e.DocumentId == _activeDocumentId)
            {
                ActiveConnectionChanged?.Invoke(this, new ActiveConnectionChangedEventArgs(
                    e.DocumentId, e.DocumentId, e.PreviousConnection, e.CurrentConnection));

                // Request metadata refresh
                if (e.CurrentConnection?.IsConnected == true)
                {
                    RequestMetadataRefresh(e.CurrentConnection, correlationId);
                }
            }
        }

        private void RequestMetadataRefresh(ConnectionContext connection, string correlationId)
        {
            _logger.Debug("[{CorrelationId}] Requesting metadata refresh for: {Server}/{Database}",
                correlationId, connection.ServerName, connection.DatabaseName);

            MetadataRefreshRequested?.Invoke(this, new MetadataRefreshRequestedEventArgs(
                connection.CacheKey, connection));
        }

        private bool HasConnectionChanged(ConnectionContext? previous, ConnectionContext? current)
        {
            if (previous == null && current == null) return false;
            if (previous == null || current == null) return true;

            return !string.Equals(previous.ServerName, current.ServerName, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(previous.DatabaseName, current.DatabaseName, StringComparison.OrdinalIgnoreCase);
        }

        private TabConnectionState GetOrCreateTabState(string documentId)
        {
            return _tabStates.GetOrAdd(documentId, id => new TabConnectionState(id));
        }

        private string GetDocumentId(IWpfTextView textView)
        {
            if (textView.TextBuffer.Properties.TryGetProperty(typeof(Microsoft.VisualStudio.Text.ITextDocument),
                out Microsoft.VisualStudio.Text.ITextDocument document) &&
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

            _connectionExtractor.ConnectionChanged -= OnConnectionChanged;
            _tabStates.Clear();

            _logger.Debug("[ConnectionManager] Disposed");
        }
    }

    /// <summary>
    /// State for a single tab/document.
    /// </summary>
    internal class TabConnectionState
    {
        public TabConnectionState(string documentId)
        {
            DocumentId = documentId;
            CreatedAt = DateTime.UtcNow;
        }

        public string DocumentId { get; }
        public ConnectionContext? Connection { get; set; }
        public DateTime CreatedAt { get; }
        public DateTime? LastActivated { get; set; }
        public DateTime? LastConnectionChange { get; set; }
    }

    /// <summary>
    /// Event args for active connection change.
    /// </summary>
    public class ActiveConnectionChangedEventArgs : EventArgs
    {
        public ActiveConnectionChangedEventArgs(
            string? previousDocumentId, string? currentDocumentId,
            ConnectionContext? previousConnection, ConnectionContext? currentConnection)
        {
            PreviousDocumentId = previousDocumentId;
            CurrentDocumentId = currentDocumentId;
            PreviousConnection = previousConnection;
            CurrentConnection = currentConnection;
        }

        public string? PreviousDocumentId { get; }
        public string? CurrentDocumentId { get; }
        public ConnectionContext? PreviousConnection { get; }
        public ConnectionContext? CurrentConnection { get; }
    }

    /// <summary>
    /// Event args for metadata refresh requests.
    /// </summary>
    public class MetadataRefreshRequestedEventArgs : EventArgs
    {
        public MetadataRefreshRequestedEventArgs(string cacheKey, ConnectionContext connection)
        {
            CacheKey = cacheKey;
            Connection = connection;
        }

        public string CacheKey { get; }
        public ConnectionContext Connection { get; }
    }

    /// <summary>
    /// Statistics about managed connections.
    /// </summary>
    public class ConnectionManagerStatistics
    {
        public int TotalTrackedDocuments { get; set; }
        public int ConnectedDocuments { get; set; }
        public string? ActiveDocumentId { get; set; }
        public System.Collections.Generic.HashSet<string> UniqueConnections { get; } = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}
