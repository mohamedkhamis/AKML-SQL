#nullable enable
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace AkmlSql.Shell.Shared.Ai
{
    /// <summary>
    /// T051: Tool window pane for the AI Chat panel.
    /// Hosts the <see cref="AiChatPanel"/> WPF control within a VS/SSMS dockable tool window.
    /// Docks next to the Output window by default.
    /// </summary>
    [Guid(ToolWindowGuid)]
    public class AiChatToolWindow : ToolWindowPane
    {
        /// <summary>
        /// Unique GUID for the AI Chat tool window.
        /// Used by VS to persist window layout state.
        /// </summary>
        public const string ToolWindowGuid = "A1B2C3D4-AAAA-BBBB-CCCC-DDEEFF000003";

        private readonly AiChatPanel _panel;

        /// <summary>
        /// Creates a new instance of the AI Chat tool window.
        /// </summary>
        public AiChatToolWindow() : base(null)
        {
            Caption = "AI Chat";
            _panel = new AiChatPanel();
            Content = _panel;
        }

        /// <summary>
        /// Spec 036 (US1, FR-027): refresh the editor-session binding as soon as the window is
        /// sited so the header reflects the current connection immediately instead of waiting for
        /// the panel's first poll tick.
        /// </summary>
        public override void OnToolWindowCreated()
        {
            base.OnToolWindowCreated();
            _panel.RefreshBinding();
        }
    }
}
