using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor.Completion
{
    /// <summary>
    /// Orchestrates the custom completion popup. Intercepts keystrokes via IOleCommandTarget,
    /// triggers debounced Engine RPC, updates the popup, handles commit/dismiss.
    /// Suppresses SSMS native IntelliSense when the AKML popup is active.
    /// </summary>
    internal sealed class CompletionController : IOleCommandTarget
    {
        private readonly IWpfTextView _textView;
        private readonly CompletionPopupAdornment _adornment;
        private readonly string _sessionId;
        private Timer _debounceTimer;
        private ICompletionBroker _broker;
        private string _filterText = string.Empty;
        private bool _fetchPending;

        public IOleCommandTarget NextTarget { get; set; }

        private const int DebounceMs = 150;

        public CompletionController(IWpfTextView textView, CompletionPopupAdornment adornment, string sessionId)
        {
            _textView = textView;
            _adornment = adornment;
            _sessionId = sessionId;
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
                var cmdId = (VSConstants.VSStd2KCmdID)nCmdId;

                switch (cmdId)
                {
                    case VSConstants.VSStd2KCmdID.TYPECHAR:
                    {
                        var typedChar = (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);

                        // Suppress native IntelliSense BEFORE letting VS handle the keystroke
                        SuppressNativeIntelliSense();

                        // Let VS insert the character
                        var result = NextTarget.Exec(ref pguidCmdGroup, nCmdId, nCmdexecopt, pvaIn, pvaOut);

                        // Suppress again after VS processes (it may trigger native IntelliSense)
                        SuppressNativeIntelliSense();

                        HandleTypedChar(typedChar);
                        return result;
                    }

                    case VSConstants.VSStd2KCmdID.RETURN:
                    case VSConstants.VSStd2KCmdID.TAB:
                        if (_adornment.Popup.IsOpen)
                        {
                            var item = _adornment.Popup.GetSelectedItem();
                            if (item != null)
                            {
                                CommitItem(item);
                                return VSConstants.S_OK; // Swallow the key
                            }
                        }
                        break;

                    case VSConstants.VSStd2KCmdID.CANCEL:
                        if (_adornment.Popup.IsOpen)
                        {
                            DismissPopup();
                            return VSConstants.S_OK;
                        }
                        break;

                    case VSConstants.VSStd2KCmdID.UP:
                        if (_adornment.Popup.IsOpen)
                        {
                            _adornment.Popup.MoveSelection(-1);
                            return VSConstants.S_OK;
                        }
                        break;

                    case VSConstants.VSStd2KCmdID.DOWN:
                        if (_adornment.Popup.IsOpen)
                        {
                            _adornment.Popup.MoveSelection(1);
                            return VSConstants.S_OK;
                        }
                        break;

                    case VSConstants.VSStd2KCmdID.BACKSPACE:
                    {
                        var result = NextTarget.Exec(ref pguidCmdGroup, nCmdId, nCmdexecopt, pvaIn, pvaOut);
                        HandleBackspace();
                        return result;
                    }

                    case VSConstants.VSStd2KCmdID.COMPLETEWORD:
                    case VSConstants.VSStd2KCmdID.SHOWMEMBERLIST:
                    case VSConstants.VSStd2KCmdID.AUTOCOMPLETE:
                        // Intercept ALL completion commands — prevent native IntelliSense
                        SuppressNativeIntelliSense();
                        TriggerCompletion();
                        return VSConstants.S_OK; // Don't pass to next handler
                }
            }

            return NextTarget?.Exec(ref pguidCmdGroup, nCmdId, nCmdexecopt, pvaIn, pvaOut)
                   ?? VSConstants.S_OK;
        }

        private void HandleTypedChar(char c)
        {
            if (c == '.')
            {
                // Dot: commit current selection (if any) + trigger new completion
                if (_adornment.Popup.IsOpen)
                {
                    var item = _adornment.Popup.GetSelectedItem();
                    if (item != null)
                    {
                        // Don't commit dot itself — the dot is already inserted
                        CommitItemBeforeDot(item);
                    }
                }
                _filterText = string.Empty;
                TriggerCompletion();
            }
            else if (char.IsLetter(c) || c == '_' || c == '@' || c == '#')
            {
                _filterText += c;
                if (_adornment.Popup.IsOpen)
                {
                    // Filter existing items
                    _adornment.Popup.SetFilter(_filterText);
                    _adornment.Reposition();
                }
                else
                {
                    // Start new completion
                    TriggerCompletionDebounced();
                }
            }
            else if (c == ' ' || c == '(' || c == ')' || c == ';' || c == ',')
            {
                DismissPopup();
            }
            else if (char.IsDigit(c))
            {
                if (_adornment.Popup.IsOpen)
                {
                    _filterText += c;
                    _adornment.Popup.SetFilter(_filterText);
                }
            }
        }

        private void HandleBackspace()
        {
            if (_filterText.Length > 0)
            {
                _filterText = _filterText.Substring(0, _filterText.Length - 1);
                if (_adornment.Popup.IsOpen)
                {
                    if (_filterText.Length == 0)
                    {
                        DismissPopup();
                    }
                    else
                    {
                        _adornment.Popup.SetFilter(_filterText);
                    }
                }
            }
            else
            {
                DismissPopup();
            }
        }

        private void TriggerCompletion()
        {
            _filterText = GetWordAtCaret();
            FetchAndShowCompletions();
        }

        private void TriggerCompletionDebounced()
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ =>
            {
                try
                {
                    _textView.VisualElement.Dispatcher.Invoke(() => FetchAndShowCompletions());
                }
                catch { /* UI might be disposed */ }
            }, null, DebounceMs, Timeout.Infinite);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void FetchAndShowCompletions()
        {
            try
            {
                var caretPos = _textView.Caret.Position.BufferPosition.Position;
                var docText = _textView.TextBuffer.CurrentSnapshot.GetText();

                // Check if we already have cached results (instant show)
                var cached = CompletionRpcHelper.GetCached(_sessionId);
                if (cached.Length > 0)
                {
                    _adornment.Popup.SetItems(cached);
                    _adornment.Popup.SetFilter(_filterText);
                    _adornment.Show();
                    _adornment.Reposition();
                }
                else
                {
                    // Show loading state for first request only
                    _adornment.Popup.ShowLoading();
                    _adornment.Show();
                }

                _fetchPending = true;

                // Fetch fresh results from Engine (background)
                // Use callback pattern instead of polling
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var client = Ipc.EngineLifecycle.Manager?.Client;
                        if (client == null || !client.IsConnected) return;

                        // Send document text
                        await client.SendNotificationAsync(
                            AkmlSql.Core.Ipc.MessageTypes.DocumentChanged,
                            new AkmlSql.Core.Ipc.Messages.DocumentChange
                            {
                                SessionId = _sessionId,
                                ChangeType = 0,
                                FullText = docText
                            });

                        await System.Threading.Tasks.Task.Delay(30);

                        // Request completions
                        var response = await client.SendRequestAsync<
                            AkmlSql.Core.Ipc.Messages.CompletionResponse,
                            AkmlSql.Core.Ipc.Messages.CompletionRequest>(
                            AkmlSql.Core.Ipc.MessageTypes.RequestCompletion,
                            new AkmlSql.Core.Ipc.Messages.CompletionRequest
                            {
                                SessionId = _sessionId,
                                CursorOffset = caretPos,
                                TriggerKind = 1
                            },
                            timeoutMs: 3000);

                        if (response?.Items != null && response.Items.Length > 0)
                        {
                            var models = new CompletionItemModel[response.Items.Length];
                            for (int i = 0; i < response.Items.Length; i++)
                            {
                                var item = response.Items[i];
                                models[i] = new CompletionItemModel
                                {
                                    DisplayText = item.DisplayText ?? string.Empty,
                                    InsertText = item.InsertText ?? item.DisplayText ?? string.Empty,
                                    SecondaryText = item.SecondaryText ?? string.Empty,
                                    ObjectType = item.ObjectType,
                                    SortPriority = item.SortPriority
                                };
                            }

                            // Update UI on dispatcher thread
                            _textView.VisualElement.Dispatcher.Invoke(() =>
                            {
                                if (!_fetchPending) return;
                                _fetchPending = false;
                                _adornment.Popup.SetItems(models);
                                _adornment.Popup.SetFilter(_filterText);
                                _adornment.Reposition();
                                Log.Debug("Completion: {Count} items shown", models.Length);
                            });
                        }
                        else
                        {
                            _textView.VisualElement.Dispatcher.Invoke(() =>
                            {
                                _fetchPending = false;
                                _adornment.Hide();
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Completion RPC failed");
                        try
                        {
                            _textView.VisualElement.Dispatcher.Invoke(() =>
                            {
                                _fetchPending = false;
                                _adornment.Hide();
                            });
                        }
                        catch { }
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to fetch completions");
                _fetchPending = false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void CommitItem(CompletionItemModel item)
        {
            try
            {
                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                var caretPos = _textView.Caret.Position.BufferPosition.Position;

                // Find word start to replace
                int start = caretPos;
                while (start > 0 && IsIdentifierChar(snapshot[start - 1]))
                    start--;

                var span = new Span(start, caretPos - start);
                _textView.TextBuffer.Replace(span, item.InsertText);

                DismissPopup();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to commit completion");
                DismissPopup();
            }
        }

        private void CommitItemBeforeDot(CompletionItemModel item)
        {
            try
            {
                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                var caretPos = _textView.Caret.Position.BufferPosition.Position;

                // Caret is AFTER the dot. Find the word before the dot.
                int dotPos = caretPos - 1; // Position of the dot
                if (dotPos < 0 || snapshot[dotPos] != '.') return;

                int start = dotPos;
                while (start > 0 && IsIdentifierChar(snapshot[start - 1]))
                    start--;

                // Replace word before dot with the selected item
                var span = new Span(start, dotPos - start);
                _textView.TextBuffer.Replace(span, item.InsertText);

                _adornment.Hide();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to commit before dot");
            }
        }

        private void DismissPopup()
        {
            _adornment.Hide();
            _filterText = string.Empty;
            _fetchPending = false;
        }

        private string GetWordAtCaret()
        {
            try
            {
                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                int pos = _textView.Caret.Position.BufferPosition.Position;
                int start = pos;
                while (start > 0 && IsIdentifierChar(snapshot[start - 1]))
                    start--;
                if (start < pos)
                    return snapshot.GetText(start, pos - start);
            }
            catch { }
            return string.Empty;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SuppressNativeIntelliSense()
        {
            // Always suppress — not just when our popup is open
            try
            {
                if (_broker == null)
                {
                    var componentModel = (Microsoft.VisualStudio.ComponentModelHost.IComponentModel)
                        Microsoft.VisualStudio.Shell.Package.GetGlobalService(
                            typeof(Microsoft.VisualStudio.ComponentModelHost.SComponentModel));
                    _broker = componentModel?.GetService<ICompletionBroker>();
                }
                _broker?.DismissAllSessions(_textView);
            }
            catch { /* non-critical */ }
        }

        private static bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '#' || c == '@';
        }
    }
}
