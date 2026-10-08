#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.StatusBar;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T090, STY-09) — short status-bar messages ("Formatted with …") give the status
    /// bar back afterwards, never paint over an open-transaction warning, and the active-style text
    /// follows the "Show active style in status bar" setting.
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class StatusBarTransientTests : AppDataIsolatedTest, IDisposable
    {
        private readonly List<string> _texts = new List<string>();
        private readonly List<Action> _timers = new List<Action>();

        public StatusBarTransientTests() : base("akmlsql-statusbar-test-")
        {
            StatusBarManager.TextSinkOverride = _texts.Add;
            StatusBarManager.DelayOverride = (_, action) => _timers.Add(action);
            StatusBarManager.ResetForTests();
        }

        public override void Dispose()
        {
            StatusBarManager.TextSinkOverride = null;
            StatusBarManager.DelayOverride = null;
            StatusBarManager.ResetForTests();
            base.Dispose();
        }

        private string Shown => _texts.Last();

        [Fact]
        public void A_transient_message_shows_then_restores_the_idle_text()
        {
            StatusBarManager.SetLoaded(null!, "Mine");
            var idle = Shown;

            StatusBarManager.ShowTransient(null!, "Formatted with 'Mine'", 4);
            Assert.Equal("Formatted with 'Mine'", Shown);

            Assert.Single(_timers);
            _timers[0]();
            Assert.Equal(idle, Shown);
        }

        [Fact]
        public void A_newer_transient_is_not_cut_short_by_the_older_ones_timer()
        {
            StatusBarManager.SetLoaded(null!, null);

            StatusBarManager.ShowTransient(null!, "first", 4);
            StatusBarManager.ShowTransient(null!, "second", 4);
            _timers[0]();

            Assert.Equal("second", Shown);
        }

        [Fact]
        public void The_idle_text_is_not_restored_over_an_open_transaction_warning()
        {
            StatusBarManager.SetLoaded(null!, "Mine");

            StatusBarManager.ShowTransient(null!, "Formatted with 'Mine'", 4);
            StatusBarManager.SetTransactionIndicator(null!, "OPEN TRANSACTION (0m 05s)");
            _timers[0]();

            Assert.Equal("OPEN TRANSACTION (0m 05s)", Shown);
        }

        [Fact]
        public void A_transient_does_not_replace_an_open_transaction_warning()
        {
            StatusBarManager.SetTransactionIndicator(null!, "OPEN TRANSACTION (0m 05s)");

            StatusBarManager.ShowTransient(null!, "Formatted with 'Mine'", 4);

            Assert.Equal("OPEN TRANSACTION (0m 05s)", Shown);
        }

        [Fact]
        public void Set_active_profile_does_nothing_when_the_setting_is_off()
        {
            var settings = ConfigManager.Load();
            settings.Formatter.ShowProfileInStatusBar = false;
            ConfigManager.Save(settings);
            StatusBarManager.ResetForTests();

            StatusBarManager.SetActiveProfile(null!, "Mine");

            Assert.Empty(_texts);
        }

        [Fact]
        public void Set_active_profile_names_the_style_when_the_setting_is_on()
        {
            StatusBarManager.SetActiveProfile(null!, "Mine");

            Assert.Contains("Format: Mine", Shown);
        }
    }
}
