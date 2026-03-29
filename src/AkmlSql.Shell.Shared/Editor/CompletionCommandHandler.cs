using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor
{
    internal class CompletionCommandHandler : IOleCommandTarget
    {
        private readonly IWpfTextView _textView;
        private ICompletionBroker _broker;
        private ICompletionSession _activeSession;

        public IOleCommandTarget NextTarget { get; set; }

        public CompletionCommandHandler(IWpfTextView textView, IVsTextView vsView)
        {
            _textView = textView;
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            return NextTarget?.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText)
                ?? (int)Constants.OLECMDERR_E_NOTSUPPORTED;
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdId, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            if (pguidCmdGroup == VSConstants.VSStd2K)
            {
                switch ((VSConstants.VSStd2KCmdID)nCmdId)
                {
                    case VSConstants.VSStd2KCmdID.TYPECHAR:
                        var typedChar = (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);

                        // Pass to next handler first (let VS insert the character)
                        var result = NextTarget.Exec(ref pguidCmdGroup, nCmdId, nCmdexecopt, pvaIn, pvaOut);

                        // Trigger completion after typing
                        if (typedChar == '.')
                        {
                            TriggerCompletionSession();
                        }
                        else if (char.IsLetter(typedChar) || typedChar == '_' || typedChar == '@')
                        {
                            // Auto-trigger on letters — like SQL Prompt
                            TriggerOrFilterCompletion();
                        }
                        else if (typedChar == ' ' || typedChar == '(' || typedChar == ')')
                        {
                            DismissCompletion();
                        }

                        return result;

                    case VSConstants.VSStd2KCmdID.RETURN:
                    case VSConstants.VSStd2KCmdID.TAB:
                        if (_activeSession != null && !_activeSession.IsDismissed)
                        {
                            if (_activeSession.SelectedCompletionSet?.SelectionStatus?.IsSelected == true)
                            {
                                _activeSession.Commit();
                                return VSConstants.S_OK; // Don't pass to next handler
                            }
                        }
                        break;

                    case VSConstants.VSStd2KCmdID.CANCEL:
                        DismissCompletion();
                        break;

                    // Ctrl+Space (complete word)
                    case VSConstants.VSStd2KCmdID.COMPLETEWORD:
                    case VSConstants.VSStd2KCmdID.SHOWMEMBERLIST:
                        TriggerCompletionSession();
                        return VSConstants.S_OK;
                }
            }

            return NextTarget?.Exec(ref pguidCmdGroup, nCmdId, nCmdexecopt, pvaIn, pvaOut)
                ?? VSConstants.S_OK;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void TriggerCompletionSession()
        {
            try
            {
                DismissCompletion();
                var broker = GetBroker();
                if (broker == null) return;

                _activeSession = broker.TriggerCompletion(_textView);
                if (_activeSession != null)
                {
                    _activeSession.Dismissed += (s, e) => _activeSession = null;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to trigger completion");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void TriggerOrFilterCompletion()
        {
            try
            {
                if (_activeSession != null && !_activeSession.IsDismissed)
                {
                    // Session exists — let VS filter it
                    _activeSession.Filter();
                    return;
                }

                // No active session — trigger new one
                var broker = GetBroker();
                if (broker == null) return;

                _activeSession = broker.TriggerCompletion(_textView);
                if (_activeSession != null)
                {
                    _activeSession.Dismissed += (s, e) => _activeSession = null;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to trigger/filter completion");
            }
        }

        private void DismissCompletion()
        {
            if (_activeSession != null && !_activeSession.IsDismissed)
            {
                _activeSession.Dismiss();
            }
            _activeSession = null;
        }

        private ICompletionBroker GetBroker()
        {
            if (_broker != null) return _broker;
            try
            {
                var componentModel = (Microsoft.VisualStudio.ComponentModelHost.IComponentModel)
                    Microsoft.VisualStudio.Shell.Package.GetGlobalService(
                        typeof(Microsoft.VisualStudio.ComponentModelHost.SComponentModel));
                _broker = componentModel?.GetService<ICompletionBroker>();
            }
            catch { }
            return _broker;
        }
    }
}
