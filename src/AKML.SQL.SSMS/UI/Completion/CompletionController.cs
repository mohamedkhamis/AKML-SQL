using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AKML.SQL.Shared;
using AKML.SQL.Shared.Contracts;
using AKML.SQL.SSMS.UI.Settings;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using Serilog;

namespace AKML.SQL.SSMS.UI.Completion
{
    /// <summary>
    /// Controls IntelliSense completion popup display and keyboard handling.
    /// Implements Story 3.2: Keyboard Navigation &amp; Completion Commit.
    /// </summary>
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType("SQL Server Tools")]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    public class CompletionController : IVsTextViewCreationListener
    {
        private static readonly ILogger _logger = Log.Logger;

        [Import]
        internal IVsEditorAdaptersFactoryService EditorAdaptersFactory { get; set; }

        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            if (EditorAdaptersFactory == null) return;

            var textView = EditorAdaptersFactory.GetWpfTextView(textViewAdapter);
            if (textView == null) return;

            // Check if this is a SQL document
            var contentType = textView.TextBuffer.ContentType;
            if (!IsSqlContentType(contentType)) return;

            // Attach completion handler to this view
            var handler = new CompletionCommandHandler(textView, _logger);
            textView.Properties.GetOrCreateSingletonProperty(() => handler);

            _logger.Debug("[CompletionController] Attached to text view: {ContentType}", contentType.TypeName);
        }

        private static bool IsSqlContentType(IContentType contentType)
        {
            return contentType.IsOfType("SQL Server Tools") ||
                   contentType.IsOfType("SQL") ||
                   contentType.TypeName.Contains("SQL", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Handles keyboard commands for completion in a specific text view.
    /// </summary>
    internal class CompletionCommandHandler : IDisposable
    {
        private readonly IWpfTextView _textView;
        private readonly ILogger _logger;
        private CompletionPopup _popup;
        private CompletionSession _session;
        private bool _disposed;

        // Trigger characters that invoke completion
        private static readonly char[] TriggerCharacters = { '.', ' ' };

        // Characters that dismiss the popup
        private static readonly char[] DismissCharacters = { ';', '(', ')', ',', '\n', '\r' };

        public CompletionCommandHandler(IWpfTextView textView, ILogger logger)
        {
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));
            _logger = logger;

            // Subscribe to keyboard events
            _textView.VisualElement.PreviewKeyDown += OnPreviewKeyDown;
            _textView.VisualElement.PreviewTextInput += OnPreviewTextInput;
            _textView.Closed += OnTextViewClosed;

            _logger.Debug("[CompletionHandler] Initialized for text view");
        }

        /// <summary>
        /// Gets whether a completion session is currently active.
        /// </summary>
        public bool IsSessionActive => _session != null && _popup != null && _popup.IsVisible;

        /// <summary>
        /// Triggers completion at the current cursor position.
        /// </summary>
        public async Task TriggerCompletionAsync(string triggerCharacter = null)
        {
            if (_disposed) return;

            // Check if IntelliSense is enabled in settings
            var settings = SettingsService.Instance?.Settings;
            if (settings != null && !settings.EnableIntelliSense)
            {
                return;
            }

            var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            _logger.Debug("[{CorrelationId}] TriggerCompletion. TriggerChar: '{TriggerChar}'",
                correlationId, triggerCharacter ?? "manual");

            try
            {
                // Get current position and text
                var caretPosition = _textView.Caret.Position.BufferPosition;
                var snapshot = _textView.TextSnapshot;
                var text = snapshot.GetText();
                var line = snapshot.GetLineFromPosition(caretPosition.Position);
                var lineNumber = line.LineNumber;
                var column = caretPosition.Position - line.Start.Position;

                // Get document ID
                var documentId = GetDocumentId();

                // Get completions from Core service
                var grpcClient = AkmlSqlPackage.Instance?.GrpcClient;
                if (grpcClient == null)
                {
                    _logger.Warning("[{CorrelationId}] gRPC client not available", correlationId);
                    return;
                }

                var maxItems = settings?.MaxCompletionItems ?? 50;
                var response = await grpcClient.GetCompletionsAsync(
                    documentId,
                    text,
                    lineNumber,
                    column,
                    triggerCharacter,
                    null, // connectionString
                    maxItems,
                    CancellationToken.None);

                if (!response.Success || response.Items.Count == 0)
                {
                    _logger.Debug("[{CorrelationId}] No completions returned", correlationId);
                    DismissCompletion();
                    return;
                }

                // Calculate popup position
                var position = GetCaretScreenPosition();

                // Show popup with items
                ShowCompletionPopup(response.Items.ToList(), position, response.FilterText);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[{CorrelationId}] Error triggering completion", correlationId);
            }
        }

        /// <summary>
        /// Dismisses any active completion session.
        /// </summary>
        public void DismissCompletion()
        {
            if (_popup != null && _popup.IsVisible)
            {
                _popup.Dismiss();
            }

            _session = null;
        }

        private void ShowCompletionPopup(System.Collections.Generic.List<CompletionItem> items, Point position, string filterText)
        {
            // Create or reuse popup
            if (_popup == null)
            {
                _popup = new CompletionPopup();
                _popup.ItemCommitted += OnItemCommitted;
                _popup.Dismissed += OnPopupDismissed;
            }

            // Create new session
            _session = new CompletionSession
            {
                StartPosition = _textView.Caret.Position.BufferPosition.Position,
                FilterText = filterText ?? string.Empty
            };

            // Set items and show
            _popup.SetItems(items);
            _popup.UpdateFilter(_session.FilterText);
            _popup.ShowAt(position);

            _logger.Debug("[CompletionHandler] Showing popup with {Count} items at ({X}, {Y})",
                items.Count, position.X, position.Y);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!IsSessionActive) return;

            // Let the popup handle navigation keys
            if (_popup.HandleKeyDown(e.Key, Keyboard.Modifiers))
            {
                e.Handled = true;
            }
        }

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;

            var c = e.Text[0];

            // Check if auto-complete is enabled in settings
            var settings = SettingsService.Instance?.Settings;
            var autoCompleteEnabled = settings?.EnableAutoComplete ?? true;
            var autoCompleteDelay = settings?.AutoCompleteDelay ?? 150;

            // Check if should trigger completion
            if (!IsSessionActive && TriggerCharacters.Contains(c) && autoCompleteEnabled)
            {
                // Delay trigger to allow character to be inserted first
                _ = Task.Run(async () =>
                {
                    await Task.Delay(autoCompleteDelay > 50 ? 50 : autoCompleteDelay);
                    await _textView.VisualElement.Dispatcher.InvokeAsync(async () =>
                    {
                        await TriggerCompletionAsync(c.ToString());
                    });
                });
                return;
            }

            // Handle active session
            if (IsSessionActive)
            {
                if (_popup.ShouldDismissOnChar(c))
                {
                    DismissCompletion();
                    return;
                }

                // Update filter with new character
                if (_session != null)
                {
                    _session.FilterText += c;
                    _popup.UpdateFilter(_session.FilterText);
                }
            }
        }

        private void OnItemCommitted(object sender, CompletionCommitEventArgs e)
        {
            if (_session == null) return;

            _logger.Information("[CompletionHandler] Committing: '{InsertText}', replacing filter: '{Filter}'",
                e.InsertText, e.FilterText);

            try
            {
                // Calculate the span to replace (from session start to current position)
                var startPosition = _session.StartPosition;
                var currentPosition = _textView.Caret.Position.BufferPosition.Position;

                // The filter text was typed after the trigger, so we replace from start
                var filterLength = e.FilterText?.Length ?? 0;
                var replaceStart = currentPosition - filterLength;
                var replaceLength = filterLength;

                if (replaceStart < 0) replaceStart = currentPosition;
                if (replaceLength < 0) replaceLength = 0;

                // Perform the text replacement
                var snapshot = _textView.TextSnapshot;
                var span = new Span(replaceStart, replaceLength);

                using (var edit = _textView.TextBuffer.CreateEdit())
                {
                    edit.Replace(span, e.InsertText);
                    edit.Apply();
                }

                _logger.Debug("[CompletionHandler] Replaced span [{Start}, {Length}] with '{Text}'",
                    replaceStart, replaceLength, e.InsertText);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CompletionHandler] Error committing completion");
            }
            finally
            {
                _session = null;
            }
        }

        private void OnPopupDismissed(object sender, EventArgs e)
        {
            _session = null;
        }

        private Point GetCaretScreenPosition()
        {
            try
            {
                var caretPosition = _textView.Caret.Position.BufferPosition;
                var textViewLine = _textView.GetTextViewLineContainingBufferPosition(caretPosition);

                if (textViewLine != null)
                {
                    var bounds = textViewLine.GetCharacterBounds(caretPosition);
                    var visualElement = _textView.VisualElement;

                    // Convert to screen coordinates
                    var point = new Point(bounds.Left, bounds.Bottom);
                    return visualElement.PointToScreen(point);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "[CompletionHandler] Error getting caret position");
            }

            // Fallback to mouse position
            return System.Windows.Forms.Control.MousePosition.ToWpfPoint();
        }

        private string GetDocumentId()
        {
            if (_textView.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document) &&
                !string.IsNullOrEmpty(document.FilePath))
            {
                return document.FilePath;
            }

            return $"untitled-{_textView.GetHashCode():X8}";
        }

        private void OnTextViewClosed(object sender, EventArgs e)
        {
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _textView.VisualElement.PreviewKeyDown -= OnPreviewKeyDown;
            _textView.VisualElement.PreviewTextInput -= OnPreviewTextInput;
            _textView.Closed -= OnTextViewClosed;

            _popup?.Close();
            _popup = null;
            _session = null;

            _logger.Debug("[CompletionHandler] Disposed");
        }
    }

    /// <summary>
    /// Represents an active completion session.
    /// </summary>
    internal class CompletionSession
    {
        public int StartPosition { get; set; }
        public string FilterText { get; set; }
    }

    /// <summary>
    /// Extension methods for point conversion.
    /// </summary>
    internal static class PointExtensions
    {
        public static Point ToWpfPoint(this System.Drawing.Point point)
        {
            return new Point(point.X, point.Y);
        }
    }
}
