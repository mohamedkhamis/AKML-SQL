using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor
{
    internal class CompletionCommandHandler : IOleCommandTarget
    {
        private readonly IWpfTextView _textView;
        private readonly IVsTextView _vsView;

        public IOleCommandTarget NextTarget { get; set; }

        public CompletionCommandHandler(IWpfTextView textView, IVsTextView vsView)
        {
            _textView = textView;
            _vsView = vsView;
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            return NextTarget?.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText)
                ?? (int)Constants.OLECMDERR_E_NOTSUPPORTED;
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            if (pguidCmdGroup == VSConstants.VSStd2K)
            {
                switch ((VSConstants.VSStd2KCmdID)nCmdID)
                {
                    case VSConstants.VSStd2KCmdID.TYPECHAR:
                        var typedChar = (char)(ushort)System.Runtime.InteropServices.Marshal.GetObjectForNativeVariant(pvaIn);

                        // Pass to next handler first
                        var result = NextTarget.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);

                        // T064: Trigger completion after dot
                        if (typedChar == '.')
                        {
                            TriggerCompletion(typedChar);
                        }
                        // T064: Trigger signature help after open parenthesis
                        else if (typedChar == '(')
                        {
                            TriggerSignatureHelp();
                        }
                        // T064: Update active parameter after comma
                        else if (typedChar == ',')
                        {
                            UpdateSignatureHelpActiveParameter();
                        }

                        return result;

                    case VSConstants.VSStd2KCmdID.RETURN:
                    case VSConstants.VSStd2KCmdID.TAB:
                        // Commit completion if active
                        break;

                    case VSConstants.VSStd2KCmdID.CANCEL:
                        // Dismiss completion if active
                        break;
                }
            }

            return NextTarget?.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut)
                ?? VSConstants.S_OK;
        }

        private void TriggerCompletion(char triggerChar)
        {
            // Will be wired to PipeRpcClient in T039
            Log.Debug("Completion triggered by '{Char}' at offset {Offset}",
                triggerChar, _textView.Caret.Position.BufferPosition.Position);
        }

        /// <summary>
        /// T064: Trigger signature help when '(' is typed.
        /// </summary>
        private void TriggerSignatureHelp()
        {
            // Will be wired to PipeRpcClient for SignatureRequest/SignatureResponse
            Log.Debug("Signature help triggered at offset {Offset}",
                _textView.Caret.Position.BufferPosition.Position);
        }

        /// <summary>
        /// T064: Update active parameter when ',' is typed inside a function call.
        /// </summary>
        private void UpdateSignatureHelpActiveParameter()
        {
            // Will be wired to update the active parameter in the current signature help session
            Log.Debug("Signature help parameter update at offset {Offset}",
                _textView.Caret.Position.BufferPosition.Position);
        }
    }
}
