#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
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
    /// Slim top-of-editor margin that mirrors SQL Prompt's "Populating suggestions
    /// for …" indicator: a compact, right-aligned status strip with a thin circular
    /// spinner, muted text, and a subtle bottom divider. Blends into the editor
    /// chrome rather than stamping a bright bar across the top.
    /// </summary>
    internal sealed class SchemaProgressMargin : UserControl, IWpfTextViewMargin
    {
        private const double MarginHeight = 20;
        private const int PollIntervalMs = 1000;
        private const int ReadyDisplayMs = 2000;
        private const int LoadingTimeoutMs = 15_000;

        private enum MarginState { Hidden, Loading, Ready }

        private readonly IWpfTextView _textView;
        private readonly DispatcherTimer _pollTimer;
        private readonly TextBlock _statusText;
        private readonly Ellipse _spinnerArc;
        private readonly TextBlock _readyGlyph;
        private readonly RotateTransform _spinnerRotate;
        private readonly Border _rootBorder;

        private bool _disposed;
        private bool _polling;
        private string _sessionId = string.Empty;
        private MarginState _state = MarginState.Hidden;
        private string _lastDatabase = string.Empty;
        private string _lastDisplayedText = string.Empty;
        private DateTime _readyShownAtUtc = DateTime.MinValue;
        private DateTime _loadingStartedAtUtc = DateTime.MinValue;
        private bool _loadingTimedOut;

        // Theme brushes — resolved once in ctor, frozen for free cross-thread use.
        private readonly SolidColorBrush _bgBrush;
        private readonly SolidColorBrush _borderBrush;
        private readonly SolidColorBrush _mutedBrush;
        private readonly SolidColorBrush _accentBrush;
        private readonly SolidColorBrush _readyBrush;

        public SchemaProgressMargin(IWpfTextView textView)
        {
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));

            var theme = ThemeManager.Instance;
            _bgBrush     = Freeze(new SolidColorBrush(theme.PreviewBackground));
            _borderBrush = Freeze(new SolidColorBrush(theme.Border));
            _mutedBrush  = Freeze(new SolidColorBrush(theme.PlaceholderText));
            _accentBrush = Freeze(new SolidColorBrush(theme.AccentColor));
            // Semantic green used only for the "ready" checkmark; intentionally
            // theme-independent so success always reads the same way.
            _readyBrush  = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43)));

            TryLoadSessionId();

            _spinnerRotate = new RotateTransform(0);

            // Thin circular arc — one stroke-dash sweeps ~90° of the circle;
            // the rotate transform carries it around. Gives a modern "arc chasing
            // its tail" spinner instead of the previous rotating rectangle border.
            _spinnerArc = new Ellipse
            {
                Width = 12,
                Height = 12,
                Stroke = _accentBrush,
                StrokeThickness = 1.6,
                StrokeDashCap = PenLineCap.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                // Ellipse perimeter ≈ 2πr ≈ 37.7 → 10 visible + 30 gap ≈ 90° arc.
                StrokeDashArray = new DoubleCollection { 10, 30 },
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = _spinnerRotate,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };

            _readyGlyph = new TextBlock
            {
                Text = "\u2713",
                Foreground = _readyBrush,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Visibility = Visibility.Collapsed
            };

            _statusText = new TextBlock
            {
                Text = string.Empty,
                Foreground = _mutedBrush,
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            // Right-aligned content keeps the visual weight off the code area —
            // the margin reads as a chrome strip rather than a banner.
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(_spinnerArc);
            content.Children.Add(_readyGlyph);
            content.Children.Add(_statusText);

            _rootBorder = new Border
            {
                Background = _bgBrush,
                BorderBrush = _borderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 0, 12, 0),
                Child = content
            };

            Content = _rootBorder;
            Height = 0;
            Opacity = 0;
            Visibility = Visibility.Collapsed;

            // Continuous rotation (stays running; visibility of the arc is gated
            // by the state machine so the animation never stalls the first time).
            _spinnerRotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromMilliseconds(1100),
                RepeatBehavior = RepeatBehavior.Forever
            });

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

            // Re-entrancy guard: the DispatcherTimer fires every 1s but the IPC
            // call below can take up to 3s; without this guard a slow engine
            // response would let a second Tick race ahead and stomp state.
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

        private void Apply(SchemaStatusResponse? status)
        {
            if (status == null || !status.Exists || string.IsNullOrEmpty(status.DatabaseName))
            {
                TransitionTo(MarginState.Hidden);
                return;
            }

            // Database switch — restart the loading cycle for the new DB.
            if (!string.Equals(status.DatabaseName, _lastDatabase, StringComparison.OrdinalIgnoreCase))
            {
                _lastDatabase = status.DatabaseName;
                _loadingTimedOut = false;
                if (status.Phase < 2)
                {
                    TransitionTo(MarginState.Loading);
                    _loadingStartedAtUtc = DateTime.UtcNow;
                }
                else
                {
                    TransitionTo(MarginState.Ready);
                    _readyShownAtUtc = DateTime.UtcNow;
                }
            }

            switch (status.Phase)
            {
                case 0: // NotLoaded
                    if (_loadingTimedOut)
                    {
                        TransitionTo(MarginState.Hidden);
                        break;
                    }
                    if (_state != MarginState.Loading)
                    {
                        TransitionTo(MarginState.Loading);
                        _loadingStartedAtUtc = DateTime.UtcNow;
                    }
                    else if ((DateTime.UtcNow - _loadingStartedAtUtc).TotalMilliseconds >= LoadingTimeoutMs)
                    {
                        _loadingTimedOut = true;
                        TransitionTo(MarginState.Hidden);
                        Log.Warning("SchemaProgressMargin: loading timed out for [{Database}] after {Timeout}ms",
                            status.DatabaseName, LoadingTimeoutMs);
                        break;
                    }
                    SetText($"Populating suggestions for {status.DatabaseName}");
                    break;

                case 1: // PhaseA done, PhaseB loading columns
                    TransitionTo(MarginState.Loading);
                    if (status.ObjectCount > 0)
                    {
                        int pct = (int)(100.0 * status.ColumnsLoadedCount / status.ObjectCount);
                        SetText($"Loading columns — {pct}% ({status.ColumnsLoadedCount}/{status.ObjectCount})");
                    }
                    else
                    {
                        SetText("Loading columns…");
                    }
                    break;

                case 2: // PhaseB done
                case 3: // Complete
                    if (_state == MarginState.Loading)
                    {
                        TransitionTo(MarginState.Ready);
                        SetText($"Schema cache ready — {status.ObjectCount} objects");
                        _readyShownAtUtc = DateTime.UtcNow;
                    }
                    else if (_state == MarginState.Ready)
                    {
                        if ((DateTime.UtcNow - _readyShownAtUtc).TotalMilliseconds >= ReadyDisplayMs)
                        {
                            TransitionTo(MarginState.Hidden);
                        }
                    }
                    else
                    {
                        // Hidden → schema was already loaded before we first saw it.
                        TransitionTo(MarginState.Hidden);
                    }
                    break;
            }
        }

        private void TransitionTo(MarginState newState)
        {
            if (_state == newState) return;
            _state = newState;

            switch (newState)
            {
                case MarginState.Hidden:
                    FadeTo(0, () =>
                    {
                        Height = 0;
                        Visibility = Visibility.Collapsed;
                    });
                    _lastDisplayedText = string.Empty;
                    break;

                case MarginState.Loading:
                    Height = MarginHeight;
                    Visibility = Visibility.Visible;
                    _spinnerArc.Visibility = Visibility.Visible;
                    _readyGlyph.Visibility = Visibility.Collapsed;
                    _statusText.Foreground = _mutedBrush;
                    FadeTo(1, null);
                    break;

                case MarginState.Ready:
                    Height = MarginHeight;
                    Visibility = Visibility.Visible;
                    _spinnerArc.Visibility = Visibility.Collapsed;
                    _readyGlyph.Visibility = Visibility.Visible;
                    _statusText.Foreground = _mutedBrush;
                    FadeTo(1, null);
                    break;
            }

            // MarginSize changed — ask the editor to relayout.
            try
            {
                if (_textView is FrameworkElement fe)
                    fe.InvalidateMeasure();
            }
            catch { /* non-fatal */ }
        }

        /// <summary>
        /// Smooth opacity fade so the margin appears/disappears without flicker.
        /// </summary>
        private void FadeTo(double target, Action? onCompleted)
        {
            var anim = new DoubleAnimation
            {
                To = target,
                Duration = TimeSpan.FromMilliseconds(150),
                FillBehavior = FillBehavior.HoldEnd
            };
            if (onCompleted != null)
                anim.Completed += (_, _) => onCompleted();
            BeginAnimation(OpacityProperty, anim);
        }

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
