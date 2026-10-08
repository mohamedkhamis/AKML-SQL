#nullable enable
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serilog;

namespace AkmlSql.Shell.Shared.Ui
{
    /// <summary>
    /// Spec 040 (T182, X-02, research R27) — gives AKML windows and forms the AKML icon instead of
    /// the host's, from the embedded <c>Resources/akml.ico</c> (a copy of the installer's icon).
    /// <see cref="Theme.ThemeAwareWindow"/> applies it to every window derived from it; other WPF
    /// windows and the WinForms forms call <see cref="Apply(Window)"/> / <see cref="Apply(System.Windows.Forms.Form)"/>.
    /// A missing or unreadable resource leaves the host's icon in place — never an error.
    /// </summary>
    internal static class WindowIcon
    {
        /// <summary>
        /// The manifest name of the embedded icon. Fixed with <c>LogicalName</c> in the projitems,
        /// so it is the same in every project that imports the shared sources.
        /// </summary>
        internal const string ResourceName = "AkmlSql.Shell.Shared.Resources.akml.ico";

        private static readonly Lazy<byte[]?> IconBytes = new Lazy<byte[]?>(ReadIconBytes);
        private static readonly Lazy<System.Drawing.Icon?> FormsIcon = new Lazy<System.Drawing.Icon?>(CreateFormsIcon);

        // A WPF icon decoder is a DispatcherObject: Window reads its frames on the window's own
        // thread, so each UI thread decodes its own copy (in SSMS that is one, the main thread).
        [ThreadStatic] private static ImageSource? _wpfIcon;
        [ThreadStatic] private static bool _wpfIconTried;

        /// <summary>The AKML icon for a WPF window on the calling thread, or null when it can't be read.</summary>
        internal static ImageSource? Source
        {
            get
            {
                if (!_wpfIconTried)
                {
                    _wpfIconTried = true;
                    _wpfIcon = CreateWpfIcon();
                }
                return _wpfIcon;
            }
        }

        /// <summary>The AKML icon for a WinForms form, or null when it can't be read.</summary>
        internal static System.Drawing.Icon? FormIcon => FormsIcon.Value;

        /// <summary>Sets the AKML icon on <paramref name="window"/>, unless it already has one.</summary>
        internal static void Apply(Window window)
        {
            if (window == null || window.Icon != null) return;
            var icon = Source;
            if (icon != null) window.Icon = icon;
        }

        /// <summary>Sets the AKML icon on <paramref name="form"/>.</summary>
        internal static void Apply(System.Windows.Forms.Form form)
        {
            if (form == null) return;
            var icon = FormIcon;
            if (icon != null) form.Icon = icon;
        }

        private static byte[]? ReadIconBytes()
        {
            try
            {
                using var stream = typeof(WindowIcon).Assembly.GetManifestResourceStream(ResourceName);
                if (stream == null)
                {
                    Log.Warning("WindowIcon: embedded resource {Resource} not found", ResourceName);
                    return null;
                }
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                return copy.ToArray();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "WindowIcon: failed to read {Resource}", ResourceName);
                return null;
            }
        }

        private static ImageSource? CreateWpfIcon()
        {
            var bytes = IconBytes.Value;
            if (bytes == null) return null;
            try
            {
                // An icon decoder keeps every frame, so Window picks the best size for the title
                // bar and the taskbar.
                var decoder = new IconBitmapDecoder(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                return decoder.Frames.Count > 0 ? decoder.Frames[0] : null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "WindowIcon: failed to decode the WPF icon");
                return null;
            }
        }

        private static System.Drawing.Icon? CreateFormsIcon()
        {
            var bytes = IconBytes.Value;
            if (bytes == null) return null;
            try
            {
                return new System.Drawing.Icon(new MemoryStream(bytes));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "WindowIcon: failed to create the WinForms icon");
                return null;
            }
        }
    }
}
