#nullable enable
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AkmlSql.Shell.Shared.Update;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 036 US5 / FR-039 + FR-005 safety convention: the pre-install confirmation names the
    /// new version and the applications that must close, Cancel is <c>IsCancel</c> and holds the
    /// initial focus, and the proceed button is deliberately not the default. Declining installs
    /// nothing and retains the offer (spec scenario 4a).
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class UpdateInstallConfirmDialogTests
    {
        [StaFact]
        public void Cancel_is_the_cancel_button_and_proceed_is_not_the_default()
        {
            var dlg = UpdateInstallConfirmDialog.CreateForUpdate("1.26.0903.0900");

            var cancel = LogicalTree.Descendants<Button>(dlg).Single(b => b.IsCancel);
            var proceed = LogicalTree.Descendants<Button>(dlg).Single(b => (string)b.Content == "Install now");

            Assert.False(proceed.IsDefault);
            Assert.False(proceed.IsCancel);
            Assert.Equal("Cancel", (string)cancel.Content);
        }

        [StaFact]
        public void Dialog_names_the_version_and_the_applications_that_must_close()
        {
            var dlg = UpdateInstallConfirmDialog.CreateForUpdate("1.26.0903.0900");

            var text = string.Join("\n",
                LogicalTree.Descendants<TextBlock>(dlg).Select(t => t.Text));

            Assert.Contains("1.26.0903.0900", text);
            Assert.Contains("SQL Server Management Studio", text);
            Assert.Contains("Visual Studio", text);
        }

        [StaFact]
        public void Cancel_holds_the_initial_focus_once_loaded()
        {
            var dlg = UpdateInstallConfirmDialog.CreateForUpdate("1.26.0903.0900");
            PositionOffScreen(dlg);
            try
            {
                dlg.Show();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Input);

                var cancel = LogicalTree.Descendants<Button>(dlg).Single(b => b.IsCancel);
                Assert.True(cancel.IsFocused, "Cancel must hold initial focus so Enter/Space cannot proceed");
            }
            finally
            {
                dlg.Close();
            }
        }

        [StaFact]
        public void Clicking_cancel_declines_and_clicking_install_proceeds()
        {
            var dlg = UpdateInstallConfirmDialog.CreateForUpdate("1.26.0903.0900");
            PositionOffScreen(dlg);
            try
            {
                dlg.Show();

                var cancel = LogicalTree.Descendants<Button>(dlg).Single(b => b.IsCancel);
                cancel.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.False(dlg.Outcome);
            }
            finally
            {
                dlg.Close();
            }

            var dlg2 = UpdateInstallConfirmDialog.CreateForUpdate("1.26.0903.0900");
            PositionOffScreen(dlg2);
            try
            {
                dlg2.Show();

                var proceed = LogicalTree.Descendants<Button>(dlg2).Single(b => (string)b.Content == "Install now");
                proceed.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.True(dlg2.Outcome);
            }
            finally
            {
                dlg2.Close();
            }
        }

        private static void PositionOffScreen(Window window)
        {
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.Left = -10_000;
            window.Top = -10_000;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
        }
    }
}
