using System;
using System.Collections.Concurrent;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AKML.SQL.Shared;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using Serilog;

namespace AKML.SQL.SSMS.Services
{
    /// <summary>
    /// Listens to SQL editor text views and synchronizes content to the Core service.
    /// Implements IVsTextViewCreationListener for automatic registration with VS editor.
    /// </summary>
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType("SQL Server Tools")]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    public class TextSynchronizer : IVsTextViewCreationListener, IDisposable
    {
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, DocumentSyncContext> _documents;
        private bool _disposed;

        [Import]
        internal IVsEditorAdaptersFactoryService? EditorAdaptersFactory { get; set; }

        /// <summary>
        /// Gets the singleton instance of the TextSynchronizer.
        /// </summary>
        public static TextSynchronizer? Instance { get; private set; }

        public TextSynchronizer()
        {
            _logger = Log.Logger;
            _documents = new ConcurrentDictionary<string, DocumentSyncContext>();
            Instance = this;
            _logger.Information("TextSynchronizer initialized");
        }

        /// <summary>
        /// Called when a new text view is created in the editor.
        /// </summary>
        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            if (EditorAdaptersFactory == null)
            {
                _logger.Warning("EditorAdaptersFactory is not available");
                return;
            }

            var textView = EditorAdaptersFactory.GetWpfTextView(textViewAdapter);
            if (textView == null)
            {
                _logger.Debug("Could not get WPF text view from adapter");
                return;
            }

            // Check if this is a SQL document
            var contentType = textView.TextBuffer.ContentType;
            if (!IsSqlContentType(contentType))
            {
                return;
            }

            // Create document context and start tracking
            var documentId = GenerateDocumentId(textView);
            var context = new DocumentSyncContext(documentId, textView, _logger);

            if (_documents.TryAdd(documentId, context))
            {
                _logger.Information("Started tracking document: {DocumentId}", documentId);
                context.StartTracking();
            }
        }

        private static bool IsSqlContentType(IContentType contentType)
        {
            // Check for SQL-related content types
            return contentType.IsOfType("SQL Server Tools") ||
                   contentType.IsOfType("SQL") ||
                   contentType.TypeName.Contains("SQL", StringComparison.OrdinalIgnoreCase);
        }

        private static string GenerateDocumentId(IWpfTextView textView)
        {
            // Try to get file path, fall back to GUID
            if (textView.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document) &&
                !string.IsNullOrEmpty(document.FilePath))
            {
                return document.FilePath;
            }

            return $"untitled-{Guid.NewGuid():N}";
        }

        /// <summary>
        /// Removes tracking for a document.
        /// </summary>
        public void StopTracking(string documentId)
        {
            if (_documents.TryRemove(documentId, out var context))
            {
                context.Dispose();
                _logger.Information("Stopped tracking document: {DocumentId}", documentId);
            }
        }

        /// <summary>
        /// Gets all currently tracked document IDs.
        /// </summary>
        public string[] GetTrackedDocuments()
        {
            return _documents.Keys.ToArray();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var context in _documents.Values)
            {
                context.Dispose();
            }
            _documents.Clear();

            _logger.Information("TextSynchronizer disposed");
        }
    }

    /// <summary>
    /// Tracks synchronization state for a single document.
    /// Implements debouncing to prevent excessive IPC traffic.
    /// </summary>
    internal class DocumentSyncContext : IDisposable
    {
        private readonly string _documentId;
        private readonly IWpfTextView _textView;
        private readonly ILogger _logger;
        private readonly object _syncLock = new object();

        private CancellationTokenSource? _debounceCts;
        private int _version;
        private DateTime _lastSyncTime;
        private bool _disposed;

        public DocumentSyncContext(string documentId, IWpfTextView textView, ILogger logger)
        {
            _documentId = documentId;
            _textView = textView;
            _logger = logger;
            _version = 0;
            _lastSyncTime = DateTime.MinValue;
        }

        /// <summary>
        /// Start listening to text buffer and caret events.
        /// </summary>
        public void StartTracking()
        {
            _textView.TextBuffer.Changed += OnTextBufferChanged;
            _textView.Caret.PositionChanged += OnCaretPositionChanged;
            _textView.Closed += OnTextViewClosed;

            // Initial sync
            ScheduleSync();
        }

        private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            if (_disposed) return;

            Interlocked.Increment(ref _version);
            ScheduleSync();
        }

        private void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            if (_disposed) return;

            // Only sync on caret change if we haven't synced recently (within debounce window)
            var timeSinceLastSync = DateTime.UtcNow - _lastSyncTime;
            if (timeSinceLastSync.TotalMilliseconds > AkmlConstants.Timeouts.TextSyncDebounce)
            {
                ScheduleSync();
            }
        }

        private void OnTextViewClosed(object sender, EventArgs e)
        {
            TextSynchronizer.Instance?.StopTracking(_documentId);
        }

        /// <summary>
        /// Schedule a debounced sync to the Core service.
        /// </summary>
        private void ScheduleSync()
        {
            lock (_syncLock)
            {
                // Cancel any pending sync
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = new CancellationTokenSource();

                var token = _debounceCts.Token;
                var currentVersion = _version;

                // Schedule sync after debounce delay (fire-and-forget, errors are logged)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(AkmlConstants.Timeouts.TextSyncDebounce, token);

                        if (!token.IsCancellationRequested)
                        {
                            await SyncToCorAsync(currentVersion, token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Debounce cancelled, a newer sync is scheduled
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "Error syncing document {DocumentId}", _documentId);
                    }
                });
            }
        }

        /// <summary>
        /// Perform the actual synchronization to Core service.
        /// </summary>
        private async Task SyncToCorAsync(int version, CancellationToken cancellationToken)
        {
            // Check if we have a newer version pending
            if (version < _version)
            {
                _logger.Debug("Skipping stale sync for {DocumentId} (version {OldVersion} < {NewVersion})",
                    _documentId, version, _version);
                return;
            }

            var grpcClient = AkmlSqlPackage.Instance?.GrpcClient;
            if (grpcClient == null)
            {
                _logger.Debug("gRPC client not available, skipping sync");
                return;
            }

            try
            {
                // Get current text and cursor position (must be on UI thread for some operations)
                string text;
                int cursorLine, cursorColumn;
                string? connectionString = null;

                var snapshot = _textView.TextSnapshot;
                text = snapshot.GetText();

                var caretPosition = _textView.Caret.Position.BufferPosition;
                var line = snapshot.GetLineFromPosition(caretPosition.Position);
                cursorLine = line.LineNumber;
                cursorColumn = caretPosition.Position - line.Start.Position;

                // Sync to Core
                var result = await grpcClient.SyncTextAsync(
                    _documentId,
                    text,
                    cursorLine,
                    cursorColumn,
                    connectionString,
                    cancellationToken);

                if (result.Success)
                {
                    _lastSyncTime = DateTime.UtcNow;
                    _logger.Debug("Synced document {DocumentId} (version {Version}, {Length} chars)",
                        _documentId, version, text.Length);
                }
                else
                {
                    _logger.Warning("Sync failed for document {DocumentId}", _documentId);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error syncing document {DocumentId}", _documentId);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _textView.TextBuffer.Changed -= OnTextBufferChanged;
            _textView.Caret.PositionChanged -= OnCaretPositionChanged;
            _textView.Closed -= OnTextViewClosed;

            lock (_syncLock)
            {
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = null;
            }
        }
    }
}
