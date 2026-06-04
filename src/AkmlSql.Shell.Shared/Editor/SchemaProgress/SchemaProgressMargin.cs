#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Ui.Theme;
using Microsoft.VisualStudio.Text.Editor;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor.SchemaProgress
{
    /// <summary>
    /// Bottom-right toast adornment that mirrors the "Populating suggestions for ..."
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

        private enum MarginState { Hidden, Loading, Ready, NeedsCredentials }

        private readonly IWpfTextView    _textView;
        private readonly IAdornmentLayer _adornmentLayer;
        private readonly DispatcherTimer _pollTimer;

        // Visual elements -- chrome flows through ThemeRegistry merged into _notificationBorder.Resources.
        private readonly TextBlock       _statusText;
        private readonly Ellipse         _spinnerArc;
        private readonly TextBlock       _loadingLabel;   // FR-019/O10: static "Loading..." alternative when motion is disabled
        private readonly TextBlock       _readyGlyph;
        private readonly RotateTransform _spinnerRotate;
        private readonly Border          _notificationBorder;

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

            TryLoadSessionId();

            _spinnerRotate = new RotateTransform(0);

            // Circular arc spinner (~90 deg arc, ~270 deg gap). Stroke flows through the registry
            // merged into _notificationBorder.Resources -- editor-margin context, so
            // EditorSpinnerStroke / EditorMarginBackground apply (Phase 6).
            _spinnerArc = new Ellipse
            {
                Width                 = 12,
                Height                = 12,
                StrokeThickness       = 1.6,
                StrokeDashCap         = PenLineCap.Round,
                StrokeStartLineCap    = PenLineCap.Round,
                StrokeEndLineCap      = PenLineCap.Round,
                StrokeDashArray       = new DoubleCollection { 10, 30 },
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform       = _spinnerRotate,
                VerticalAlignment     = VerticalAlignment.Center,
                Margin                = new Thickness(0, 0, Spacing.Sm, 0)
            };
            _spinnerArc.SetResourceReference(Shape.StrokeProperty, ThemeTokens.EditorSpinnerStroke);

            // FR-019/O10: static "Loading..." alternative shown when ClientAreaAnimation is false.
            // Same horizontal slot as the spinner; visibility is mutually exclusive in Loading state.
            _loadingLabel = new TextBlock
            {
                Text              = "Loading...",
                FontSize          = Typography.Small,
                FontFamily        = Typography.UiFont,
                FontWeight        = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, Spacing.Sm, 0),
                Visibility        = Visibility.Collapsed
            };
            _loadingLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.EditorSpinnerStroke);

            _readyGlyph = new TextBlock
            {
                Text              = "✓",
                FontSize          = 13,
                FontWeight        = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, Spacing.Sm, 0),
                Visibility        = Visibility.Collapsed
            };
            _readyGlyph.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.StatusSuccess);

            _statusText = new TextBlock
            {
                Text              = string.Empty,
                FontSize          = Typography.Small,
                FontFamily        = Typography.UiFont,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming      = TextTrimming.CharacterEllipsis
            };
            _statusText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);

            var row = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment   = VerticalAlignment.Center
            };
            row.Children.Add(_spinnerArc);
            row.Children.Add(_loadingLabel);
            row.Children.Add(_readyGlyph);
            row.Children.Add(_statusText);

            _notificationBorder = new Border
            {
                Width           = NotificationWidth,
                Height          = NotificationHeight,
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(4),
                Padding         = new Thickness(Spacing.Md, 0, Spacing.Md, 0),
                Child           = row,
                Opacity         = 0,
                Visibility      = Visibility.Collapsed
            };

            // Attach the registry HERE so resource lookups from children resolve via
            // _notificationBorder.Resources -- adornment-layer adornments aren't placed under a
            // ThemeAware* root, so the topmost element we control merges the dictionary.
            ThemeRegistry.Instance.AttachTo(_notificationBorder);
            _notificationBorder.SetResourceReference(Border.BackgroundProperty,  ThemeTokens.EditorMarginBackground);
            _notificationBorder.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderDefault);
            _notificationBorder.MouseLeftButtonUp += OnNotificationClicked;

            // Continuous spinner rotation -- only START when motion is allowed (FR-019/O10).
            // If the user toggles the preference at runtime, OnAnimationsEnabledChanged swaps the
            // visual immediately if currently in Loading state; existing animations are not
            // cancelled mid-loop per O10.
            if (HostThemeWatcher.Instance.AnimationsEnabled)
            {
                _spinnerRotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation
                {
                    From           = 0,
                    To             = 360,
                    Duration       = TimeSpan.FromMilliseconds(1100),
                    RepeatBehavior = RepeatBehavior.Forever
                });
            }
            HostThemeWatcher.Instance.AnimationsEnabledChanged += OnAnimationsEnabledChanged;

            // Poll schema status every second.
            _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(PollIntervalMs)
            };
            _pollTimer.Tick += OnPollTick;
            _pollTimer.Start();

            // Reposition when the editor viewport is resized OR when a layout pass
            // runs (LayoutChanged catches the initial layout when ViewportWidth/Height
            // first become non-zero — without it, the adornment can land at (0,0)
            // = top-left if it's added before the editor has measured).
            _textView.ViewportWidthChanged  += OnViewportSizeChanged;
            _textView.ViewportHeightChanged += OnViewportSizeChanged;
            _textView.LayoutChanged         += OnLayoutChanged;
            _textView.Closed += OnTextViewClosed;
        }

        private void OnLayoutChanged(object? sender, Microsoft.VisualStudio.Text.Editor.TextViewLayoutChangedEventArgs e)
        {
            if (_disposed || !_adornmentAdded) return;
            RepositionNotification();
        }

        /// <summary>
        /// Reduced-motion preference flipped at runtime. If we're currently showing the Loading
        /// state, swap between the spinner and the static "Loading..." label so the new preference
        /// takes effect immediately. If we're not in Loading, the swap is deferred -- the next
        /// TransitionTo(Loading) call applies the current preference (per O10).
        /// </summary>
        private void OnAnimationsEnabledChanged(object? sender, EventArgs e)
        {
            if (_disposed) return;
            if (_state == MarginState.Loading)
            {
                ApplyMotionPreferenceForLoading();
            }
        }

        /// <summary>
        /// Applies the current motion preference to the Loading-state visuals: when animations are
        /// enabled the spinner is visible and the static label is hidden; when disabled the spinner
        /// is hidden and the static label is shown.
        /// </summary>
        private void ApplyMotionPreferenceForLoading()
        {
            if (HostThemeWatcher.Instance.AnimationsEnabled)
            {
                _spinnerArc.Visibility   = Visibility.Visible;
                _loadingLabel.Visibility = Visibility.Collapsed;
            }
            else
            {
                _spinnerArc.Visibility   = Visibility.Collapsed;
                _loadingLabel.Visibility = Visibility.Visible;
            }
        }

        // --- Viewport positioning ------------------------------------------

        private void EnsureAdornmentAdded()
        {
            if (_adornmentAdded) return;
            _adornmentAdded = true;

            // ViewportRelative keeps the box pinned to the viewport regardless of scrolling.
            _adornmentLayer.AddAdornment(
                AdornmentPositioningBehavior.ViewportRelative,
                null, null, _notificationBorder, null);

            RepositionNotification();

            // The viewport may not have measured yet — Reposition above could have
            // run with ViewportWidth/Height = 0, clamping the adornment to (0,0)
            // = top-left of the text area (visible as "spinner at line 1"). Schedule
            // a second pass at Loaded priority so it runs after the layout settles.
            _notificationBorder.Dispatcher.BeginInvoke(
                new Action(RepositionNotification),
                DispatcherPriority.Loaded);
        }

        private void OnViewportSizeChanged(object? sender, EventArgs e) => RepositionNotification();

        private void RepositionNotification()
        {
            // Place the notification box at the bottom-right corner of the viewport.
            // ViewportRelative adornments live in a Canvas whose coordinates are the
            // viewport origin at (0,0); SetRight / SetBottom are ignored by that layer
            // (proved in-repo by every other adornment -- MinimapAdornment, StickyScroll,
            // SchemaStatusIndicator -- all using SetLeft / SetTop).
            var left = _textView.ViewportWidth  - NotificationWidth  - EdgeMargin;
            var top  = _textView.ViewportHeight - NotificationHeight - EdgeMargin;
            if (left < 0) left = 0;
            if (top  < 0) top  = 0;
            Canvas.SetLeft(_notificationBorder, left);
            Canvas.SetTop (_notificationBorder, top);
        }

        // --- Poll loop -----------------------------------------------------

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

                // Spec 029: SQL-auth windows are driven by shell-local SqlAuthState, not the engine
                // (which has no session until we send ConnectionChanged). This takes priority.
                if (TryGetAuthState(out var authState) && authState.NeedsCredentials)
                {
                    if (ConnectionWiringHelper.TryResolveStoredSqlCredential(_sessionId, _textView))
                    {
                        // A credential is now stored (this window, or another window on the same
                        // server/login just saved it) — ConnectionChanged was sent; show Loading.
                        TransitionTo(MarginState.Loading);
                        _loadingStartedAtUtc = DateTime.UtcNow;
                    }
                    else
                    {
                        TransitionTo(MarginState.NeedsCredentials);
                    }
                    return;
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

                // Spec 029: the engine rejected a stored SQL credential (login/permission failure).
                // Only treat it as "re-enter credentials" for SQL-auth sessions (SqlAuthState present);
                // Windows-auth permission denials keep their existing behavior.
                if (resp != null && resp.AuthError && TryGetAuthState(out var rejected))
                {
                    SqlCredentialStore.Remove(rejected.Server, rejected.Login);
                    rejected.NeedsCredentials = true;
                    TransitionTo(MarginState.NeedsCredentials);
                    return;
                }

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

            // Database switch -- restart the loading cycle for the new DB.
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
                        SetText($"Loading columns -- {pct}% ({status.ColumnsLoadedCount}/{status.ObjectCount})");
                    }
                    else
                    {
                        SetText("Loading columns...");
                    }
                    break;

                case 2: // PhaseB done
                case 3: // Complete
                    if (_state == MarginState.Loading)
                    {
                        TransitionTo(MarginState.Ready);
                        SetText($"Schema cache ready -- {status.ObjectCount} objects");
                        _readyShownAtUtc = DateTime.UtcNow;
                    }
                    else if (_state == MarginState.Ready)
                    {
                        if ((DateTime.UtcNow - _readyShownAtUtc).TotalMilliseconds >= ReadyDisplayMs)
                            TransitionTo(MarginState.Hidden);
                    }
                    else
                    {
                        // Hidden -> schema was already loaded before we first saw it.
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
                    _notificationBorder.Cursor = System.Windows.Input.Cursors.Arrow;
                    FadeTo(0, () => _notificationBorder.Visibility = Visibility.Collapsed);
                    _lastDisplayedText = string.Empty;
                    break;

                case MarginState.Loading:
                    EnsureAdornmentAdded();
                    _notificationBorder.Visibility = Visibility.Visible;
                    _notificationBorder.Cursor = System.Windows.Input.Cursors.Arrow;
                    _statusText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
                    ApplyMotionPreferenceForLoading();   // FR-019/O10: spinner vs. static "Loading..." label
                    _readyGlyph.Visibility = Visibility.Collapsed;
                    FadeTo(1, null);
                    break;

                case MarginState.Ready:
                    EnsureAdornmentAdded();
                    _notificationBorder.Visibility = Visibility.Visible;
                    _notificationBorder.Cursor = System.Windows.Input.Cursors.Arrow;
                    _statusText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
                    _spinnerArc.Visibility   = Visibility.Collapsed;
                    _loadingLabel.Visibility = Visibility.Collapsed;
                    _readyGlyph.Visibility   = Visibility.Visible;
                    FadeTo(1, null);
                    break;

                case MarginState.NeedsCredentials:
                    EnsureAdornmentAdded();
                    _notificationBorder.Visibility = Visibility.Visible;
                    _notificationBorder.Cursor = System.Windows.Input.Cursors.Hand;
                    _spinnerArc.Visibility = Visibility.Collapsed;
                    _loadingLabel.Visibility = Visibility.Collapsed;
                    _readyGlyph.Visibility = Visibility.Collapsed; // no glyph — emoji render unreliably in the adornment
                    _statusText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.EditorSpinnerStroke); // accent = actionable
                    SetText("SQL auth — click to enable IntelliSense");
                    FadeTo(1, null);
                    break;
            }
        }

        private void FadeTo(double target, Action? onCompleted)
        {
            // Reduced-motion: skip the fade entirely so theme switches and visibility changes are
            // instantaneous (per O10).
            if (!HostThemeWatcher.Instance.AnimationsEnabled)
            {
                _notificationBorder.BeginAnimation(UIElement.OpacityProperty, null);
                _notificationBorder.Opacity = target;
                onCompleted?.Invoke();
                return;
            }

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

        // --- External triggers ---------------------------------------------

        /// <summary>
        /// Forces the toast into the Loading state immediately, before the next poll
        /// cycle. Use this from <c>RefreshCacheCommand</c> so the user gets visible
        /// feedback that their refresh request was received — without this, the
        /// 1-second poll interval can miss the brief NotLoaded → PhaseA transition
        /// for fast schemas, and the toast would jump straight from "Ready"
        /// (previous cycle) to "Ready" (refreshed) with no loading spinner shown.
        /// </summary>
        public void BeginRefresh()
        {
            if (_disposed) return;

            void Apply()
            {
                if (_disposed) return;
                _loadingTimedOut     = false;
                _loadingStartedAtUtc = DateTime.UtcNow;
                TransitionTo(MarginState.Loading);
                SetText("Refreshing schema cache...");
            }

            var dispatcher = _textView.VisualElement.Dispatcher;
            if (dispatcher.CheckAccess()) Apply();
            else dispatcher.BeginInvoke(new Action(Apply));
        }

        private void OnNotificationClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_disposed) return;
            if (_state != MarginState.NeedsCredentials) return;
            BeginEnterCredentials();
        }

        /// <summary>Spec 029. Opens the SQL credential dialog; on a successful save (or clear),
        /// re-resolves the connection so schema loads (or the affordance reappears).</summary>
        public void BeginEnterCredentials()
        {
            if (_disposed) return;
            if (!TryGetAuthState(out var state)) return;
            try
            {
                bool hasExisting = SqlCredentialStore.Has(state.Server, state.Login);
                var dlg = new SqlCredentialDialog(state.Server, state.Database, state.Login, hasExisting);
                var result = dlg.ShowDialog();
                if (result == true)
                {
                    if (ConnectionWiringHelper.TryResolveStoredSqlCredential(_sessionId, _textView))
                    {
                        TransitionTo(MarginState.Loading);
                        _loadingStartedAtUtc = DateTime.UtcNow;
                    }
                    else
                    {
                        // "Clear saved password" was used (no credential now) — keep the affordance.
                        state.NeedsCredentials = true;
                        TransitionTo(MarginState.NeedsCredentials);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BeginEnterCredentials failed");
            }
        }

        private bool TryGetAuthState(out SqlAuthState state)
        {
            state = null!;
            try
            {
                if (_textView.TextBuffer.Properties.TryGetProperty<SqlAuthState>("AkmlSqlAuthState", out var s) && s != null)
                {
                    state = s;
                    return true;
                }
            }
            catch { }
            return false;
        }

        // --- Cleanup -------------------------------------------------------

        private void OnTextViewClosed(object sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _pollTimer.Stop(); } catch { }
            try { HostThemeWatcher.Instance.AnimationsEnabledChanged -= OnAnimationsEnabledChanged; } catch { }
            try { _textView.ViewportWidthChanged  -= OnViewportSizeChanged; } catch { }
            try { _textView.ViewportHeightChanged -= OnViewportSizeChanged; } catch { }
            try { _textView.LayoutChanged         -= OnLayoutChanged; } catch { }
            try { _textView.Closed -= OnTextViewClosed; } catch { }
            try { _notificationBorder.MouseLeftButtonUp -= OnNotificationClicked; } catch { }
        }
    }
}
