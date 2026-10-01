#nullable enable
using System;
using System.ComponentModel.Design;
using System.Windows;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>What the Options loop does after a window closes.</summary>
    internal enum OptionsLoopStep
    {
        /// <summary>A theme was picked: reopen under the new brushes with the working copy.</summary>
        Reopen,
        /// <summary>OK: save and notify.</summary>
        Save,
        /// <summary>Cancel: write nothing and restore the theme the dialog opened with.</summary>
        Cancel,
    }

    /// <summary>
    /// Spec 040 (T023) — the Options window as the loop sees it, so the loop can be tested with a
    /// scripted fake. <see cref="SettingsWindow"/> is the only production implementation.
    /// </summary>
    internal interface IOptionsDialog
    {
        string? InitialAgentId { get; set; }
        /// <summary>Spec 040 (T163): the label of an option to scroll to and focus once the window loads.</summary>
        string? InitialFocusLabel { get; set; }
        bool ShowDialog(string? initialPageKey);
        bool ThemeChangeRequested { get; }
        AppSettings WorkingCopy { get; }
        string? CurrentPageKey { get; }
        AppSettings GetSettings();
    }

    internal sealed class OptionsCommand
    {
        private OptionsCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdOptions);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        public static OptionsCommand? Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new OptionsCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ShowOptions(null, null);
        }

        /// <summary>
        /// Spec 037 (US1, FR-017, research R8): the ONE open-Options-and-save path. Opens the
        /// settings dialog (deep-linked to <paramref name="pageKey"/> and, for the AI Assistance
        /// page, to <paramref name="agentId"/> when given), then on OK performs
        /// <c>ConfigManager.Save</c> AND the <see cref="MessageTypes.AnalysisSettingsChanged"/>
        /// notification that makes the engine drop its settings cache — the mechanism FR-019's
        /// "no restart" stands on. Callers (the chat panel's onboarding card, the no-agent gate)
        /// must use this rather than saving settings themselves, or the engine keeps serving
        /// stale settings. Returns true when settings were saved.
        /// </summary>
        internal static bool ShowOptions(string? pageKey, string? agentId) =>
            ShowOptions(pageKey, agentId, null);

        /// <summary>
        /// Spec 040 (OPT-07, FR-053, T163): as <see cref="ShowOptions(string?, string?)"/>, and once
        /// the window has loaded it scrolls to, flashes and focuses the option labelled
        /// <paramref name="focusLabel"/> on that page — the Command Palette's way into an option it
        /// can't toggle in place. A label no page shows just opens the page.
        /// </summary>
        internal static bool ShowOptions(string? pageKey, string? agentId, string? focusLabel)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return RunOptionsLoop(pageKey, agentId, focusLabel);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open settings dialog");
                MessageBox.Show(
                    "Failed to load settings: " + ex.Message,
                    Constants.ProductName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Spec 040 (OPT-02, FR-004) — open, and reopen after a theme pick, until OK or Cancel.
        /// A theme pick reopens the window with the previous window's working copy on the same
        /// page; nothing reaches disk until OK. Cancel restores the theme the dialog opened with.
        /// <paramref name="focusLabel"/> goes to the first window only — after a theme pick the
        /// reopened window stays where the user was.
        /// </summary>
        internal static bool RunOptionsLoop(string? pageKey, string? agentId, string? focusLabel = null)
        {
            var settings = ConfigManager.Load();
            var originalTheme = settings.Theme;

            while (true)
            {
                var window = CreateDialog(settings);
                window.InitialAgentId = agentId;
                window.InitialFocusLabel = focusLabel;
                bool ok = window.ShowDialog(pageKey);

                switch (NextStep(ok, window.ThemeChangeRequested))
                {
                    case OptionsLoopStep.Reopen:
                        settings = window.WorkingCopy;
                        pageKey = window.CurrentPageKey ?? pageKey;
                        focusLabel = null;
                        continue;

                    case OptionsLoopStep.Save:
                        var saved = window.GetSettings();
                        SaveAndNotify(saved);
                        ApplyThemePreference(saved.Theme);
                        return true;

                    default:
                        ApplyThemePreference(originalTheme);
                        return false;
                }
            }
        }

        /// <summary>The loop's decision after a window closes. A theme pick wins over OK/Cancel.</summary>
        internal static OptionsLoopStep NextStep(bool dialogResult, bool themeChangeRequested)
        {
            if (themeChangeRequested) return OptionsLoopStep.Reopen;
            return dialogResult ? OptionsLoopStep.Save : OptionsLoopStep.Cancel;
        }

        /// <summary>
        /// Spec 040 (T023) test seam: when set, the loop creates its windows through this instead
        /// of <see cref="SettingsWindow"/>. Production code never sets it.
        /// </summary>
        internal static Func<AppSettings, IOptionsDialog>? WindowFactoryOverride { get; set; }

        /// <summary>
        /// Spec 040 (T023) test seam: when set, theme preferences go here instead of
        /// <see cref="Ui.Theme.ThemeRegistry"/> (whose shared dictionary can't be switched from a
        /// unit test's thread). Production code never sets it.
        /// </summary>
        internal static Action<string>? ThemePreferenceOverride { get; set; }

        /// <summary>Applies a theme preference to every AKML surface (Options preview, OK, Cancel).</summary>
        internal static void ApplyThemePreference(string? preference)
        {
            var pref = string.IsNullOrWhiteSpace(preference) ? "light" : preference!;
            if (ThemePreferenceOverride != null) ThemePreferenceOverride(pref);
            else Ui.Theme.ThemeRegistry.Instance.SetPreference(pref);
        }

        private static IOptionsDialog CreateDialog(AppSettings settings) =>
            WindowFactoryOverride != null ? WindowFactoryOverride(settings) : new SettingsWindow(settings);

        /// <summary>
        /// Test seam (spec 037 review): when set, <see cref="SaveAndNotify"/>'s
        /// <see cref="MessageTypes.AnalysisSettingsChanged"/> notification goes through this
        /// accessor instead of <see cref="EngineLifecycle.Manager"/>, so the notification is
        /// assertable without a pipe or an engine. Production code never sets it.
        /// </summary>
        internal static IRpcClientAccessor? TestRpcAccessor { get; set; }

        /// <summary>
        /// Spec 037 (US3, FR-040): the save-and-notify half of the shared path, for callers that
        /// change settings WITHOUT opening the dialog — the chat panel's agent picker persisting
        /// <c>FeatureAgents.Chat</c>. Keeping the body here means there is still exactly one
        /// save path: <c>ConfigManager.Save</c>, the tab-color repaint, and the
        /// <see cref="MessageTypes.AnalysisSettingsChanged"/> notification that makes the engine
        /// drop its settings cache. The panel must never call <c>ConfigManager.Save</c> itself.
        /// </summary>
        internal static void SaveAndNotify(AppSettings settings)
        {
            ConfigManager.Save(settings);
            Log.Information("Settings saved successfully.");

            // FR-042: Live re-render tab colors after settings change
            try { Tabs.TabColoringManager.RepaintAllTabs(); } catch { }

            // Spec 040 (OPT-01): "Show in Error List" applies to open documents at once.
            try { Analysis.ErrorListReporter.ReapplyAll(); }
            catch (Exception ex) { Log.Debug(ex, "Options: Error List re-apply failed"); }

            // Spec 040 (T108): the status bar follows "Show active style in status bar" at once,
            // and (T104) the Active Style menu shows a style chosen on the Format page.
            try
            {
                StatusBar.StatusBarManager.ApplyStatusBarSetting(
                    settings.Formatter.ShowProfileInStatusBar, settings.Formatter.ActiveProfile);
            }
            catch (Exception ex) { Log.Debug(ex, "Options: status bar update failed"); }
            Formatting.ActiveStyleCache.Instance.RefreshNow();

            // T066: Notify the engine to reload its settings cache (fire-and-forget)
            var accessor = TestRpcAccessor;
            if (accessor != null)
            {
                if (accessor.IsConnected)
                    _ = accessor.SendNotificationAsync(MessageTypes.AnalysisSettingsChanged, new { });
                return;
            }

            var client = EngineLifecycle.Manager?.Client;
            if (client != null && client.IsConnected)
            {
                _ = client.SendNotificationAsync(
                    MessageTypes.AnalysisSettingsChanged,
                    new { });
            }
        }
    }
}
