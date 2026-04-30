using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Serilog;

namespace AkmlSql.Shell.Shared.Ui.Theme
{
    /// <summary>
    /// Listens for environmental theme changes (host VS/SSMS theme + Windows accessibility settings)
    /// and feeds them to <see cref="ThemeRegistry"/>. Also exposes the user's animation preference
    /// (<see cref="AnimationsEnabled"/>) for motion-aware surfaces (FR-019).
    ///
    /// Subscriptions:
    /// • <c>VSColorTheme.ThemeChanged</c> (Microsoft.VisualStudio.PlatformUI) — runtime VS/SSMS theme switch.
    /// • <c>SystemParameters.StaticPropertyChanged</c> filtered to <c>HighContrast</c> — accessibility forced override.
    /// • <c>SystemParameters.StaticPropertyChanged</c> filtered to <c>ClientAreaAnimation</c> — reduced-motion preference.
    ///
    /// Failure mode: if <c>VSColorTheme</c> is unavailable on a niche host (older SSMS 20 build, design-time host),
    /// the watcher logs a warning at startup and falls back to a one-shot <see cref="SystemColors.Window"/>
    /// luminance read. The user can still set Dark/Light explicitly via the AKML preference.
    /// </summary>
    public sealed class HostThemeWatcher
    {
        private static readonly object SLock = new object();
        private static HostThemeWatcher _instance;
        public static HostThemeWatcher Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (SLock)
                    {
                        if (_instance == null) _instance = new HostThemeWatcher();
                    }
                }
                return _instance;
            }
        }

        private bool _initialized;

        /// <summary>
        /// Latest classification of the host's VS/SSMS theme based on luminance of the tool-window background.
        /// HighContrast is tracked separately via <see cref="IsHighContrast"/>.
        /// </summary>
        public ThemeVariant LastDetectedHostVariant { get; private set; } = ThemeVariant.Light;

        /// <summary>Mirrors <see cref="SystemParameters.HighContrast"/>.</summary>
        public bool IsHighContrast { get; private set; }

        /// <summary>
        /// Mirrors <see cref="SystemParameters.ClientAreaAnimation"/>. Read by motion-aware surfaces
        /// at the moment they start an animation; running animations are not canceled mid-loop.
        /// </summary>
        public bool AnimationsEnabled { get; private set; } = true;

        /// <summary>
        /// Raised when <see cref="AnimationsEnabled"/> flips. Surfaces with a visible representation
        /// dependent on this flag (e.g., <c>SchemaProgressMargin</c>) subscribe to swap their visual
        /// on the next <c>Loaded</c> cycle.
        /// </summary>
        public event EventHandler AnimationsEnabledChanged;

        private HostThemeWatcher() { }

        /// <summary>
        /// Initialize subscriptions. Idempotent — safe to call multiple times.
        /// Must be called from the WPF dispatcher thread (typically during shell-package init).
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;

            // 1) Initial reads.
            IsHighContrast = SafeReadHighContrast();
            AnimationsEnabled = SafeReadAnimationsEnabled();
            LastDetectedHostVariant = DetectHostVariant();

            // 2) Subscribe to runtime changes.
            SubscribeVsColorTheme();

            // SystemParameters.StaticPropertyChanged is a static event — no unsubscribe needed for process lifetime.
            try
            {
                SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "HostThemeWatcher: failed to subscribe to SystemParameters.StaticPropertyChanged");
            }

            _initialized = true;
            Log.Debug("HostThemeWatcher initialized: hostVariant={Variant}, highContrast={HC}, animationsEnabled={Anim}",
                LastDetectedHostVariant, IsHighContrast, AnimationsEnabled);
        }

        // -------------------------------------------------------------------
        // VS color theme subscription (defensive — VSColorTheme may be unavailable in some hosts)
        // -------------------------------------------------------------------

        private void SubscribeVsColorTheme()
        {
            try
            {
                Microsoft.VisualStudio.PlatformUI.VSColorTheme.ThemeChanged += OnVsThemeChanged;
            }
            catch (Exception ex)
            {
                Log.Warning(ex,
                    "HostThemeWatcher: VSColorTheme.ThemeChanged unavailable; runtime host-theme tracking disabled. " +
                    "User can still set Dark/Light explicitly via the AKML preference.");
            }
        }

        private void OnVsThemeChanged(Microsoft.VisualStudio.PlatformUI.ThemeChangedEventArgs args)
        {
            // Marshal to UI dispatcher and re-classify.
            DispatchToUi(() =>
            {
                LastDetectedHostVariant = DetectHostVariant();
                ThemeRegistry.Instance.OnHostThemeChanged(LastDetectedHostVariant);
            });
        }

        // -------------------------------------------------------------------
        // SystemParameters subscription (HighContrast + ClientAreaAnimation)
        // -------------------------------------------------------------------

        private void OnSystemParametersChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e?.PropertyName == "HighContrast")
            {
                DispatchToUi(() =>
                {
                    var prev = IsHighContrast;
                    IsHighContrast = SafeReadHighContrast();
                    if (prev != IsHighContrast)
                    {
                        ThemeRegistry.Instance.OnHighContrastChanged(IsHighContrast);
                    }
                });
            }
            else if (e?.PropertyName == "ClientAreaAnimation")
            {
                DispatchToUi(() =>
                {
                    var prev = AnimationsEnabled;
                    AnimationsEnabled = SafeReadAnimationsEnabled();
                    if (prev != AnimationsEnabled)
                    {
                        AnimationsEnabledChanged?.Invoke(this, EventArgs.Empty);
                    }
                });
            }
        }

        // -------------------------------------------------------------------
        // Detection helpers
        // -------------------------------------------------------------------

        private static bool SafeReadHighContrast()
        {
            try { return SystemParameters.HighContrast; }
            catch { return false; }
        }

        private static bool SafeReadAnimationsEnabled()
        {
            try { return SystemParameters.ClientAreaAnimation; }
            catch { return true; }
        }

        /// <summary>
        /// Classify the host's tool-window background luminance as Light or Dark.
        /// Tries <c>VSColorTheme.GetThemedColor</c> first; falls back to <see cref="SystemColors.Window"/>.
        /// </summary>
        private static ThemeVariant DetectHostVariant()
        {
            try
            {
                var color = Microsoft.VisualStudio.PlatformUI.VSColorTheme.GetThemedColor(
                    Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolWindowBackgroundColorKey);
                return ClassifyByLuminance(color.R, color.G, color.B);
            }
            catch
            {
                // Fallback — system color (less accurate when OS theme differs from VS theme).
                try
                {
                    var bg = SystemColors.WindowColor; // System.Windows.SystemColors → Media.Color
                    return ClassifyByLuminance(bg.R, bg.G, bg.B);
                }
                catch
                {
                    return ThemeVariant.Light;
                }
            }
        }

        private static ThemeVariant ClassifyByLuminance(byte r, byte g, byte b)
        {
            // Luminance via the standard ITU-R BT.601 weights.
            var lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
            return lum < 0.5 ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        // -------------------------------------------------------------------
        // Dispatcher helper
        // -------------------------------------------------------------------

        private static void DispatchToUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.BeginInvoke(action);
            }
        }
    }
}
