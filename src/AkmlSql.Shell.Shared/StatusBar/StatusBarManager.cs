using System;
using System.Windows.Threading;
using AkmlSql.Core.Config;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Serilog;

namespace AkmlSql.Shell.Shared.StatusBar
{
    internal static class StatusBarManager
    {
        /// <summary>Tracks whether we are currently displaying a transaction indicator.</summary>
        private static bool _transactionIndicatorActive;

        /// <summary>
        /// The current idle text, restored after a transient indicator clears. Spec 030 T021 /
        /// FR-006: when "show active style in status bar" is on this carries the active formatting
        /// style, so the user can always see which style Format SQL will apply.
        /// </summary>
        private static string _idleText = $"AKML SQL v{Core.Constants.RuntimeVersion}";

        /// <summary>Spec 040 (T108): a short message ("Formatted with …") is showing.</summary>
        private static bool _transientActive;

        /// <summary>Bumped per transient message, so an older message's timer never ends a newer one.</summary>
        private static int _transientGeneration;

        private static bool? _showProfileCached;
        private static DateTime _showProfileReadUtc;

        /// <summary>
        /// Test seam: receives every status-bar text instead of the VS status bar, and lets these
        /// methods run off the VS main thread. Null in the product.
        /// </summary>
        internal static Action<string>? TextSinkOverride { get; set; }

        /// <summary>Test seam: schedules the transient-message timeout. Null in the product (a DispatcherTimer).</summary>
        internal static Action<TimeSpan, Action>? DelayOverride { get; set; }

        /// <summary>Test seam: forgets the idle text, indicators and the cached setting.</summary>
        internal static void ResetForTests()
        {
            _idleText = $"AKML SQL v{Core.Constants.RuntimeVersion}";
            _transactionIndicatorActive = false;
            _transientActive = false;
            _transientGeneration++;
            _showProfileCached = null;
        }

        private static void EnsureUiThread()
        {
            if (TextSinkOverride == null) ThreadHelper.ThrowIfNotOnUIThread();
        }

        /// <summary>
        /// "Show active style in status bar" (Options › Format), read at most every 2 s — style
        /// switches can arrive in bursts (the Active Style menu, the Format Styles window).
        /// </summary>
        private static bool ShowProfileInStatusBar()
        {
            var now = DateTime.UtcNow;
            if (_showProfileCached is bool cached && now - _showProfileReadUtc < TimeSpan.FromSeconds(2))
                return cached;
            bool show;
            try { show = ConfigManager.Load().Formatter.ShowProfileInStatusBar; }
            catch (Exception ex)
            {
                Log.Debug(ex, "StatusBarManager: could not read the status-bar setting");
                show = true;
            }
            _showProfileCached = show;
            _showProfileReadUtc = now;
            return show;
        }

        /// <summary>The status bar service, or null outside VS.</summary>
        private static IVsStatusbar? Service()
        {
            if (TextSinkOverride != null) return null;
            try { return Package.GetGlobalService(typeof(SVsStatusbar)) as IVsStatusbar; }
            catch (Exception ex)
            {
                Log.Debug(ex, "StatusBarManager: status bar service unavailable");
                return null;
            }
        }

        public static void SetLoaded(IVsStatusbar statusBar) => SetLoaded(statusBar, null);

        /// <summary>
        /// Sets the idle status text, optionally annotated with the active formatting style
        /// (spec 030 T021). Pass <paramref name="activeProfile"/> = null for the plain version.
        /// </summary>
        public static void SetLoaded(IVsStatusbar statusBar, string? activeProfile)
        {
            EnsureUiThread();
            _idleText = BuildIdleText(activeProfile);
            if (!_transactionIndicatorActive && !_transientActive)
                SetText(statusBar, _idleText);
        }

        /// <summary>
        /// Updates the active-style portion of the idle text when the user switches styles
        /// (spec 030 T021 / FR-006). Repaints immediately unless a transient indicator is showing,
        /// in which case the new idle text is restored when that indicator clears. Spec 040 (T108):
        /// does nothing while "Show active style in status bar" is off.
        /// </summary>
        public static void SetActiveProfile(IVsStatusbar statusBar, string? activeProfile)
        {
            EnsureUiThread();
            if (!ShowProfileInStatusBar()) return;
            _idleText = BuildIdleText(activeProfile);
            if (!_transactionIndicatorActive && !_transientActive)
                SetText(statusBar, _idleText);
        }

        /// <summary>
        /// Spec 040 (T108) — applies the saved "Show active style in status bar" setting: shows
        /// the active style when it is on, the plain version text when it is off.
        /// </summary>
        public static void ApplyStatusBarSetting(IVsStatusbar statusBar, bool show, string? activeProfile)
        {
            EnsureUiThread();
            _showProfileCached = show;
            _showProfileReadUtc = DateTime.UtcNow;
            _idleText = BuildIdleText(show ? activeProfile : null);
            if (!_transactionIndicatorActive && !_transientActive)
                SetText(statusBar, _idleText);
        }

        /// <summary>
        /// Spec 040 (T108, STY-09) — shows <paramref name="text"/> for <paramref name="seconds"/>,
        /// then gives the status bar back to the idle text. An open-transaction warning outranks it:
        /// the message is not shown over one, and the idle text is not restored over one.
        /// </summary>
        public static void ShowTransient(IVsStatusbar? statusBar, string text, int seconds)
        {
            EnsureUiThread();
            if (_transactionIndicatorActive) return;

            var generation = ++_transientGeneration;
            _transientActive = true;
            SetText(statusBar, text);

            Schedule(TimeSpan.FromSeconds(Math.Max(1, seconds)), () =>
            {
                if (generation != _transientGeneration) return; // a newer message replaced this one
                _transientActive = false;
                if (!_transactionIndicatorActive) SetText(statusBar, _idleText);
            });
        }

        /// <summary><see cref="ApplyStatusBarSetting(IVsStatusbar, bool, string?)"/> on the VS status bar.</summary>
        public static void ApplyStatusBarSetting(bool show, string? activeProfile) =>
            ApplyStatusBarSetting(Service()!, show, activeProfile);

        /// <summary><see cref="SetActiveProfile(IVsStatusbar, string?)"/> on the VS status bar.</summary>
        public static void SetActiveProfile(string? activeProfile) => SetActiveProfile(Service()!, activeProfile);

        /// <summary><see cref="ShowTransient(IVsStatusbar?, string, int)"/> on the VS status bar.</summary>
        public static void ShowTransient(string text, int seconds) => ShowTransient(Service(), text, seconds);

        private static void Schedule(TimeSpan delay, Action action)
        {
            var hook = DelayOverride;
            if (hook != null) { hook(delay, action); return; }

            var timer = new DispatcherTimer { Interval = delay };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try { action(); }
                catch (Exception ex) { Log.Debug(ex, "StatusBarManager: transient restore failed"); }
            };
            timer.Start();
        }

        private static string BuildIdleText(string? activeProfile)
        {
            var version = $"AKML SQL v{Core.Constants.RuntimeVersion}";
            return string.IsNullOrWhiteSpace(activeProfile)
                ? version
                : $"{version} · Format: {activeProfile}";
        }

        public static void SetFailed(IVsStatusbar statusBar)
        {
            EnsureUiThread();
            SetText(statusBar, "AKML SQL [FAILED]");
        }

        /// <summary>
        /// Displays a transaction warning indicator in the status bar.
        /// Called by <see cref="Safety.TransactionMonitor"/> to show elapsed time.
        /// </summary>
        /// <param name="statusBar">The VS status bar service.</param>
        /// <param name="text">
        /// The text to display (e.g. <c>"OPEN TRANSACTION (2m 15s)"</c>).
        /// </param>
        public static void SetTransactionIndicator(IVsStatusbar statusBar, string text)
        {
            EnsureUiThread();
            SetText(statusBar, text);
            _transactionIndicatorActive = true;
            _transientActive = false; // the warning replaced any short message
        }

        /// <summary>
        /// Clears the transaction indicator from the status bar and restores the default text.
        /// </summary>
        /// <param name="statusBar">The VS status bar service.</param>
        public static void ClearTransactionIndicator(IVsStatusbar statusBar)
        {
            EnsureUiThread();
            if (!_transactionIndicatorActive) return;

            SetText(statusBar, _idleText);
            _transactionIndicatorActive = false;
        }

        private static void SetText(IVsStatusbar? statusBar, string text)
        {
            var sink = TextSinkOverride;
            if (sink != null) { sink(text); return; }
            if (statusBar == null) return;

            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                statusBar.IsFrozen(out int frozen);
                if (frozen != 0)
                {
                    statusBar.FreezeOutput(0);
                }

                statusBar.SetText(text);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to update status bar");
            }
        }
    }
}
