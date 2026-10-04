#nullable enable
using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using AkmlSql.Shell.Shared.Help;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Serilog;

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

        /// <summary>Spec 040 (X-03, contracts/ui.md §3): what F1 opens in this window.</summary>
        internal const string HelpTopic = F1HelpRegistrations.SqlHistoryTopic;

        /// <summary>
        /// Creates a new instance of the History tool window.
        /// </summary>
        public HistoryToolWindow() : base(null)
        {
            Caption = "SQL History";
            var control = new HistoryToolWindowControl();
            // F1 that reaches WPF (VS normally turns it into Help.F1Help first — see Initialize).
            HelpBinding.Attach(control, () => HelpTopic);
            Content = control;
        }

        /// <summary>
        /// Spec 040 (X-03, FR-062, research R26): VS turns F1 into the Help.F1Help command before
        /// WPF sees the key, and routes it to the active pane's command target first. Claiming
        /// the command here opens the SQL History topic instead of the host's own help.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            try
            {
                if (GetService(typeof(IMenuCommandService)) is OleMenuCommandService commands)
                {
                    commands.AddCommand(new MenuCommand(
                        (_, __) => F1HelpListener.Default.Open(HelpTopic),
                        new CommandID(VSConstants.GUID_VSStandardCommandSet97, (int)VSConstants.VSStd97CmdID.F1Help)));

                    // Spec 040 (HIS-11): F2 arrives as the host's Rename command the same way, so
                    // the list never saw the key and F2 renamed nothing.
                    foreach (var rename in new[]
                    {
                        new CommandID(VSConstants.GUID_VSStandardCommandSet97, (int)VSConstants.VSStd97CmdID.Rename),
                        new CommandID(VSConstants.VSStd2K, (int)VSConstants.VSStd2KCmdID.RENAME),
                    })
                    {
                        commands.AddCommand(new MenuCommand((_, __) => RenameFromKeyboard(), rename));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "HistoryToolWindow: F1 help command was not registered");
            }
        }

        /// <summary>
        /// Spec 040 (HIS-11): keys reach the pane here before the host turns them into commands.
        /// F2 is one SSMS keeps for itself, so the list never saw it; with the list focused it
        /// renames the selected query.
        /// </summary>
        protected override bool PreProcessMessage(ref System.Windows.Forms.Message m)
        {
            const int WM_KEYDOWN = 0x0100;
            const int VK_F2 = 0x71;
            if (m.Msg == WM_KEYDOWN && m.WParam.ToInt64() == VK_F2
                && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None
                && Content is HistoryToolWindowControl control && control.RenameSelectedFromKeyboard())
                return true;
            return base.PreProcessMessage(ref m);
        }

        private void RenameFromKeyboard()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Content is HistoryToolWindowControl control && !control.RenameSelectedFromKeyboard())
                Log.Debug("HistoryToolWindow: Rename ignored (the query list does not have focus)");
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
