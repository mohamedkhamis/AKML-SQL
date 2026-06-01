using System;
using System.Collections.Generic;
using System.Linq;
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
        private Timer _quickInfoTimer;
        private ICompletionBroker _broker;
        private string _filterText = string.Empty;
        private bool _fetchPending;
        private bool _expectsObjects; // true when auto-triggered after FROM/JOIN — skip keyword-only results
        private bool _wildcardPending;
        private int _quickInfoVersion;
        private System.Windows.Threading.DispatcherTimer _suppressTimer;

        // Stored wildcard expansion context to avoid re-detection on commit
        private int _wildcardStarPos = -1;
        private string _wildcardQualifier = string.Empty;

        public IOleCommandTarget NextTarget { get; set; }

        private const int DebounceMs = 150;
        private const int QuickInfoDebounceMs = 300;

        public CompletionController(IWpfTextView textView, CompletionPopupAdornment adornment, string sessionId)
        {
            _textView = textView;
            _adornment = adornment;
            _sessionId = sessionId;

            // Subscribe to selection changes for QuickInfo debounce
            _adornment.Popup.SelectionChanged += OnCompletionSelectionChanged;

            // Wildcard popup: double-click commits (same as Tab/Enter) — SQL Prompt parity.
            _adornment.WildcardPopup.CommitRequested += CommitWildcardExpansion;

            // Timer that continuously suppresses native IntelliSense while our popup is open
            _suppressTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(20)
            };
            _suppressTimer.Tick += (s, e) =>
            {
                if (_adornment.Popup.IsOpen)
                    SuppressNativeIntelliSense();
                else
                    _suppressTimer.Stop();
            };
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            // Report completion commands as supported+enabled so SSMS doesn't skip our filter
            if (prgCmds != null && cCmds > 0)
            {
                if (pguidCmdGroup == VSConstants.VSStd2K)
                {
                    var cmdId = (VSConstants.VSStd2KCmdID)prgCmds[0].cmdID;
                    if (cmdId == VSConstants.VSStd2KCmdID.COMPLETEWORD ||
                        cmdId == VSConstants.VSStd2KCmdID.SHOWMEMBERLIST ||
                        cmdId == VSConstants.VSStd2KCmdID.AUTOCOMPLETE)
                    {
                        prgCmds[0].cmdf = (uint)(OLECMDF.OLECMDF_ENABLED | OLECMDF.OLECMDF_SUPPORTED);
                        return VSConstants.S_OK;
                    }
                }
                else if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97)
                {
                    var cmdId97 = (VSConstants.VSStd97CmdID)prgCmds[0].cmdID;
                    if (cmdId97 == (VSConstants.VSStd97CmdID)898)
                    {
                        prgCmds[0].cmdf = (uint)(OLECMDF.OLECMDF_ENABLED | OLECMDF.OLECMDF_SUPPORTED);
                        return VSConstants.S_OK;
                    }
                }
            }

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

                        // Space toggles checkbox in wildcard popup
                        if (typedChar == ' ' && _adornment.IsWildcardOpen)
                        {
                            _adornment.WildcardPopup.ToggleSelected();
                            return VSConstants.S_OK; // Don't insert space
                        }

                        // Suppress native IntelliSense BEFORE letting VS handle the keystroke
                        SuppressNativeIntelliSense();

                        // Let VS insert the character
                        var result = NextTarget.Exec(ref pguidCmdGroup, nCmdId, nCmdexecopt, pvaIn, pvaOut);

                        // Suppress again after VS processes (it may trigger native IntelliSense)
                        SuppressNativeIntelliSense();

                        HandleTypedChar(typedChar);
                        UpdatePopupCtrlTransparency();
                        return result;
                    }

                    case VSConstants.VSStd2KCmdID.RETURN:
                    case VSConstants.VSStd2KCmdID.TAB:
                        // Wildcard expansion popup is open — commit checked columns
                        if (_adornment.IsWildcardOpen)
                        {
                            CommitWildcardExpansion();
                            return VSConstants.S_OK;
                        }
                        // Completion popup is open — commit selected item
                        if (_adornment.Popup.IsOpen)
                        {
                            var item = _adornment.Popup.GetSelectedItem();
                            if (item != null)
                            {
                                CommitItem(item);
                                return VSConstants.S_OK; // Swallow the key
                            }
                        }
                        // Tab only: check for wildcard at cursor, then snippet abbreviation
                        if (cmdId == VSConstants.VSStd2KCmdID.TAB)
                        {
                            var wildcardInfo = DetectWildcardAtCursor();
                            if (wildcardInfo != null)
                            {
                                TriggerWildcardExpansion(wildcardInfo.Value.starPos, wildcardInfo.Value.qualifier);
                                return VSConstants.S_OK;
                            }

                            // Check for snippet abbreviation at cursor (e.g., "ssf" + Tab → expand snippet)
                            // Only attempt expansion if the word matches a known snippet shortcode
                            var wordAtCaret = GetWordAtCaret();
                            if (!string.IsNullOrEmpty(wordAtCaret) && wordAtCaret.Length >= 2
                                && IsKnownSnippetShortcode(wordAtCaret))
                            {
                                TryExpandSnippet(wordAtCaret);
                                return VSConstants.S_OK;
                            }
                        }
                        break;

                    case VSConstants.VSStd2KCmdID.CANCEL:
                        if (_adornment.IsWildcardOpen)
                        {
                            DismissWildcardPopup();
                            return VSConstants.S_OK;
                        }
                        if (_adornment.Popup.IsOpen)
                        {
                            DismissPopup();
                            return VSConstants.S_OK;
                        }
                        break;

                    case VSConstants.VSStd2KCmdID.UP:
                        if (_adornment.IsWildcardOpen)
                        {
                            _adornment.WildcardPopup.MoveSelection(-1);
                            return VSConstants.S_OK;
                        }
                        if (_adornment.Popup.IsOpen)
                        {
                            _adornment.Popup.MoveSelection(-1);
                            return VSConstants.S_OK;
                        }
                        break;

                    case VSConstants.VSStd2KCmdID.DOWN:
                        if (_adornment.IsWildcardOpen)
                        {
                            _adornment.WildcardPopup.MoveSelection(1);
                            return VSConstants.S_OK;
                        }
                        if (_adornment.Popup.IsOpen)
                        {
                            _adornment.Popup.MoveSelection(1);
                            return VSConstants.S_OK;
                        }
                        break;

                    case VSConstants.VSStd2KCmdID.BACKSPACE:
                    {
                        var result = NextTarget.Exec(ref pguidCmdGroup, nCmdId, nCmdexecopt, pvaIn, pvaOut);
                        SuppressNativeIntelliSense();
                        HandleBackspace();
                        UpdatePopupCtrlTransparency();
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

            // Handle VSStd97 command group — Ctrl+Space in SSMS 22 may arrive here
            if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97)
            {
                var cmdId97 = (VSConstants.VSStd97CmdID)nCmdId;
                if (cmdId97 == (VSConstants.VSStd97CmdID)898)
                {
                    SuppressNativeIntelliSense();
                    TriggerCompletion();
                    return VSConstants.S_OK; // Don't pass to next handler
                }
            }

            var finalResult = NextTarget?.Exec(ref pguidCmdGroup, nCmdId, nCmdexecopt, pvaIn, pvaOut)
                             ?? VSConstants.S_OK;

            // Nuclear option: after EVERY command, dismiss any native IntelliSense that appeared
            SuppressNativeIntelliSense();

            // Ctrl transparency: make popup semi-transparent while Ctrl is held
            UpdatePopupCtrlTransparency();

            return finalResult;
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
                    // Trigger IMMEDIATELY — no debounce. Show cached items instantly
                    // to beat SSMS native IntelliSense which also triggers immediately.
                    TriggerCompletion();
                }
            }
            else if (c == ' ' && _adornment.Popup.IsOpen)
            {
                // Space as insertion key (SQL Prompt style): commit if enabled in settings.
                // VS inserts the space BEFORE HandleTypedChar, so the caret is after the space.
                // We must compute the replacement span from before the space to cover the partial text.
                try
                {
                    var settings = AkmlSql.Core.Config.ConfigManager.Load();
                    if (settings.IntelliSense.SpaceCommits)
                    {
                        var item = _adornment.Popup.GetSelectedItem();
                        if (item != null)
                        {
                            CommitItemFromSpaceKey(item);
                            return; // Space is already inserted by VS before HandleTypedChar
                        }
                    }
                }
                catch { /* Settings load failure — fall through to default dismiss */ }

                DismissPopup();

                if (IsObjectExpectingKeywordBeforeCaret())
                {
                    _expectsObjects = true;
                    TriggerCompletion();
                }
            }
            else if (c == ' ' || c == '(' || c == ')' || c == ';' || c == ',')
            {
                DismissPopup();

                // After space, check if the preceding word is a keyword that expects
                // object names (table/view). If so, auto-trigger a fresh completion.
                if (c == ' ' && IsObjectExpectingKeywordBeforeCaret())
                {
                    _expectsObjects = true;
                    TriggerCompletion();
                }
                // SQL-Prompt-style smart GROUP BY: auto-trigger after "GROUP BY " (and
                // "ORDER BY ") so the engine's "▶ Add columns from SELECT" action and column
                // suggestions appear without a manual Ctrl+Space. Mirrors the web editor's
                // POST_KEYWORD_TRIGGER, which fires after a bare "by". We deliberately do NOT
                // set _expectsObjects — GROUP BY wants columns + the smart item, not tables.
                else if (c == ' ' && IsByKeywordBeforeCaret())
                {
                    TriggerCompletion();
                }
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

        /// <summary>Public entry point for Ctrl+Space from WPF PreviewKeyDown.</summary>
        public void TriggerManualCompletion() => TriggerCompletion();

        /// <summary>Dismiss native IntelliSense then show AKML popup (for Ctrl+Space).</summary>
        public void SuppressAndTrigger()
        {
            SuppressNativeIntelliSense();
            TriggerCompletion();
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

                // Start suppress timer to continuously dismiss native IntelliSense
                SuppressNativeIntelliSense();
                if (!_suppressTimer.IsEnabled)
                    _suppressTimer.Start();

                _fetchPending = true;

                // Fetch fresh results from Engine (background)
                // Use callback pattern instead of polling
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var client = Ipc.EngineLifecycle.Manager?.Client;
                        if (client == null || !client.IsConnected) return;

                        // Send document text and wait for Engine to process it.
                        // 150ms is needed because the Engine must parse the document
                        // and update the session before CursorContextAnalyzer can see
                        // the FROM/JOIN context for table completions.
                        await client.SendNotificationAsync(
                            AkmlSql.Core.Ipc.MessageTypes.DocumentChanged,
                            new AkmlSql.Core.Ipc.Messages.DocumentChange
                            {
                                SessionId = _sessionId,
                                ChangeType = 0,
                                FullText = docText
                            });

                        await System.Threading.Tasks.Task.Delay(150);

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
                            // Check configurable completion options
                            bool showSnippets = false;
                            bool showAlias = false;
                            try
                            {
                                var s = AkmlSql.Core.Config.ConfigManager.Load();
                                showSnippets = s.IntelliSense.SnippetsInCompletion;
                                showAlias = s.IntelliSense.AutoAlias;
                            }
                            catch { }

                            // Capture flag on background thread before switching to UI
                            var expectsObj = _expectsObjects;
                            _expectsObjects = false;

                            var modelList = new System.Collections.Generic.List<CompletionItemModel>(response.Items.Length);
                            bool hasObjects = false;
                            for (int i = 0; i < response.Items.Length; i++)
                            {
                                var item = response.Items[i];
                                // ObjectType 4 = Snippet — skip if disabled
                                if (item.ObjectType == 4 && !showSnippets)
                                    continue;
                                // ObjectType 10 = Alias suggestion — skip if disabled
                                if (item.ObjectType == 10 && !showAlias)
                                    continue;
                                // Track if we have any non-keyword items (tables, views, etc.)
                                if (item.ObjectType != 3 && item.ObjectType != 4) // not keyword, not snippet
                                    hasObjects = true;
                                modelList.Add(new CompletionItemModel
                                {
                                    DisplayText = item.DisplayText ?? string.Empty,
                                    InsertText = item.InsertText ?? item.DisplayText ?? string.Empty,
                                    SecondaryText = item.SecondaryText ?? string.Empty,
                                    ObjectType = item.ObjectType,
                                    SortPriority = item.SortPriority
                                });
                            }

                            // When auto-triggered after FROM/JOIN and we have schema objects,
                            // filter out keywords to prevent accidental keyword insertion via Tab
                            if (expectsObj && hasObjects)
                            {
                                modelList.RemoveAll(m => m.ObjectType == 3); // Remove keywords
                            }

                            var models = modelList.ToArray();

                            // Populate cache for instant show on next trigger (#10)
                            CompletionRpcHelper.UpdateCache(_sessionId, models);

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

                // Type-specific commit behavior
                switch (item.ObjectType)
                {
                    case 3: // Keyword — append trailing space for flow
                    {
                        var textAfter = caretPos < snapshot.Length ? snapshot[caretPos] : ' ';
                        var insertText = textAfter == ' ' ? item.InsertText : item.InsertText + " ";
                        _textView.TextBuffer.Replace(span, insertText);
                        DismissPopup();
                        // Auto-trigger for keywords that expect objects (FROM, JOIN, etc.)
                        if (IsObjectExpectingKeyword(item.InsertText))
                        {
                            TriggerCompletion();
                        }
                        return;
                    }

                    case 4: // Snippet — expand via engine
                    {
                        // Capture position before deleting abbreviation (fix #3: race condition)
                        int snippetInsertPos = start;
                        int snippetReplaceLen = span.Length;
                        DismissPopup();
                        TryExpandSnippetAtPosition(item.InsertText, snippetInsertPos, snippetReplaceLen);
                        return;
                    }

                    default:
                        _textView.TextBuffer.Replace(span, item.InsertText);
                        DismissPopup();

                        // Table/View commit → auto-trigger column completion after dot
                        if (item.ObjectType == 0 || item.ObjectType == 1) // Table or View
                        {
                            // If next char is a dot, trigger column completion
                            var newCaretPos = _textView.Caret.Position.BufferPosition.Position;
                            var newSnapshot = _textView.TextBuffer.CurrentSnapshot;
                            if (newCaretPos < newSnapshot.Length && newSnapshot[newCaretPos] == '.')
                            {
                                TriggerCompletion();
                            }
                        }
                        return;
                }
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

        /// <summary>
        /// Attempts to expand a snippet by its abbreviation (shortcode).
        /// Sends SnippetExpand IPC request to the engine. If the snippet exists,
        /// replaces the abbreviation text with the expanded body.
        /// Returns true if a snippet was found and expanded.
        /// </summary>
        private bool TryExpandSnippet(string abbreviation)
        {
            try
            {
                var client = Ipc.EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected) return false;

                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                var caretPos = _textView.Caret.Position.BufferPosition.Position;

                // Find word start for replacement span
                int start = caretPos;
                while (start > 0 && IsIdentifierChar(snapshot[start - 1]))
                    start--;

                var request = new AkmlSql.Core.Ipc.Messages.SnippetExpandRequest
                {
                    SessionId = _sessionId,
                    Shortcode = abbreviation,
                    CursorOffset = caretPos,
                    FormatOnExpand = true
                };

                // Fire-and-forget with callback on UI thread
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var response = await client.SendRequestAsync<
                            AkmlSql.Core.Ipc.Messages.SnippetExpandResponse,
                            AkmlSql.Core.Ipc.Messages.SnippetExpandRequest>(
                            AkmlSql.Core.Ipc.MessageTypes.SnippetExpand, request, timeoutMs: 3000);

                        if (response?.Success == true && !string.IsNullOrEmpty(response.ExpandedText))
                        {
                            _textView.VisualElement.Dispatcher.Invoke(() =>
                            {
                                try
                                {
                                    var currentSnapshot = _textView.TextBuffer.CurrentSnapshot;
                                    int replaceLen = caretPos - start;
                                    if (start + replaceLen <= currentSnapshot.Length)
                                    {
                                        _textView.TextBuffer.Replace(
                                            new Span(start, replaceLen),
                                            response.ExpandedText);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Debug(ex, "Snippet expansion: failed to insert text");
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Snippet expansion RPC failed for '{Abbreviation}'", abbreviation);
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Snippet expansion failed");
                return false;
            }
        }

        /// <summary>
        /// Expand a snippet at a known position (used when committing from popup).
        /// The position is captured at commit time to avoid race conditions (#3).
        /// </summary>
        private void TryExpandSnippetAtPosition(string abbreviation, int insertPos, int replaceLen)
        {
            try
            {
                var client = Ipc.EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected) return;

                var request = new AkmlSql.Core.Ipc.Messages.SnippetExpandRequest
                {
                    SessionId = _sessionId,
                    Shortcode = abbreviation,
                    CursorOffset = insertPos + replaceLen,
                    FormatOnExpand = true
                };

                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var response = await client.SendRequestAsync<
                            AkmlSql.Core.Ipc.Messages.SnippetExpandResponse,
                            AkmlSql.Core.Ipc.Messages.SnippetExpandRequest>(
                            AkmlSql.Core.Ipc.MessageTypes.SnippetExpand, request, timeoutMs: 3000);

                        if (response?.Success == true && !string.IsNullOrEmpty(response.ExpandedText))
                        {
                            _textView.VisualElement.Dispatcher.Invoke(() =>
                            {
                                try
                                {
                                    var currentSnapshot = _textView.TextBuffer.CurrentSnapshot;
                                    if (insertPos + replaceLen <= currentSnapshot.Length)
                                    {
                                        _textView.TextBuffer.Replace(
                                            new Span(insertPos, replaceLen),
                                            response.ExpandedText);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Debug(ex, "Snippet expansion: failed to insert at position {Pos}", insertPos);
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Snippet expansion RPC failed for '{Abbreviation}'", abbreviation);
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Snippet expansion at position failed");
            }
        }

        /// <summary>
        /// Known built-in snippet shortcodes. Tab only attempts expansion for these.
        /// Prevents Tab key from being swallowed for non-snippet words (#2).
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<string> KnownSnippetShortcodes =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ssf", "sel", "ins", "upd", "del", "cte"
            };

        private static bool IsKnownSnippetShortcode(string word)
        {
            return KnownSnippetShortcodes.Contains(word);
        }

        /// <summary>
        /// Space-key commit: the space was already inserted by VS, so caret is AFTER the space.
        /// We must scan backwards past the space to find the partial text to replace (#5).
        /// </summary>
        private void CommitItemFromSpaceKey(CompletionItemModel item)
        {
            try
            {
                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                var caretPos = _textView.Caret.Position.BufferPosition.Position;

                // Caret is after the space. Walk back past the space to find the partial text.
                int beforeSpace = caretPos - 1; // position of the space
                if (beforeSpace < 0 || snapshot[beforeSpace] != ' ')
                {
                    // Unexpected state — fall back to normal commit
                    CommitItem(item);
                    return;
                }

                // Find word start before the space
                int start = beforeSpace;
                while (start > 0 && IsIdentifierChar(snapshot[start - 1]))
                    start--;

                // Replace: partial text + space → insertText + space
                var span = new Span(start, beforeSpace - start); // exclude the space itself
                _textView.TextBuffer.Replace(span, item.InsertText);
                DismissPopup();

                // Auto-trigger for keywords that expect objects (FROM, JOIN, etc.)
                if (item.ObjectType == 3 && IsObjectExpectingKeyword(item.InsertText))
                {
                    _expectsObjects = true;
                    TriggerCompletion();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Space-commit failed");
            }
        }

        private void DismissPopup()
        {
            CancelQuickInfo();
            _adornment.PopupOpacity = 1.0;
            _adornment.Hide();
            _filterText = string.Empty;
            _fetchPending = false;
            // Clear stale cached items so the next trigger fetches fresh results
            // from the engine with the correct context (e.g., keywords after table name,
            // not table names after FROM).
            CompletionRpcHelper.ClearCache(_sessionId);
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

        /// <summary>
        /// Checks if the word immediately before the caret (before the just-typed space)
        /// is a SQL keyword that expects an object name (table, view, etc.).
        /// Used to auto-trigger table/view completions after "FROM ", "JOIN ", etc.
        /// </summary>
        private bool IsObjectExpectingKeywordBeforeCaret()
        {
            try
            {
                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                int pos = _textView.Caret.Position.BufferPosition.Position;

                // Caret is after the space. Move back past the space.
                int end = pos - 1;
                while (end > 0 && snapshot[end - 1] == ' ')
                    end--;

                // Now find the word before the space(s)
                int start = end;
                while (start > 0 && char.IsLetter(snapshot[start - 1]))
                    start--;

                if (start >= end)
                    return false;

                var word = snapshot.GetText(start, end - start);
                return IsObjectExpectingKeyword(word);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the word immediately before the caret (before the just-typed space) is a
        /// "BY". Used to auto-trigger column completions — and the engine's smart "Add columns
        /// from SELECT" GROUP BY action — the moment the user finishes typing "GROUP BY " /
        /// "ORDER BY ". The bare keyword "BY" also ends PARTITION BY (window functions); firing
        /// a completion there is harmless — the engine only emits the smart GROUP BY item when
        /// the cursor is in a real GROUP BY context, and column suggestions are welcome anyway.
        /// </summary>
        private bool IsByKeywordBeforeCaret()
        {
            try
            {
                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                int pos = _textView.Caret.Position.BufferPosition.Position;

                // Caret is after the space. Move back past the space(s).
                int end = pos - 1;
                while (end > 0 && snapshot[end - 1] == ' ')
                    end--;

                // Now find the word before the space(s).
                int start = end;
                while (start > 0 && char.IsLetter(snapshot[start - 1]))
                    start--;

                if (start >= end)
                    return false;

                var word = snapshot.GetText(start, end - start);
                return string.Equals(word, "BY", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsObjectExpectingKeyword(string word)
        {
            // Case-insensitive check for SQL keywords that expect object names
            if (string.IsNullOrEmpty(word))
                return false;

            switch (word.ToUpperInvariant())
            {
                case "FROM":
                case "JOIN":
                // Note: INNER, LEFT, RIGHT, CROSS, FULL are join qualifiers that expect
                // the JOIN keyword next, not table names. Only trigger after full "JOIN".
                case "INTO":
                case "UPDATE":
                case "TABLE":
                case "VIEW":
                case "EXEC":
                case "EXECUTE":
                case "TRUNCATE":
                case "DROP":
                case "ALTER":
                    return true;
                default:
                    return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SuppressNativeIntelliSense()
        {
            try
            {
                if (_broker == null)
                {
                    var componentModel = (Microsoft.VisualStudio.ComponentModelHost.IComponentModel)
                        Microsoft.VisualStudio.Shell.Package.GetGlobalService(
                            typeof(Microsoft.VisualStudio.ComponentModelHost.SComponentModel));
                    _broker = componentModel?.GetService<ICompletionBroker>();
                }

                if (_broker == null) return;

                // Dismiss ALL native sessions unconditionally
                if (_broker.IsCompletionActive(_textView))
                {
                    _broker.DismissAllSessions(_textView);
                }
            }
            catch { /* non-critical */ }
        }

        /// <summary>
        /// Checks if Ctrl is currently held and adjusts popup opacity.
        /// When Ctrl is down (and the popup is visible), set opacity to 30%
        /// so the user can see through the popup to the code underneath.
        /// </summary>
        private void UpdatePopupCtrlTransparency()
        {
            if (_adornment.IsPopupVisible)
            {
                bool ctrlDown = (System.Windows.Input.Keyboard.Modifiers
                                 & System.Windows.Input.ModifierKeys.Control) != 0;
                _adornment.PopupOpacity = ctrlDown ? 0.3 : 1.0;
            }
        }

        private static bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '#' || c == '@';
        }

        #region Object Definition (QuickInfo)

        /// <summary>
        /// Called when the selected item in the completion popup changes.
        /// Restarts the 300ms debounce timer before sending a QuickInfo request.
        /// </summary>
        private void OnCompletionSelectionChanged(object sender, CompletionItemModel item)
        {
            if (item == null || !_adornment.Popup.IsOpen)
            {
                CancelQuickInfo();
                return;
            }

            // Increment version to invalidate any in-flight requests
            var version = Interlocked.Increment(ref _quickInfoVersion);

            _quickInfoTimer?.Dispose();
            _quickInfoTimer = new Timer(_ =>
            {
                try
                {
                    // Check that our version is still current (no newer selection change)
                    if (Volatile.Read(ref _quickInfoVersion) != version) return;

                    FetchQuickInfo(item, version);
                }
                catch { /* Timer callback safety */ }
            }, null, QuickInfoDebounceMs, Timeout.Infinite);
        }

        /// <summary>
        /// Send a QuickInfo request to the engine for the currently selected completion item.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void FetchQuickInfo(CompletionItemModel item, int version)
        {
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var client = Ipc.EngineLifecycle.Manager?.Client;
                    if (client == null || !client.IsConnected) return;

                    // Cancelled if a newer selection arrived
                    if (Volatile.Read(ref _quickInfoVersion) != version) return;

                    var caretPos = 0;
                    try
                    {
                        _textView.VisualElement.Dispatcher.Invoke(() =>
                        {
                            caretPos = _textView.Caret.Position.BufferPosition.Position;
                        });
                    }
                    catch { return; }

                    var response = await client.SendRequestAsync<
                        AkmlSql.Core.Ipc.Messages.QuickInfoResponse,
                        AkmlSql.Core.Ipc.Messages.QuickInfoRequest>(
                        AkmlSql.Core.Ipc.MessageTypes.RequestQuickInfo,
                        new AkmlSql.Core.Ipc.Messages.QuickInfoRequest
                        {
                            SessionId = _sessionId,
                            CursorOffset = caretPos
                        },
                        timeoutMs: 3000);

                    // Cancelled if a newer selection arrived
                    if (Volatile.Read(ref _quickInfoVersion) != version) return;

                    _textView.VisualElement.Dispatcher.Invoke(() =>
                    {
                        // Final version check on the UI thread
                        if (Volatile.Read(ref _quickInfoVersion) != version) return;
                        if (!_adornment.Popup.IsOpen) return;

                        if (response != null && !string.IsNullOrEmpty(response.Header))
                        {
                            _adornment.DefinitionPanel.UpdateContent(
                                response.ObjectType,
                                response.Header,
                                response.Details,
                                response.Description);
                            _adornment.ShowDefinition();
                            _adornment.RepositionDefinition();
                        }
                        else
                        {
                            _adornment.HideDefinition();
                        }
                    });
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "QuickInfo RPC failed");
                    try
                    {
                        _textView.VisualElement.Dispatcher.Invoke(() => _adornment.HideDefinition());
                    }
                    catch { }
                }
            });
        }

        /// <summary>
        /// Cancel any pending QuickInfo requests and hide the definition panel.
        /// </summary>
        private void CancelQuickInfo()
        {
            Interlocked.Increment(ref _quickInfoVersion);
            _quickInfoTimer?.Dispose();
            _quickInfoTimer = null;
            try
            {
                _adornment.HideDefinition();
            }
            catch { }
        }

        #endregion

        #region Wildcard Expansion

        /// <summary>
        /// Detects if the cursor is at a SELECT wildcard (* or alias.*).
        /// Returns the star position and optional qualifier, or null if not a wildcard.
        /// </summary>
        private (int starPos, string qualifier)? DetectWildcardAtCursor()
        {
            try
            {
                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                var caretPos = _textView.Caret.Position.BufferPosition.Position;
                int length = snapshot.Length;

                // Find the * character at or adjacent to cursor
                int starPos = -1;
                if (caretPos > 0 && caretPos <= length && snapshot[caretPos - 1] == '*')
                {
                    starPos = caretPos - 1; // Cursor right after *
                }
                else if (caretPos < length && snapshot[caretPos] == '*')
                {
                    starPos = caretPos; // Cursor right before *
                }

                if (starPos < 0) return null;

                // Check for qualified wildcard: identifier.*
                string qualifier = null;
                if (starPos >= 2 && snapshot[starPos - 1] == '.')
                {
                    int idEnd = starPos - 2;
                    int idStart = idEnd;
                    while (idStart > 0 && IsIdentifierChar(snapshot[idStart - 1]))
                        idStart--;

                    if (idStart <= idEnd)
                    {
                        qualifier = snapshot.GetText(idStart, idEnd - idStart + 1);
                    }
                }

                // Verify SELECT context: scan backwards for SELECT keyword
                if (!IsInSelectContext(snapshot, starPos))
                    return null;

                return (starPos, qualifier);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Verify that the * at starPos is in a SELECT context (not arithmetic).
        /// </summary>
        private static bool IsInSelectContext(ITextSnapshot snapshot, int starPos)
        {
            int pos = starPos - 1;

            // Skip qualifier.* prefix if present
            if (pos >= 0 && snapshot[pos] == '.')
            {
                pos--;
                while (pos >= 0 && IsIdentifierChar(snapshot[pos]))
                    pos--;
            }

            // Skip whitespace and commas (handles "SELECT col1, *")
            while (pos >= 0 && (snapshot[pos] == ' ' || snapshot[pos] == '\t' ||
                                snapshot[pos] == '\r' || snapshot[pos] == '\n' ||
                                snapshot[pos] == ','))
                pos--;

            // Now extract the word at this position
            int wordEnd = pos;
            while (pos >= 0 && char.IsLetter(snapshot[pos]))
                pos--;
            pos++;

            if (pos > wordEnd) return false;
            var word = snapshot.GetText(pos, wordEnd - pos + 1).ToUpperInvariant();

            // Direct SELECT before the *
            if (word == "SELECT") return true;

            // DISTINCT or ALL after SELECT
            if (word == "DISTINCT" || word == "ALL")
            {
                return HasSelectBefore(snapshot, pos);
            }

            // TOP N — check for SELECT before TOP
            if (word == "TOP")
            {
                return HasSelectBefore(snapshot, pos);
            }

            // Could be after a comma in the select list (SELECT col1, *)
            return FindSelectBeforePosition(snapshot, pos);
        }

        private static bool HasSelectBefore(ITextSnapshot snapshot, int pos)
        {
            pos--;
            while (pos >= 0 && char.IsWhiteSpace(snapshot[pos]))
                pos--;

            // Skip a number (TOP 10)
            while (pos >= 0 && char.IsDigit(snapshot[pos]))
                pos--;
            while (pos >= 0 && char.IsWhiteSpace(snapshot[pos]))
                pos--;

            int wordEnd = pos;
            while (pos >= 0 && char.IsLetter(snapshot[pos]))
                pos--;
            pos++;

            if (pos > wordEnd) return false;
            var word = snapshot.GetText(pos, wordEnd - pos + 1).ToUpperInvariant();
            if (word == "SELECT") return true;
            if (word == "TOP") return HasSelectBefore(snapshot, pos);
            if (word == "DISTINCT" || word == "ALL") return HasSelectBefore(snapshot, pos);
            return false;
        }

        /// <summary>
        /// Walk backwards from pos to find SELECT, skipping identifiers and commas.
        /// Returns false if FROM/WHERE/JOIN is encountered first.
        /// </summary>
        private static bool FindSelectBeforePosition(ITextSnapshot snapshot, int pos)
        {
            int current = pos - 1;
            int maxScan = 2000;
            int scanned = 0;

            while (current >= 0 && scanned < maxScan)
            {
                scanned++;
                char c = snapshot[current];

                if (char.IsWhiteSpace(c) || c == ',' || c == '.' || c == '*' ||
                    c == '(' || c == ')' || c == '[' || c == ']' || c == '"' ||
                    char.IsDigit(c))
                {
                    current--;
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int wordEnd = current;
                    while (current >= 0 && (char.IsLetterOrDigit(snapshot[current]) || snapshot[current] == '_'))
                        current--;
                    current++;

                    var word = snapshot.GetText(current, wordEnd - current + 1).ToUpperInvariant();

                    if (word == "SELECT") return true;
                    if (word == "FROM" || word == "WHERE" || word == "JOIN" ||
                        word == "ON" || word == "SET" || word == "INTO" ||
                        word == "UPDATE" || word == "DELETE" || word == "INSERT")
                        return false;

                    current--;
                    continue;
                }

                current--;
            }

            return false;
        }

        /// <summary>
        /// Send WildcardExpansionRequest to the engine and show the checkbox popup.
        /// </summary>
        private void TriggerWildcardExpansion(int starPos, string qualifier)
        {
            // SQL Prompt parity: when the user has highlighted a runnable
            // sub-region of a malformed multi-statement script, treat the
            // selection as the document so the engine's parser only sees the
            // valid portion. The cursor offset is rebased to the selection
            // start. If there's no selection, fall back to the full buffer.
            string docText;
            int effectiveOffset = starPos;
            try
            {
                var selection = _textView.Selection;
                if (selection != null && !selection.IsEmpty &&
                    selection.SelectedSpans.Count > 0)
                {
                    var span = selection.SelectedSpans[0];
                    if (starPos >= span.Start.Position && starPos <= span.End.Position)
                    {
                        docText = span.GetText();
                        effectiveOffset = starPos - span.Start.Position;
                    }
                    else
                    {
                        docText = _textView.TextBuffer.CurrentSnapshot.GetText();
                    }
                }
                else
                {
                    docText = _textView.TextBuffer.CurrentSnapshot.GetText();
                }
            }
            catch
            {
                docText = _textView.TextBuffer.CurrentSnapshot.GetText();
                effectiveOffset = starPos;
            }

            // Store position so CommitWildcardExpansion doesn't need to re-detect.
            // Note: _wildcardStarPos is the ABSOLUTE document offset (used for
            // text-replacement on commit); effectiveOffset is what we send to the
            // engine for parsing.
            _wildcardStarPos = starPos;
            _wildcardQualifier = qualifier ?? string.Empty;
            _wildcardPending = true;

            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var client = Ipc.EngineLifecycle.Manager?.Client;
                    if (client == null || !client.IsConnected) return;

                    var response = await client.SendRequestAsync<
                        AkmlSql.Core.Ipc.Messages.WildcardExpansionResponse,
                        AkmlSql.Core.Ipc.Messages.WildcardExpansionRequest>(
                        AkmlSql.Core.Ipc.MessageTypes.WildcardExpansion,
                        new AkmlSql.Core.Ipc.Messages.WildcardExpansionRequest
                        {
                            SessionId = _sessionId,
                            CursorOffset = effectiveOffset,
                            DocumentText = docText,
                            Qualifier = qualifier
                        },
                        timeoutMs: 5000);

                    if (response?.Success == true && response.Tables != null && response.Tables.Length > 0)
                    {
                        _textView.VisualElement.Dispatcher.Invoke(() =>
                        {
                            if (!_wildcardPending) return;
                            _wildcardPending = false;

                            var groups = new List<WildcardExpansionPopup.TableGroupData>();
                            foreach (var t in response.Tables)
                            {
                                var cols = new WildcardExpansionPopup.ColumnData[t.Columns.Length];
                                for (int i = 0; i < t.Columns.Length; i++)
                                {
                                    cols[i] = new WildcardExpansionPopup.ColumnData
                                    {
                                        ColumnName = t.Columns[i].ColumnName,
                                        TypeDisplay = t.Columns[i].TypeDisplay
                                    };
                                }

                                groups.Add(new WildcardExpansionPopup.TableGroupData
                                {
                                    TableName = t.TableName,
                                    Qualifier = t.Qualifier,
                                    Columns = cols
                                });
                            }

                            _adornment.WildcardPopup.SetData(groups);
                            _adornment.ShowWildcard();
                            _adornment.RepositionWildcard();
                            SuppressNativeIntelliSense();
                        });
                    }
                    else
                    {
                        _textView.VisualElement.Dispatcher.Invoke(() => _wildcardPending = false);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Wildcard expansion RPC failed");
                    try
                    {
                        _textView.VisualElement.Dispatcher.Invoke(() => _wildcardPending = false);
                    }
                    catch { }
                }
            });
        }

        /// <summary>
        /// Replace * or alias.* with the checked columns, formatted multi-line.
        /// </summary>
        private void CommitWildcardExpansion()
        {
            try
            {
                var columns = _adornment.WildcardPopup.GetCheckedColumns();
                if (columns == null)
                {
                    // No columns checked — just dismiss
                    DismissWildcardPopup();
                    return;
                }

                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                var caretPos = _textView.Caret.Position.BufferPosition.Position;

                // Use stored position first, then fallback to re-detection from caret
                int starPos = -1;
                if (_wildcardStarPos >= 0 && _wildcardStarPos < snapshot.Length && snapshot[_wildcardStarPos] == '*')
                {
                    starPos = _wildcardStarPos;
                }
                else if (caretPos > 0 && caretPos <= snapshot.Length && snapshot[caretPos - 1] == '*')
                {
                    starPos = caretPos - 1;
                }
                else if (caretPos < snapshot.Length && snapshot[caretPos] == '*')
                {
                    starPos = caretPos;
                }

                _wildcardStarPos = -1; // Reset stored position

                if (starPos < 0)
                {
                    DismissWildcardPopup();
                    return;
                }

                // Determine replacement span start (includes qualifier.* if present)
                int spanStart = starPos;
                if (starPos >= 2 && snapshot[starPos - 1] == '.')
                {
                    int idEnd = starPos - 2;
                    int idStart = idEnd;
                    while (idStart > 0 && IsIdentifierChar(snapshot[idStart - 1]))
                        idStart--;
                    spanStart = idStart;
                }

                int spanLength = starPos - spanStart + 1; // +1 for the * itself

                // Calculate indentation: number of characters from line start to spanStart
                var line = snapshot.GetLineFromPosition(spanStart);
                int indentChars = spanStart - line.Start.Position;
                string indent = new string(' ', indentChars);

                // Determine if columns need qualifier prefix
                bool useQualifier = columns.Select(c => c.Qualifier).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1
                                    || (spanStart < starPos); // qualifier.* was explicit

                // Build expansion text
                var parts = new List<string>();
                foreach (var col in columns)
                {
                    string colText = useQualifier ? col.Qualifier + "." + col.ColumnName : col.ColumnName;
                    parts.Add(colText);
                }

                string expansion;
                if (parts.Count == 1)
                {
                    expansion = parts[0];
                }
                else
                {
                    // First column on same line, rest indented
                    var sb = new System.Text.StringBuilder();
                    sb.Append(parts[0]);
                    for (int i = 1; i < parts.Count; i++)
                    {
                        sb.Append(",\r\n");
                        sb.Append(indent);
                        sb.Append(parts[i]);
                    }
                    expansion = sb.ToString();
                }

                var span = new Span(spanStart, spanLength);
                _textView.TextBuffer.Replace(span, expansion);

                DismissWildcardPopup();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to commit wildcard expansion");
                DismissWildcardPopup();
            }
        }

        private void DismissWildcardPopup()
        {
            _adornment.HideWildcard();
            _wildcardPending = false;
        }

        #endregion
    }
}
