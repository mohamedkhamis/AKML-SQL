#nullable enable
using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Tool window pane for the SQL History panel. Hosts the <see cref="HistoryToolWindowControl"/>
    /// WPF UserControl within a VS/SSMS dockable tool window.
    /// </summary>
    [Guid(ToolWindowGuid)]
    public class HistoryToolWindow : ToolWindowPane
    {
        /// <summary>
        /// Unique GUID for the History tool window. Used by VS to persist window layout state.
        /// </summary>
        public const string ToolWindowGuid = "A1B2C3D4-7777-8888-9999-AABBCCDDEEFF";

        /// <summary>
        /// Creates a new instance of the History tool window.
        /// </summary>
        public HistoryToolWindow() : base(null)
        {
            Caption = "SQL History";
            Content = new HistoryToolWindowControl();
        }

        /// <summary>
        /// Called after the tool window frame is created. Sets a reasonable default size
        /// so the window doesn't open tiny on first use.
        /// </summary>
        public override void OnToolWindowCreated()
        {
            base.OnToolWindowCreated();
            try
            {
                if (Frame is IVsWindowFrame frame)
                {
                    // Set default size (800x500) when the window is first created.
                    // VS persists layout after the user resizes, so this only affects first open.
                    frame.SetFramePos(VSSETFRAMEPOS.SFP_fSize, Guid.Empty, 0, 0, 800, 500);
                }
            }
            catch
            {
                // Non-critical — window will open at VS's default size
            }
        }
    }
}
