#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Shell.Shared.Commands;
using Xunit;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T020, OPT-02, FR-004) — the Options loop: a theme pick reopens the window with
    /// the working copy on the same page and writes nothing; OK saves once and keeps the picked
    /// theme; Cancel writes nothing and restores the theme the dialog opened with. Driven through
    /// <see cref="OptionsCommand.WindowFactoryOverride"/> with scripted fake dialogs
    /// (<see cref="OptionsCommand.RunOptionsLoop"/> is <c>ShowOptions</c> minus the VS UI-thread check).
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class OptionsReopenLoopTests : AppDataIsolatedTest
    {
        private readonly FakeRpcClientAccessor _rpc = new FakeRpcClientAccessor();
        private readonly List<FakeDialog> _dialogs = new List<FakeDialog>();
        private readonly Queue<Func<FakeDialog, bool>> _script = new Queue<Func<FakeDialog, bool>>();
        private static readonly List<string> ThemeCalls = new List<string>();

        public OptionsReopenLoopTests() : base("akml-options-loop-")
        {
            OptionsCommand.TestRpcAccessor = _rpc;
            OptionsCommand.WindowFactoryOverride = settings =>
            {
                var dialog = new FakeDialog(settings, _script.Dequeue());
                _dialogs.Add(dialog);
                return dialog;
            };
            ThemeCalls.Clear();
            OptionsCommand.ThemePreferenceOverride = ThemeCalls.Add;
            ConfigManager.Save(new AppSettings { Theme = "light" });
        }

        public override void Dispose()
        {
            OptionsCommand.TestRpcAccessor = null;
            OptionsCommand.WindowFactoryOverride = null;
            OptionsCommand.ThemePreferenceOverride = null;
            base.Dispose();
        }

        private sealed class FakeDialog : IOptionsDialog
        {
            private readonly Func<FakeDialog, bool> _onShow;

            public FakeDialog(AppSettings settings, Func<FakeDialog, bool> onShow)
            {
                Settings = settings;
                _onShow = onShow;
            }

            public AppSettings Settings { get; }
            public string? ShownPage { get; private set; }
            public string? InitialAgentId { get; set; }
            public bool ThemeChangeRequested { get; set; }
            public string? CurrentPageKey { get; set; }
            public AppSettings WorkingCopy => Settings;
            public AppSettings GetSettings() => Settings;

            public bool ShowDialog(string? initialPageKey)
            {
                ShownPage = initialPageKey;
                return _onShow(this);
            }
        }

        /// <summary>What the real window does when the user picks Dark on the History page after an edit.</summary>
        private static bool PickDarkOnHistory(FakeDialog d)
        {
            d.Settings.IntelliSense.MaxSuggestions = 77;
            d.Settings.Theme = "dark";
            OptionsCommand.ApplyThemePreference("dark"); // the window's live preview
            d.CurrentPageKey = "History";
            d.ThemeChangeRequested = true;
            return true;
        }

        private static byte[] ConfigBytes() => File.ReadAllBytes(Constants.ConfigFilePath);

        [Theory]
        [InlineData(true, true, "Reopen")]
        [InlineData(false, true, "Reopen")]
        [InlineData(true, false, "Save")]
        [InlineData(false, false, "Cancel")]
        public void NextStep_maps_the_close_to_an_action(bool ok, bool themeChange, string expected)
            => Assert.Equal(expected, OptionsCommand.NextStep(ok, themeChange).ToString());

        [StaFact]
        public void A_theme_pick_reopens_with_the_working_copy_on_the_same_page_and_writes_nothing()
        {
            var before = ConfigBytes();
            _script.Enqueue(PickDarkOnHistory);
            byte[]? duringSecond = null;
            _script.Enqueue(d => { duringSecond = ConfigBytes(); return false; });

            OptionsCommand.RunOptionsLoop("IntelliSense", null);

            Assert.Equal(2, _dialogs.Count);
            Assert.Same(_dialogs[0].WorkingCopy, _dialogs[1].Settings);
            Assert.Equal(77, _dialogs[1].Settings.IntelliSense.MaxSuggestions);
            Assert.Equal("IntelliSense", _dialogs[0].ShownPage);
            Assert.Equal("History", _dialogs[1].ShownPage);
            Assert.Equal(before, duringSecond);
        }

        [StaFact]
        public void OK_after_a_theme_pick_saves_once_and_keeps_the_theme()
        {
            _script.Enqueue(PickDarkOnHistory);
            _script.Enqueue(d => true);

            var saved = OptionsCommand.RunOptionsLoop(null, null);

            Assert.True(saved);
            Assert.Single(_rpc.Notifications, n => n.MessageType == MessageTypes.AnalysisSettingsChanged);
            var onDisk = ConfigManager.Load();
            Assert.Equal("dark", onDisk.Theme);
            Assert.Equal(77, onDisk.IntelliSense.MaxSuggestions);
            Assert.Equal(new[] { "dark", "dark" }, ThemeCalls); // preview, then the saved theme
        }

        [StaFact]
        public void Cancel_after_a_theme_pick_writes_nothing_and_restores_the_theme()
        {
            var before = ConfigBytes();
            _script.Enqueue(PickDarkOnHistory);
            _script.Enqueue(d => false);

            var saved = OptionsCommand.RunOptionsLoop(null, null);

            Assert.False(saved);
            Assert.Empty(_rpc.Notifications);
            Assert.Equal(before, ConfigBytes());
            Assert.Equal(new[] { "dark", "light" }, ThemeCalls); // preview, then the original theme restored
        }
    }
}
