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
    /// Bottom-right toast adornment that mirrors the "Populating suggestions for …"
    /// indicator: a compact notification box with a circular spinner, muted text,
    /// and fade-in/out transitions. Hosted on the <c>AkmlSchemaProgress</c>
    /// adornment layer so it floats above the editor text rather than occupying
    /// a margin strip at the top.
    /// </summary>
    internal sealed class SchemaProgressMargin : IDisposable
    {
        private const double NotificationWidth  = 280;
        private const double NotificationHeight = 56;
        private const double EdgeMargin         = 12;
        private const int    PollIntervalMs     = 1000;
        private const int    ReadyDisplayMs     = 2000;
        private const int    LoadingTimeoutMs   = 15_000;

        private static readonly FontFamily SegoeUiFont = new FontFamily("Segoe UI");

        private enum MarginState { Hidden, Loading, Ready }

        private readonly IWpfTextView    _textView;
        private readonly IAdornmentLayer _adornmentLayer;
        private readonly DispatcherTimer _pollTimer;

        // Visual elements — all frozen-brush themed.
        private readonly TextBlock      _statusText;
        private readonly Ellipse        _spinnerArc;
        private readonly TextBlock      _readyGlyph;
        private readonly RotateTransform _spinnerRotate;
        private readonly Border         _notificationBorder;

        // Theme brushes — resolved once in ctor, frozen for cross-thread safety.
        private readonly SolidColorBrush _bgBrush;
        private readonly SolidColorBrush _borderBrush;
        private readonly SolidColorBrush _mutedBrush;
        private readonly SolidColorBrush _accentBrush;
        private readonly SolidColorBrush _readyBrush;

        private bool        _disposed;
        private bool        _polling;
        private string      _sessionId            = string.Empty;
        private MarginState _state                = MarginState.Hidden;
        private string      _lastDatabase         = string.Empty;
        private string      _lastDisplayedText    = string.Empty;
        private DateTime    _readyShownAtUtc      = DateTime.MinValue;
        private DateTime    _loadingStartedAtUtc  = DateTime.MinValue;
        private bool        _loadingTimedOut;
        private bool        _adornmentAdded;

        public SchemaProgressMargin(IWpfTextView textView, IAdornmentLayer adornmentLayer)
        {
            _textView       = textView       ?? throw new ArgumentNullException(nameof(textView));
            _adornmentLayer = adornmentLayer ?? throw new ArgumentNullException(nameof(adornmentLayer));

            var theme    = ThemeManager.Instance;
            _bgBrush     = Freeze(new SolidColorBrush(theme.EditorPanelBackground));
            _borderBrush = Freeze(new SolidColorBrush(theme.Border));
            _mutedBrush  = Freeze(new SolidColorBrush(theme.PlaceholderText));
            _accentBrush = Freeze(new SolidColorBrush(theme.AccentColor));
            // Semantic green for the "ready" checkmark — theme-independent.
            _readyBrush  = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43)));

            TryLoadSessionId();

            _spinnerRotate = new RotateTransform(0);

            // Circular arc spinner (~90° arc, ~270° gap).
            _spinnerArc = new Ellipse
            {
                Width               = 12,
                Height              = 12,
                Stroke              = _accentBrush,
                StrokeThickness     = 1.6,
                StrokeDashCap       = PenLineCap.Round,
                StrokeStartLineCap  = PenLineCap.Round,
                StrokeEndLineCap    = PenLineCap.Round,
                StrokeDashArray     = new DoubleCollection { 10, 30 },
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform     = _spinnerRotate,
                VerticalAlignment   = VerticalAlignment.Center,
                Margin              = new Thickness(0, 0, 8, 0)
            };

            _readyGlyph = new TextBlock
            {
                Text              = "\u2713",
                Foreground        = _readyBrush,
                FontSize          = 13,
                FontWeight        = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 8, 0),
                Visibility        = Visibility.Collapsed
            };

            _statusText = new TextBlock
            {
                Text          = string.Empty,
                Foreground    = _mutedBrush,
                FontSize      = 11,
                FontFamily    = SegoeUiFont,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming  = TextTrimming.CharacterEllipsis
            };

            var row = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment   = VerticalAlignment.Center
            };
            row.Children.Add(_spinnerArc);
            row.Children.Add(_readyGlyph);
            row.Children.Add(_statusText);

            _notificationBorder = new Border
            {
                Width           = NotificationWidth,
                Height          = NotificationHeight,
                Background      = _bgBrush,
                BorderBrush     = _borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(4),
                Padding         = new Thickness(12, 0, 12, 0),
                Child           = row,
                Opacity         = 0,
                Visibility      = Visibility.Collapsed
            };

            // Continuous spinner rotation — gated by visibility.
            _spinnerRotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation
            {
                From             = 0,
                To               = 360,
                Duration         = TimeSpan.FromMilliseconds(1100),
                RepeatBehavior   = RepeatBehavior.Forever
            });

            // Poll schema status every second.
            _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(PollIntervalMs)
            };
            _pollTimer.Tick += OnPollTick;
            _pollTimer.Start();

            // Reposition when the editor viewport is resized.
            _textView.ViewportWidthChanged  += OnViewportSizeChanged;
            _textView.ViewportHeightChanged += OnViewportSizeChanged;
            _textView.Closed += OnTextViewClosed;
        }

        // ─── Viewport positioning ───────────────────────────────────────────

        private void EnsureAdornmentAdded()
        {
            if (_adornmentAdded) return;
            _adornmentAdded = true;

            // ViewportRelative keeps the box pinned to the viewport regardless of scrolling.
            _adornmentLayer.AddAdornment(
                AdornmentPositioningBehavior.ViewportRelative,
                null, null, _notificationBorder, null);

            RepositionNotification();
        }

        private void OnViewportSizeChanged(object? sender, EventArgs e) => RepositionNotification();

        private void RepositionNotification()
        {
            // Place the notification box at the bottom-right corner of the viewport.
            // ViewportRelative adornments live in a Canvas whose coordinates are the
            // viewport origin at (0,0); SetRight / SetBottom are ignored by that layer
            // (proved in-repo by every other adornment — MinimapAdornment, StickyScroll,
            // SchemaStatusIndicator — all using SetLeft / SetTop).
            var left = _textView.ViewportWidth  - NotificationWidth  - EdgeMargin;
            var top  = _textView.ViewportHeight - NotificationHeight - EdgeMargin;
            if (left < 0) left = 0;
            if (top  < 0) top  = 0;
            Canvas.SetLeft(_notificationBorder, left);
            Canvas.SetTop (_notificationBorder, top);
        }

        // ─── Poll loop ──────────────────────────────────────────────────────

        private async void OnPollTick(object sender, EventArgs e)
        {
            if (_disposed) return;
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

                var req  = new SchemaStatusRequest { SessionId = _sessionId };
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
                _lastDatabase    = status.DatabaseName;
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
                            TransitionTo(MarginState.Hidden);
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
                    FadeTo(0, () => _notificationBorder.Visibility = Visibility.Collapsed);
                    _lastDisplayedText = string.Empty;
                    break;

                case MarginState.Loading:
                    EnsureAdornmentAdded();
                    _notificationBorder.Visibility = Visibility.Visible;
                    _spinnerArc.Visibility = Visibility.Visible;
                    _readyGlyph.Visibility = Visibility.Collapsed;
                    _statusText.Foreground = _mutedBrush;
                    FadeTo(1, null);
                    break;

                case MarginState.Ready:
                    EnsureAdornmentAdded();
                    _notificationBorder.Visibility = Visibility.Visible;
                    _spinnerArc.Visibility  = Visibility.Collapsed;
                    _readyGlyph.Visibility  = Visibility.Visible;
                    _statusText.Foreground  = _mutedBrush;
                    FadeTo(1, null);
                    break;
            }
        }

        private void FadeTo(double target, Action? onCompleted)
        {
            var anim = new DoubleAnimation
            {
                To           = target,
                Duration     = TimeSpan.FromMilliseconds(150),
                FillBehavior = FillBehavior.HoldEnd
            };
            if (onCompleted != null)
                anim.Completed += (_, _) => onCompleted();
            _notificationBorder.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private void SetText(string text)
        {
            if (string.Equals(text, _lastDisplayedText, StringComparison.Ordinal)) return;
            _lastDisplayedText = text;
            _statusText.Text   = text;
        }

        private void TryLoadSessionId()
        {
            try
            {
                if (_textView.TextBuffer.Properties.TryGetProperty<string>("AkmlSqlSessionId", out var sid))
                    _sessionId = sid ?? string.Empty;
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
            try { _textView.ViewportWidthChanged  -= OnViewportSizeChanged; } catch { }
            try { _textView.ViewportHeightChanged -= OnViewportSizeChanged; } catch { }
            try { _textView.Closed -= OnTextViewClosed; } catch { }
        }

        private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
    }
}
