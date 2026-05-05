using System;
using System.Windows;
using System.Windows.Interop;

namespace AkmlSql.Shell.Shared.Ui.Theme
{
    /// <summary>
    /// Base class for AKML-owned modal <see cref="Window"/> instances. Subclassing is preferred
    /// over manual <c>ThemeRegistry.Instance.AttachTo(this)</c> because it also handles the
    /// DTE-derived owner HWND and the default Background/Foreground references.
    ///
    /// Derived classes that need bespoke construction order (e.g., <c>SafetyWarningDialog</c>'s
    /// focus-on-Cancel discipline) may inherit <see cref="Window"/> directly and call
    /// <c>ThemeRegistry.Instance.AttachTo(this)</c> themselves.
    /// </summary>
    public class ThemeAwareWindow : Window
    {
        protected ThemeAwareWindow()
        {
            ThemeRegistry.Instance.AttachTo(this);

            this.SetResourceReference(BackgroundProperty, ThemeTokens.SurfaceCanvas);
            this.SetResourceReference(ForegroundProperty, ThemeTokens.TextPrimary);

            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Loaded += OnLoadedSetOwner;
        }

        private void OnLoadedSetOwner(object sender, RoutedEventArgs e)
        {
            // Set Owner from the DTE main window HWND so CenterOwner works and the modal parents
            // to the host's main window. Reference pattern from src/AkmlSql.Shell.Shared/History/HistoryDiffWindow.cs.
            // Silent no-op when DTE is unreachable (e.g., design-time host).
            if (Owner != null) return;
            try
            {
                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte?.MainWindow != null)
                {
                    var helper = new WindowInteropHelper(this);
                    helper.Owner = (IntPtr)dte.MainWindow.HWnd;
                }
            }
            catch
            {
                // DTE unavailable — accept the un-parented window rather than failing the dialog.
            }
        }
    }
}
