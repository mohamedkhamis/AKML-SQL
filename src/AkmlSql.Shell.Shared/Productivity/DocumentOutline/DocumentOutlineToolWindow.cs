#nullable enable
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace AkmlSql.Shell.Shared.Productivity.DocumentOutline
{
    /// <summary>
    /// Tool window pane for the Document Outline panel.
    /// Hosts the <see cref="DocumentOutlineControl"/> WPF control within a VS/SSMS dockable tool window.
    /// </summary>
    [Guid(ToolWindowGuid)]
    public class DocumentOutlineToolWindow : ToolWindowPane
    {
        /// <summary>
        /// Unique GUID for the Document Outline tool window.
        /// Used by VS to persist window layout state.
        /// </summary>
        public const string ToolWindowGuid = "A1B2C3D4-8888-9999-AAAA-BBCCDDEE0001";

        private readonly DocumentOutlineControl _control;

        /// <summary>
        /// Creates a new instance of the Document Outline tool window.
        /// </summary>
        public DocumentOutlineToolWindow() : base(null)
        {
            Caption = "Document Outline";
            _control = new DocumentOutlineControl();
            Content = _control;
        }

        /// <summary>
        /// Gets the ViewModel for external binding (e.g., from TextViewCreationListener).
        /// </summary>
        public DocumentOutlineViewModel ViewModel => _control.ViewModel;
    }
}
