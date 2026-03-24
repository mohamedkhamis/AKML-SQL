#nullable enable
using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

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
    }
}
