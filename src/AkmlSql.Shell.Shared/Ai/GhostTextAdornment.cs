#nullable enable
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Ui.Theme;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Serilog;

namespace AkmlSql.Shell.Shared.Ai
{
    /// <summary>
    /// T058 / T059: MEF-managed adornment that displays inline ghost-text (autocomplete) predictions
    /// after the cursor. Subscribes to text changes, debounces requests (300ms), sends
    /// <see cref="AiGhostTextRequest"/> to the engine, and renders the prediction as a
    /// gray semi-transparent <see cref="TextBlock"/> at the cursor position.
    /// Tab accepts the prediction (inserts text); Escape or further typing dismisses it.
    /// Maintains a <see cref="CancellationTokenSource"/> to cancel stale requests (T059).
    /// </summary>
    internal sealed class GhostTextAdornment
    {
        private const string AdornmentLayerName = "AkmlSqlGhostText";
        private const int DebounceMs = 300;
        private const int MaxPrecedingChars = 500;

        private readonly IWpfTextView _view;
        private readonly IAdornmentLayer _layer;
        private readonly DispatcherTimer _debounceTimer;

        /// <summary>Current ghost text prediction displayed in the adornment layer.</summary>
        private string? _currentPrediction;

        /// <summary>The cursor offset at which the current prediction was generated.</summary>
        private int _predictionCursorOffset;

        /// <summary>T059: Cancellation token source for the current ghost text request.</summary>
        private CancellationTokenSource? _currentCts;

        /// <summary>Whether a ghost text visual is currently displayed.</summary>
        private bool _isGhostTextVisible;

        public GhostTextAdornment(IWpfTextView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _layer = view.GetAdornmentLayer(AdornmentLayerName);

            _debounceTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(DebounceMs)
            };
            _debounceTimer.Tick += OnDebounceTimerTick;

            // Subscribe to text buffer changes
            _view.TextBuffer.Changed += OnTextBufferChanged;
            _view.Closed += OnViewClosed;

            // Subscribe to key events for Tab/Escape handling
            _view.VisualElement.PreviewKeyDown += OnPreviewKeyDown;

            Log.Debug("GhostTextAdornment: created for view");
        }

        /// <summary>
        /// Called when text in the buffer changes. Dismisses any current ghost text
        /// and restarts the debounce timer. Also cancels any in-flight request (T059).
        /// </summary>
        private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            // Dismiss current ghost text on any typing
            DismissGhostText();

            // Cancel any pending request (T059)
            CancelCurrentRequest();

            // Restart debounce timer
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        /// <summary>
        /// Fires after the debounce interval. Gets text before cursor and sends a ghost text request.
        /// </summary>
        private void OnDebounceTimerTick(object? sender, EventArgs e)
        {
            _debounceTimer.Stop();

            try
            {
                RequestGhostText();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GhostTextAdornment: debounce tick failed");
            }
        }

        /// <summary>
        /// Extracts preceding text and sends an <see cref="AiGhostTextRequest"/> to the engine.
        /// </summary>
        private void RequestGhostText()
        {
            var manager = EngineLifecycle.Manager;
            if (manager?.Client == null || !manager.Client.IsConnected)
                return;

            var snapshot = _view.TextSnapshot;
            var caretPosition = _view.Caret.Position.BufferPosition;
            var caretOffset = caretPosition.Position;

            if (caretOffset == 0)
                return;

            // Get preceding text (up to MaxPrecedingChars)
            var startOffset = Math.Max(0, caretOffset - MaxPrecedingChars);
            var precedingText = snapshot.GetText(startOffset, caretOffset - startOffset);

            if (string.IsNullOrWhiteSpace(precedingText))
                return;

            // T059: Cancel previous request and create new cancellation token
            CancelCurrentRequest();
            var cts = new CancellationTokenSource();
            _currentCts = cts;

            var request = new AiGhostTextRequest
            {
                SessionId = Guid.NewGuid().ToString("N"),
                DocumentText = snapshot.GetText(),
                CursorOffset = caretOffset,
                PrecedingText = precedingText
            };

            // Fire and forget — response handling is on the UI thread via ContinueWith
            var task = manager.Client.SendRequestAsync<AiGhostTextResponse, AiGhostTextRequest>(
                MessageTypes.AiGhostText, request, timeoutMs: 5000, ct: cts.Token);

            task.ContinueWith(t =>
            {
                if (cts.IsCancellationRequested)
                    return;

                if (t.IsFaulted)
                {
                    Log.Debug(t.Exception, "GhostTextAdornment: request failed");
                    return;
                }

                if (t.IsCanceled)
                    return;

                var response = t.Result;
                if (response.Success && !string.IsNullOrEmpty(response.PredictedText))
                {
                    // Dispatch to UI thread
                    _view.VisualElement.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ShowGhostText(response.PredictedText, response.CursorOffset);
                    }));
                }
            }, System.Threading.Tasks.TaskScheduler.Default);
        }

        /// <summary>
        /// Renders the predicted text as a gray semi-transparent adornment at the cursor position.
        /// </summary>
        private void ShowGhostText(string prediction, int cursorOffset)
        {
            try
            {
                // Verify cursor hasn't moved since request was made
                var currentOffset = _view.Caret.Position.BufferPosition.Position;
                if (currentOffset != cursorOffset)
                    return;

                DismissGhostText();

                _currentPrediction = prediction;
                _predictionCursorOffset = cursorOffset;

                // Get the caret visual position
                var caretLine = _view.Caret.ContainingTextViewLine;
                var caretBounds = _view.Caret.Left;
                var caretTop = caretLine.TextTop;

                // Create the ghost text visual. Foreground reads the current TextDisabled brush
                // from the registry once at creation -- ghost text is short-lived (typed and
                // dismissed) so live theme switches between show and dismiss are not a concern;
                // each new ghost text picks up the current theme.
                var ghostBrush = (Brush)ThemeRegistry.Instance.Resources[ThemeTokens.TextDisabled];
                var textBlock = new TextBlock
                {
                    Text = prediction,
                    FontFamily = _view.FormattedLineSource?.DefaultTextProperties?.Typeface?.FontFamily
                                 ?? Typography.MonoFont,
                    FontSize = _view.FormattedLineSource?.DefaultTextProperties?.FontRenderingEmSize ?? 13.0,
                    Foreground = ghostBrush,
                    FontStyle = FontStyles.Italic,
                    IsHitTestVisible = false,
                    Background = Brushes.Transparent
                };

                Canvas.SetLeft(textBlock, caretBounds);
                Canvas.SetTop(textBlock, caretTop - _view.ViewportTop);

                _layer.AddAdornment(
                    AdornmentPositioningBehavior.ViewportRelative,
                    null,
                    "GhostTextPrediction",
                    textBlock,
                    null);

                _isGhostTextVisible = true;

                Log.Debug("GhostTextAdornment: showing prediction ({Length} chars)", prediction.Length);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GhostTextAdornment: failed to show ghost text");
            }
        }

        /// <summary>
        /// Handles Tab (accept) and Escape (dismiss) key presses for the ghost text.
        /// </summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isGhostTextVisible || string.IsNullOrEmpty(_currentPrediction))
                return;

            if (e.Key == Key.Tab)
            {
                // Accept: insert the predicted text into the buffer
                e.Handled = true;
                AcceptGhostText();
            }
            else if (e.Key == Key.Escape)
            {
                // Dismiss the ghost text
                e.Handled = true;
                DismissGhostText();
            }
        }

        /// <summary>
        /// Inserts the current ghost text prediction into the text buffer at the cursor position.
        /// </summary>
        private void AcceptGhostText()
        {
            if (string.IsNullOrEmpty(_currentPrediction))
                return;

            var prediction = _currentPrediction;
            DismissGhostText();

            try
            {
                var caretPosition = _view.Caret.Position.BufferPosition;

                using var edit = _view.TextBuffer.CreateEdit();
                edit.Insert(caretPosition.Position, prediction);
                edit.Apply();

                Log.Debug("GhostTextAdornment: accepted prediction ({Length} chars)", prediction.Length);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GhostTextAdornment: failed to accept prediction");
            }
        }

        /// <summary>
        /// Removes the ghost text visual from the adornment layer and clears the current prediction.
        /// </summary>
        private void DismissGhostText()
        {
            if (_isGhostTextVisible)
            {
                _layer.RemoveAdornmentsByTag("GhostTextPrediction");
                _isGhostTextVisible = false;
            }

            _currentPrediction = null;
        }

        /// <summary>
        /// T059: Cancels the current in-flight ghost text request to prevent stale predictions.
        /// </summary>
        private void CancelCurrentRequest()
        {
            var cts = Interlocked.Exchange(ref _currentCts, null);
            if (cts != null)
            {
                try
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed — safe to ignore
                }
            }
        }

        /// <summary>
        /// Cleans up event subscriptions and cancels any pending request when the view closes.
        /// </summary>
        private void OnViewClosed(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            CancelCurrentRequest();
            DismissGhostText();

            _view.TextBuffer.Changed -= OnTextBufferChanged;
            _view.Closed -= OnViewClosed;
            _view.VisualElement.PreviewKeyDown -= OnPreviewKeyDown;
        }
    }
}
