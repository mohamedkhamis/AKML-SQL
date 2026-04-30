using System.Windows;
using System.Windows.Controls;

namespace AkmlSql.Shell.Shared.Ui.Theme
{
    /// <summary>
    /// Base class for AKML-owned <see cref="UserControl"/> instances (typically dockable tool-window content).
    /// Auto-merges <see cref="ThemeRegistry.Resources"/> into the control's <c>Resources</c> and applies
    /// default Background/Foreground references — same as <see cref="ThemeAwareWindow"/> minus the
    /// DTE owner HWND logic (tool-window panes don't have an owner concept).
    /// </summary>
    public class ThemeAwareUserControl : UserControl
    {
        protected ThemeAwareUserControl()
        {
            ThemeRegistry.Instance.AttachTo(this);

            this.SetResourceReference(BackgroundProperty, ThemeTokens.SurfacePanel);
            this.SetResourceReference(ForegroundProperty, ThemeTokens.TextPrimary);
        }
    }
}
