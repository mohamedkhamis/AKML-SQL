#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Ui;
using Microsoft.VisualStudio.Text.Editor;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor.SchemaProgress
{
    /// <summary>
    /// Top-of-editor WPF margin that shows the schema loading state for the
    /// current session. Sits just below the <see cref="Toolbar.EditorToolbar"/>
    /// and above the document text — matching SQL Prompt's layout.
    ///
    /// <para>
    /// Uses a state machine (<see cref="MarginState"/>) to guarantee stable
    /// visibility transitions: the margin shows ONCE when schema loading starts,
    /// stays visible with live progress updates while Phase A / B are running,
    /// briefly shows "ready" when complete, then auto-hides. It only redraws
    /// when text actually changes — no more flicker from per-poll toggling.
    /// </para>
    /// </summary>
    internal sealed class SchemaProgressMargin : UserControl, IWpfTextViewMargin
    {
        private const double MarginHeight = 22;
        private const int PollIntervalMs = 1000;
        private const int ReadyDisplayMs = 2000;

        private enum MarginState
        {
            Hidden,      // Nothing to show
            Loading,     // Phase 0/1 — show spinner + progress
            Ready,       // Phase 2/3 — briefly show "ready" before hiding
        }

        private readonly IWpfTextView _textView;
        private readonly DispatcherTimer _pollTimer;
        private readonly TextBlock _statusText;
        private readonly Border _spinner;
        private readonly RotateTransform _spinnerRotate;
        private readonly Border _rootBorder;

        private bool _disposed;
        private bool _polling;
        private string _sessionId = string.Empty;
        private MarginState _state = MarginState.Hidden;
        private string _lastDatabase = string.Empty;
        private string _lastDisplayedText = string.Empty;
        private DateTime _readyShownAtUtc = DateTime.MinValue;

        // Theme brushes
        private readonly SolidColorBrush _bgBrush;
        private readonly SolidColorBrush _borderBrush;
        private readonly SolidColorBrush _fgBrush;
        private readonly SolidColorBrush _accentBrush;

        public SchemaProgressMargin(IWpfTextView textView)
        {
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));

            var theme = ThemeManager.Instance.DetectTheme();
            var isDark = theme == VsThemeKind.Dark || theme == VsThemeKind.Blue;
            if (isDark)
            {
                _bgBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x25, 0x28, 0x36)));
                _borderBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x4E)));
                _fgBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)));
                _accentBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x4F, 0x8C, 0xFF)));
            }
            else
            {
                _bgBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF8, 0xF8, 0xF8)));
                _borderBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)));
                _fgBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)));
                _accentBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)));
            }

            TryLoadSessionId();

            // ── UI ──
            _spinnerRotate = new RotateTransform(0);
            _spinner = new Border
            {
                Width = 12,
                Height = 12,
                BorderBrush = _accentBrush,
                BorderThickness = new Thickness(2, 2, 0, 0),
                CornerRadius = new CornerRadius(6),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 8, 0),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = _spinnerRotate
            };

            _statusText = new TextBlock
            {
                Text = string.Empty,
                Foreground = _fgBrush,
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center
            };

            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(_spinner);
            stack.Children.Add(_statusText);

            _rootBorder = new Border
            {
                Background = _bgBrush,
                BorderBrush = _borderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = stack
            };

            Content = _rootBorder;
            Height = 0; // Start collapsed — no layout height reserved until state != Hidden
            Visibility = Visibility.Collapsed;

            // Continuous spin animation (always running, only visible when we are)
            var rotate = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromMilliseconds(1200),
                RepeatBehavior = RepeatBehavior.Forever
            };
            _spinnerRotate.BeginAnimation(RotateTransform.AngleProperty, rotate);

            // Polling timer
            _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(PollIntervalMs)
            };
            _pollTimer.Tick += OnPollTick;
            _pollTimer.Start();

            _textView.Closed += OnTextViewClosed;
        }

        // ─── IWpfTextViewMargin ─────────────────────────────────────────────
        public FrameworkElement VisualElement => this;

        /// <summary>
        /// Layout height reserved for the margin. Returns 0 when hidden so the
        /// editor doesn't get a blank bar when nothing is loading, and
        /// <see cref="MarginHeight"/> while loading/ready.
        /// </summary>
        public double MarginSize => _state == MarginState.Hidden ? 0 : MarginHeight;

        public bool Enabled => !_disposed;

        public ITextViewMargin? GetTextViewMargin(string marginName)
            => string.Equals(marginName, "AkmlSqlSchemaProgressMargin", StringComparison.OrdinalIgnoreCase)
               ? this
               : null;

        // ─── Poll loop ──────────────────────────────────────────────────────
        private async void OnPollTick(object sender, EventArgs e)
        {
            if (_disposed) return;

            // Re-entrancy guard: the DispatcherTimer fires every 1s but the
            // IPC call below can take up to 3s (its timeout). Without this
            // guard a slow engine response would let a second Tick race ahead
            // of the first, and the two Apply(resp) calls could interleave
            // state transitions (a stale response stomping on a newer one).
            if (_polling) return;
            _polling = true;

            try
            {
                if (string.IsNullOrEmpty(_sessionId))
                {
                    TryLoadSessionId();
                    if (string.IsNullOrEmpty(_sessionId)) return;
                }

                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                {
                    TransitionTo(MarginState.Hidden);
                    return;
                }

                var req = new SchemaStatusRequest { SessionId = _sessionId };
                var resp = await client.SendRequestAsync<SchemaStatusResponse, SchemaStatusRequest>(
                    MessageTypes.SchemaStatusRequest, req, timeoutMs: 3000);

                if (_disposed) return;
                Apply(resp);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "SchemaProgressMargin: poll failed");
            }
            finally
            {
                _polling = false;
            }
        }

        /// <summary>
        /// Applies a poll response through the state machine. Only calls Show/Hide
        /// when the state actually changes, avoiding the visual flicker from
        /// per-poll visibility toggles.
        /// </summary>
        private void Apply(SchemaStatusResponse? status)
        {
            if (status == null || !status.Exists || string.IsNullOrEmpty(status.DatabaseName))
            {
                TransitionTo(MarginState.Hidden);
                return;
            }

            // Detect database change — if the user switched to another DB,
            // reset the state so the margin shows the new loading cycle.
            if (!string.Equals(status.DatabaseName, _lastDatabase, StringComparison.OrdinalIgnoreCase))
            {
                _lastDatabase = status.DatabaseName;
                if (status.Phase < 2)
                {
                    TransitionTo(MarginState.Loading);
                }
                else
                {
                    // New DB already cached — skip straight to Ready (shown briefly).
                    TransitionTo(MarginState.Ready);
                    _readyShownAtUtc = DateTime.UtcNow;
                }
            }

            switch (status.Phase)
            {
                case 0: // NotLoaded
                    TransitionTo(MarginState.Loading);
                    SetText($"Loading schema [{status.DatabaseName}]…");
                    break;
                case 1: // PhaseA done, PhaseB loading columns
                    TransitionTo(MarginState.Loading);
                    int pct = status.ObjectCount == 0
                        ? 0
                        : (int)(100.0 * status.ColumnsLoadedCount / status.ObjectCount);
                    SetText($"Loading columns for [{status.DatabaseName}] — {pct}% ({status.ColumnsLoadedCount}/{status.ObjectCount})");
                    break;
                case 2: // PhaseB done
                case 3: // Complete
                    if (_state == MarginState.Loading)
                    {
                        TransitionTo(MarginState.Ready);
                        SetText($"Schema [{status.DatabaseName}] ready — {status.ObjectCount} objects");
                        _readyShownAtUtc = DateTime.UtcNow;
                    }
                    else if (_state == MarginState.Ready)
                    {
                        // Already in Ready — hide after the display duration elapses.
                        if ((DateTime.UtcNow - _readyShownAtUtc).TotalMilliseconds >= ReadyDisplayMs)
                        {
                            TransitionTo(MarginState.Hidden);
                        }
                    }
                    else
                    {
                        // Hidden → schema was already loaded before we first saw it.
                        // Stay hidden — no point flashing.
                        TransitionTo(MarginState.Hidden);
                    }
                    break;
            }
        }

        /// <summary>
        /// Transitions to a new state, updating visibility ONLY on actual changes.
        /// </summary>
        private void TransitionTo(MarginState newState)
        {
            if (_state == newState) return;
            _state = newState;

            switch (newState)
            {
                case MarginState.Hidden:
                    Height = 0;
                    Visibility = Visibility.Collapsed;
                    _lastDisplayedText = string.Empty;
                    break;
                case MarginState.Loading:
                case MarginState.Ready:
                    Height = MarginHeight;
                    Visibility = Visibility.Visible;
                    _spinner.Visibility = newState == MarginState.Loading ? Visibility.Visible : Visibility.Collapsed;
                    break;
            }

            // Force the editor to relayout because MarginSize changed.
            try
            {
                if (_textView is FrameworkElement fe)
                {
                    fe.InvalidateMeasure();
                }
            }
            catch { /* non-fatal */ }
        }

        /// <summary>
        /// Sets the status text only when it actually changes. Avoids redundant
        /// TextBlock re-rendering.
        /// </summary>
        private void SetText(string text)
        {
            if (string.Equals(text, _lastDisplayedText, StringComparison.Ordinal)) return;
            _lastDisplayedText = text;
            _statusText.Text = text;
        }

        private void TryLoadSessionId()
        {
            try
            {
                if (_textView.TextBuffer.Properties.TryGetProperty<string>("AkmlSqlSessionId", out var sid))
                {
                    _sessionId = sid ?? string.Empty;
                }
            }
            catch { }
        }

        // ─── Cleanup ────────────────────────────────────────────────────────
        private void OnTextViewClosed(object sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _pollTimer.Stop(); } catch { }
            try { _textView.Closed -= OnTextViewClosed; } catch { }
        }

        private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
    }
}
