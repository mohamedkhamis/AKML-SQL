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

        // Spec 030 T033 / FR-013 — when true, the (shared) wildcard checkbox popup is acting as the
        // Column Picker: it was opened via Ctrl+Right (not a '*'), and committing INSERTS the checked
        // columns at the live caret rather than replacing a '*'.
        private bool _columnPickerMode;

        // Spec 030 T027 / FR-017 — the object-definition panel's Script tab is filled with the real
        // CREATE script (via GetObjectDefinition) for the currently-shown completion item. Cached per
        // session by full name so re-selecting the same object doesn't re-query the engine.
        private CompletionItemModel _currentDefinitionItem;
        private readonly System.Collections.Generic.Dictionary<string, string> _definitionCache =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public IOleCommandTarget NextTarget { get; set; }

        /// <summary>
        /// Spec 030 T026 / FR-010 — signature (parameter) help broker, set by
        /// <c>CompletionPopupProvider</c>. Used to start/refresh a signature session on '(' / ','.
        /// Null if the broker was unavailable (signature help silently inert).
        /// </summary>
        public Microsoft.VisualStudio.Language.Intellisense.ISignatureHelpBroker SignatureBroker { get; set; }

        private const int DebounceMs = 150;
        private const int QuickInfoDebounceMs = 300;

        public CompletionController(IWpfTextView textView, CompletionPopupAdornment adornment, string sessionId)
        {
            _textView = textView;
            _adornment = adornment;
            _sessionId = sessionId;

            // Subscribe to selection changes for QuickInfo debounce
            _adornment.Popup.SelectionChanged += OnCompletionSelectionChanged;

            // Auto-close type-over lifecycle: disarm pairs the caret leaves (same lifetime as the
            // view, matching the other ctor subscriptions).
            _textView.Caret.PositionChanged += OnCaretPositionChangedAutoClose;

            // Wildcard popup: double-click commits (same as Tab/Enter) — SQL Prompt parity.
            _adornment.WildcardPopup.CommitRequested += CommitWildcardExpansion;

            // Completion popup: double-click commits the clicked item (same as Tab/Enter).
            _adornment.Popup.ItemCommitRequested += OnPopupItemCommitRequested;

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
            // Focus-routing repair (SSMS 22 Quick Find): with the Find box focused, SSMS still
            // translates editing keys into EDITOR commands and routes them through the view
            // chain — printable characters reach the find TextBox, but Backspace/Delete arrived
            // here and the default handler applied them to the DOCUMENT (log-proven:
            // "BACKSPACE ... focused=TextBox"). Being first in the chain, redirect the editing
            // action to the actually-focused TextBox and never let it reach the document.
            // (VisualElement.IsKeyboardFocused, not HasAggregateFocus — the latter is true for
            // adornment focus too.)
            if (_textView?.VisualElement != null && !_textView.VisualElement.IsKeyboardFocused
                && pguidCmdGroup == VSConstants.VSStd2K)
            {
                var routedCmd = (VSConstants.VSStd2KCmdID)nCmdId;
                var focusedBox = System.Windows.Input.Keyboard.FocusedElement
                    as System.Windows.Controls.Primitives.TextBoxBase;

                if (focusedBox != null &&
                    (routedCmd == VSConstants.VSStd2KCmdID.BACKSPACE ||
                     routedCmd == VSConstants.VSStd2KCmdID.DELETE))
                {
                    Log.Debug(
                        "CompletionController: redirecting {Cmd} to the focused {Box} (Quick Find) instead of the document",
                        routedCmd, focusedBox.GetType().Name);
                    var editingCommand = routedCmd == VSConstants.VSStd2KCmdID.BACKSPACE
                        ? System.Windows.Documents.EditingCommands.Backspace
                        : System.Windows.Documents.EditingCommands.Delete;
                    editingCommand.Execute(null, focusedBox);
                    return VSConstants.S_OK;   // handled — the document must not see it
                }

                if (routedCmd == VSConstants.VSStd2KCmdID.TYPECHAR ||
                    routedCmd == VSConstants.VSStd2KCmdID.BACKSPACE ||
                    routedCmd == VSConstants.VSStd2KCmdID.DELETE ||
                    routedCmd == VSConstants.VSStd2KCmdID.RETURN ||
                    routedCmd == VSConstants.VSStd2KCmdID.TAB)
                {
                    // Editing command without a redirect target: swallowing is strictly safer
                    // than forwarding — forwarded commands edit the DOCUMENT the user is not
                    // typing in. TYPECHAR is the exception (never observed routing here; the
                    // find box receives printable keys directly) — pass it through unchanged.
                    if (routedCmd == VSConstants.VSStd2KCmdID.TYPECHAR)
                    {
                        Log.Debug(
                            "CompletionController: TYPECHAR without content focus (focused={Focused}) — passing through",
                            System.Windows.Input.Keyboard.FocusedElement?.GetType().Name ?? "null");
                        return NextTarget?.Exec(ref pguidCmdGroup, nCmdId, nCmdexecopt, pvaIn, pvaOut)
                               ?? VSConstants.S_OK;
                    }

                    Log.Debug(
                        "CompletionController: swallowing {Cmd} without content focus (focused={Focused}) — protecting the document",
                        routedCmd,
                        System.Windows.Input.Keyboard.FocusedElement?.GetType().Name ?? "null");
                    return VSConstants.S_OK;
                }
            }

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

                        // Auto-close type-over: typing the closer we just auto-inserted skips over
                        // it instead of doubling it (') → move past the existing )').
                        if (TryTypeOverAutoClosed(typedChar))
                        {
                            // Mirror HandleTypedChar's ')' arm — the keystroke is swallowed and the
                            // buffer never changes, so nothing else dismisses the popups.
                            if (typedChar == ')')
                            {
                                DismissPopup();
                                DismissSignatureHelp();
                            }
                            UpdatePopupCtrlTransparency();
                            return VSConstants.S_OK;
                        }

                        // Suppress native IntelliSense BEFORE letting VS handle the keystroke —
                        // but only while AKML completion is enabled; when disabled (FR-012) we hand
                        // off to the host's native IntelliSense instead of suppressing it.
                        bool akmlCompletionOn = CompletionEnabled();
                        if (akmlCompletionOn) SuppressNativeIntelliSense();

                        // Let VS insert the character
                        var result = NextTarget.Exec(ref pguidCmdGroup, nCmdId, nCmdexecopt, pvaIn, pvaOut);

                        // Suppress again after VS processes (it may trigger native IntelliSense)
                        if (akmlCompletionOn) SuppressNativeIntelliSense();

                        HandleTypedChar(typedChar);
                        HandleAutoClose(typedChar);
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
                            // Spec 030 T034 / FR-014 — Ctrl+Up jumps to the previous category; plain
                            // Up moves to the previous item. (Ctrl+Up usually arrives as SCROLLUP —
                            // handled below — but cover the modifier-on-arrow delivery path here too.)
                            if (CtrlHeld) _adornment.Popup.MoveCategory(-1);
                            else _adornment.Popup.MoveSelection(-1);
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
                            if (CtrlHeld) _adornment.Popup.MoveCategory(1);
                            else _adornment.Popup.MoveSelection(1);
                            return VSConstants.S_OK;
                        }
                        break;

                    // Spec 030 T034 / FR-014 — Ctrl+Up/Down arrive as the editor's scroll-line commands.
                    // While the suggestions box is open, repurpose them for category navigation; otherwise
                    // let them fall through to the host's normal scroll behaviour.
                    case VSConstants.VSStd2KCmdID.SCROLLUP:
                        if (_adornment.Popup.IsOpen)
                        {
                            _adornment.Popup.MoveCategory(-1);
                            return VSConstants.S_OK;
                        }
                        break;

                    case VSConstants.VSStd2KCmdID.SCROLLDN:
                        if (_adornment.Popup.IsOpen)
                        {
                            _adornment.Popup.MoveCategory(1);
                            return VSConstants.S_OK;
                        }
                        break;

                    // Spec 030 T033 / FR-013 — Ctrl+Left / Ctrl+Right toggle between the suggestions
                    // box and the column picker. Only intercepted while one of them is open; otherwise
                    // word-navigation passes through untouched.
                    case VSConstants.VSStd2KCmdID.WORDPREV:
                    case VSConstants.VSStd2KCmdID.WORDNEXT:
                        if (_columnPickerMode && _adornment.IsWildcardOpen)
                        {
                            // Picker → back to the suggestions box.
                            DismissWildcardPopup();
                            TriggerCompletion();
                            return VSConstants.S_OK;
                        }
                        if (_adornment.Popup.IsOpen)
                        {
                            // Suggestions box → column picker.
                            DismissPopup();
                            TriggerColumnPicker();
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
                        // FR-012: when IntelliSense is disabled, let the host's native completion
                        // handle the command (fall through) rather than swallowing it.
                        if (!CompletionEnabled())
                            break;
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
                if (cmdId97 == (VSConstants.VSStd97CmdID)898 && CompletionEnabled())
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
                // Dot: commit current selection (if any) + trigger new completion.
                // Spec 030 T078 / FR-042: gate the commit on the DotCommits setting (default true,
                // so behaviour is unchanged out of the box). When the user turns "Dot commits" off,
                // typing "." still re-triggers completion (e.g. the column list after "table.") but no
                // longer accepts the highlighted item — matching SQL Prompt. Cached accessor (2s TTL),
                // so no per-keystroke disk read.
                if (_adornment.Popup.IsOpen && IntelliSenseSettings().DotCommits)
                {
                    var item = _adornment.Popup.GetSelectedItem();
                    if (item != null)
                    {
                        // Don't commit dot itself — the dot is already inserted
                        CommitItemBeforeDot(item);
                    }
                }
                _filterText = string.Empty;
                AutoTriggerCompletion();
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
                    AutoTriggerCompletion();
                }
            }
            else if (c == ' ' && _adornment.Popup.IsOpen)
            {
                // Space as insertion key (SQL Prompt style): commit if enabled in settings.
                // VS inserts the space BEFORE HandleTypedChar, so the caret is after the space.
                // We must compute the replacement span from before the space to cover the partial text.

                // SQL-Prompt-style smart GROUP BY: capture the "GROUP BY "/"ORDER BY " context
                // BEFORE any commit mutates the buffer, so we re-open with the engine's
                // "▶ Add columns from SELECT" action whether or not SpaceCommits consumes this
                // space. The popup is typically OPEN here (the just-typed "BY" re-opened it), so
                // without this the trigger in the popup-CLOSED branch below would never run.
                bool byContext = IsByKeywordBeforeCaret();
                try
                {
                    var settings = AkmlSql.Core.Config.ConfigManager.Load();
                    if (settings.IntelliSense.SpaceCommits)
                    {
                        var item = _adornment.Popup.GetSelectedItem();
                        if (item != null)
                        {
                            CommitItemFromSpaceKey(item);
                            if (byContext) AutoTriggerCompletion();
                            return; // Space is already inserted by VS before HandleTypedChar
                        }
                    }
                }
                catch { /* Settings load failure — fall through to default dismiss */ }

                DismissPopup();

                if (IsObjectExpectingKeywordBeforeCaret())
                {
                    _expectsObjects = true;
                    AutoTriggerCompletion();
                }
                else if (byContext)
                {
                    AutoTriggerCompletion();
                }
            }
            else if (c == ' ' || c == '(' || c == ')' || c == ';' || c == ',')
            {
                DismissPopup();

                // Signature help (FR-010): '(' starts a call, ',' advances the active parameter,
                // ')' / ';' ends it. Re-trigger on '(' and ',' so the engine recomputes the active
                // parameter; dismiss on the closers.
                if (c == '(' || c == ',')
                    TriggerSignatureHelp();
                else if (c == ')' || c == ';')
                    DismissSignatureHelp();

                // After space, check if the preceding word is a keyword that expects
                // object names (table/view). If so, auto-trigger a fresh completion.
                if (c == ' ' && IsObjectExpectingKeywordBeforeCaret())
                {
                    _expectsObjects = true;
                    AutoTriggerCompletion();
                }
                // SQL-Prompt-style smart GROUP BY: auto-trigger after "GROUP BY " (and
                // "ORDER BY ") so the engine's "▶ Add columns from SELECT" action and column
                // suggestions appear without a manual Ctrl+Space. Mirrors the web editor's
                // POST_KEYWORD_TRIGGER, which fires after a bare "by". We deliberately do NOT
                // set _expectsObjects — GROUP BY wants columns + the smart item, not tables.
                else if (c == ' ' && IsByKeywordBeforeCaret())
                {
                    AutoTriggerCompletion();
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

        /// <summary>Public entry point for Ctrl+Space from WPF PreviewKeyDown. Respects FR-012.</summary>
        public void TriggerManualCompletion()
        {
            if (!CompletionEnabled()) return;
            TriggerCompletion();
        }

        /// <summary>Dismiss native IntelliSense then show AKML popup (for Ctrl+Space).</summary>
        public void SuppressAndTrigger()
        {
            if (!CompletionEnabled()) return;   // FR-012: IntelliSense disabled
            SuppressNativeIntelliSense();
            TriggerCompletion();
        }

        private void TriggerCompletion()
        {
            _filterText = GetWordAtCaret();
            FetchAndShowCompletions();
        }

        // Spec 030 R6 / T030-T031 / FR-012 — honor IntelliSense.Enabled (suppress the box entirely)
        // and AutoTrigger (typing triggers only when on; Ctrl+Space always works while Enabled).
        // Settings are cached with a short TTL so a fast typist doesn't hit the disk per keystroke,
        // while an Options change still applies within ~2s (no settings-changed event in the shell).
        private AkmlSql.Core.Config.AppSettings _settingsCache;
        private DateTime _settingsCacheUtc;

        private AkmlSql.Core.Config.AppSettings SettingsSnapshot()
        {
            if (_settingsCache == null || (DateTime.UtcNow - _settingsCacheUtc).TotalSeconds > 2)
            {
                try { _settingsCache = AkmlSql.Core.Config.ConfigManager.Load(); }
                catch (Exception ex)
                {
                    // Fall back to defaults (IntelliSense on), but log it — a corrupt/locked config
                    // would otherwise silently re-enable a popup the user disabled (FR-012).
                    _settingsCache ??= new AkmlSql.Core.Config.AppSettings();
                    Log.Warning(ex, "CompletionController: failed to load IntelliSense settings; using defaults");
                }
                _settingsCacheUtc = DateTime.UtcNow;
            }
            return _settingsCache;
        }

        private AkmlSql.Core.Config.IntelliSenseSettings IntelliSenseSettings() => SettingsSnapshot().IntelliSense;

        /// <summary>IntelliSense master switch (FR-012). False ⇒ no AKML completion at all.</summary>
        private bool CompletionEnabled() => IntelliSenseSettings().Enabled;

        // ─── Auto-close characters (Inserted Code › Special characters) ─────
        // Pairing decisions live in AutoClosePairs (unit-tested); this is the buffer glue.
        // Each armed entry tracks one auto-inserted closer: Open marks where the caret sat when the
        // pair was created (Negative tracking — stays put as content is typed at it), Close follows
        // the closer itself (Positive tracking). A STACK supports nested pairs ("['|']" must type
        // over ' then ]), and OnCaretPositionChangedAutoClose disarms entries the caret leaves so a
        // stale point can never swallow a genuinely intended closer typed there later.
        private readonly List<(ITrackingPoint Open, ITrackingPoint Close, char Closer)> _autoCloseStack = new();

        private void ArmTypeOver(ITextSnapshot snapshot, int position, char closer)
        {
            _autoCloseStack.Add((
                snapshot.CreateTrackingPoint(position, PointTrackingMode.Negative),
                snapshot.CreateTrackingPoint(position, PointTrackingMode.Positive),
                closer));
        }

        /// <summary>
        /// Disarms auto-close entries whose pair region the caret has left (clicked away, arrowed
        /// out, jumped elsewhere). Entries nest, so popping from the top until the caret is back
        /// inside a region keeps exactly the still-relevant pairs armed.
        /// </summary>
        private void OnCaretPositionChangedAutoClose(object sender, CaretPositionChangedEventArgs e)
        {
            try
            {
                if (_autoCloseStack.Count == 0) return;
                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                var caretPos = e.NewPosition.BufferPosition.Position;

                while (_autoCloseStack.Count > 0)
                {
                    var top = _autoCloseStack[_autoCloseStack.Count - 1];
                    var openPos = top.Open.GetPosition(snapshot);
                    var closePos = top.Close.GetPosition(snapshot);
                    if (caretPos < openPos || caretPos > closePos)
                        _autoCloseStack.RemoveAt(_autoCloseStack.Count - 1);
                    else
                        break;
                }
            }
            catch
            {
                _autoCloseStack.Clear();
            }
        }

        private void HandleAutoClose(char typedChar)
        {
            try
            {
                var i = IntelliSenseSettings();
                if (!i.Enabled) return;

                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                var caretPos = _textView.Caret.Position.BufferPosition.Position;

                // The caret must sit directly after the char VS just inserted; anything else
                // (overtype selections, undo replays) is left alone.
                if (caretPos < 1 || caretPos > snapshot.Length || snapshot[caretPos - 1] != typedChar)
                    return;

                var prev = caretPos >= 2 ? snapshot[caretPos - 2] : '\0';
                var prev2 = caretPos >= 3 ? snapshot[caretPos - 3] : '\0';
                var next = caretPos < snapshot.Length ? snapshot[caretPos] : '\0';

                var closer = AutoClosePairs.TryGetCloser(typedChar, prev, prev2, next, i.SpecialCharOptions);
                if (closer == null) return;

                _textView.TextBuffer.Insert(caretPos, closer);
                var newSnapshot = _textView.TextBuffer.CurrentSnapshot;
                _textView.Caret.MoveTo(new SnapshotPoint(newSnapshot, caretPos));

                // Multi-char closers ("*/") have no single type-over key — insert only, no arming
                // (and, unlike the old single-slot field, no clobbering of an outer armed pair).
                if (closer.Length == 1)
                    ArmTypeOver(newSnapshot, caretPos, closer[0]);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Auto-close failed");
                _autoCloseStack.Clear();
            }
        }

        /// <summary>
        /// True when <paramref name="typedChar"/> matches the innermost armed closer sitting
        /// directly at the caret — the caret is moved past it and the keystroke should be swallowed.
        /// </summary>
        private bool TryTypeOverAutoClosed(char typedChar)
        {
            try
            {
                if (_autoCloseStack.Count == 0) return false;
                var top = _autoCloseStack[_autoCloseStack.Count - 1];
                if (typedChar != top.Closer) return false;

                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                var caretPos = _textView.Caret.Position.BufferPosition.Position;

                if (caretPos != top.Close.GetPosition(snapshot)
                    || caretPos >= snapshot.Length
                    || snapshot[caretPos] != typedChar)
                {
                    // Not at the closer (the caret-move handler owns disarming) — insert normally.
                    return false;
                }

                _autoCloseStack.RemoveAt(_autoCloseStack.Count - 1);
                _textView.Caret.MoveTo(new SnapshotPoint(snapshot, caretPos + 1));
                return true;
            }
            catch
            {
                _autoCloseStack.Clear();
                return false;
            }
        }

        /// <summary>Auto-trigger (typing) gate (FR-012). Requires Enabled AND AutoTrigger.</summary>
        private bool AutoTriggerEnabled()
        {
            var i = IntelliSenseSettings();
            return i.Enabled && i.AutoTrigger;
        }

        /// <summary>Trigger completion from a typing event — no-op unless auto-trigger is on.</summary>
        private void AutoTriggerCompletion()
        {
            if (AutoTriggerEnabled())
                TriggerCompletion();
        }

        /// <summary>
        /// Spec 030 T026 / FR-010 — start (or refresh) a signature-help session. Dismisses any
        /// existing session first so the engine recomputes the active parameter as commas are typed.
        /// </summary>
        private void TriggerSignatureHelp()
        {
            var broker = SignatureBroker;
            if (broker == null || !CompletionEnabled()) return;   // FR-012: IntelliSense disabled
            try
            {
                broker.DismissAllSessions(_textView);
                broker.TriggerSignatureHelp(_textView);
            }
            catch (Exception ex) { Log.Debug(ex, "SignatureHelp: trigger failed"); }
        }

        private void DismissSignatureHelp()
        {
            try { SignatureBroker?.DismissAllSessions(_textView); }
            catch (Exception ex) { Log.Debug(ex, "SignatureHelp: dismiss failed"); }
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

        // Latched once per popup show: keeps the per-keystroke paths (Exec fall-through,
        // selection-changed) free of settings reads AND gives a freshly shown popup the correct
        // flags (its Ctrl poll previously ran on a stale default until the next TYPECHAR).
        // Options is modal, so the values cannot change while a popup is open.
        private bool _showDefinitionBoxLatched = true;

        private void LatchPopupSettings()
        {
            var snapshot = SettingsSnapshot();
            _adornment.Popup.CtrlTransparencyEnabled = snapshot.IntelliSense.CtrlTransparentPopups;
            _showDefinitionBoxLatched = snapshot.CompletionPolish.ShowObjectDefinitionBox;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void FetchAndShowCompletions()
        {
            try
            {
                LatchPopupSettings();

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
                                    SortPriority = item.SortPriority,
                                    SourceObject = item.SourceObject ?? string.Empty
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
        private void OnPopupItemCommitRequested(object sender, CompletionItemModel item)
        {
            // Mouse double-click on a popup row — same commit path as Tab/Enter.
            CommitItem(item);
        }

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
                        // Spec 030 T039 / FR-030 — pass the SHORTCODE (DisplayText) to the engine, not the
                        // body (InsertText). SnippetProvider sets DisplayText = shortcode, InsertText = body;
                        // the engine resolves snippets by shortcode, so sending the body never matched.
                        TryExpandSnippetAtPosition(item.DisplayText, snippetInsertPos, snippetReplaceLen);
                        return;
                    }

                    default:
                    {
                        var insertText = ApplyFunctionParens(item, snapshot, caretPos, start, item.InsertText,
                            out int caretBetweenParens);

                        _textView.TextBuffer.Replace(span, insertText);
                        MoveCaretIntoParens(caretBetweenParens);
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
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to commit completion");
                DismissPopup();
            }
        }

        /// <summary>
        /// Special characters › "Add parentheses ( ) when inserting a function or data type":
        /// appends <c>()</c> to a committed Function item unless the insert text already carries
        /// parens or one follows the caret. Shared by the Enter/Tab/double-click and space-key
        /// commit paths so the same item commits identically regardless of gesture.
        /// <paramref name="caretBetweenParens"/> is the caret position inside the pair, or -1.
        /// </summary>
        private string ApplyFunctionParens(CompletionItemModel item, ITextSnapshot snapshot,
            int caretPos, int start, string insertText, out int caretBetweenParens)
        {
            caretBetweenParens = -1;
            if (item.ObjectType != 5) return insertText; // Function
            if (!IntelliSenseSettings().SpecialCharOptions.AddParentheses) return insertText;
            if (insertText.EndsWith("(") || insertText.EndsWith(")")) return insertText;

            var nextCh = caretPos < snapshot.Length ? snapshot[caretPos] : '\0';
            if (nextCh == '(') return insertText;

            caretBetweenParens = start + insertText.Length + 1;
            return insertText + "()";
        }

        /// <summary>
        /// Moves the caret between the parens appended by <see cref="ApplyFunctionParens"/> and
        /// arms type-over for the closing paren, so the user's natural closing ')' keystroke skips
        /// it instead of doubling it (consistent with HandleAutoClose-inserted closers).
        /// </summary>
        private void MoveCaretIntoParens(int caretBetweenParens)
        {
            if (caretBetweenParens < 0) return;
            var snapshot = _textView.TextBuffer.CurrentSnapshot;
            if (caretBetweenParens > snapshot.Length) return;
            _textView.Caret.MoveTo(new SnapshotPoint(snapshot, caretBetweenParens));
            ArmTypeOver(snapshot, caretBetweenParens, ')');
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

                // T039 — a snippet committed before a dot must expand via the engine, not insert its raw
                // body (rare interaction, but keep all commit paths consistent).
                if (item.ObjectType == 4)
                {
                    _adornment.Hide();
                    TryExpandSnippetAtPosition(item.DisplayText, start, dotPos - start);
                    return;
                }

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
                                InsertSnippetExpansion(start, caretPos - start, response.ExpandedText, response.CursorOffset));
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
                                InsertSnippetExpansion(insertPos, replaceLen, response.ExpandedText, response.CursorOffset));
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
        /// Spec 030 T039 / FR-035 — insert an expanded snippet body at <paramref name="insertPos"/>
        /// (replacing <paramref name="replaceLen"/> chars), normalizing the engine's LF-joined text to
        /// the document's newline so a CRLF buffer doesn't end up with mixed line endings, then placing
        /// the caret at the <c>$CURSOR$</c> position (<paramref name="cursorOffset"/>, -1 ⇒ end), adjusted
        /// for the newline expansion. Must run on the UI thread.
        /// </summary>
        private void InsertSnippetExpansion(int insertPos, int replaceLen, string expandedText, int cursorOffset)
        {
            try
            {
                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                if (insertPos < 0 || replaceLen < 0 || insertPos + replaceLen > snapshot.Length) return;

                string text = expandedText ?? string.Empty;
                int caret = cursorOffset >= 0 ? cursorOffset : text.Length;

                string nl = GetBufferNewLine();
                if (nl != "\n" && text.IndexOf('\n') >= 0)
                {
                    // Count LF before the caret to keep the $CURSOR$ offset correct after LF → CRLF growth.
                    int lfBefore = 0, upto = Math.Min(caret, text.Length);
                    for (int i = 0; i < upto; i++) if (text[i] == '\n') lfBefore++;
                    text = text.Replace("\r\n", "\n").Replace("\n", nl);
                    caret += lfBefore * (nl.Length - 1);
                }

                _textView.TextBuffer.Replace(new Span(insertPos, replaceLen), text);

                var after = _textView.TextBuffer.CurrentSnapshot;
                int caretPos = insertPos + caret;
                if (caretPos >= 0 && caretPos <= after.Length)
                    _textView.Caret.MoveTo(new SnapshotPoint(after, caretPos));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Snippet expansion: insert failed at {Pos}", insertPos);
            }
        }

        /// <summary>Returns the document's line-break text (first non-empty line break, default CRLF).</summary>
        private string GetBufferNewLine()
        {
            try
            {
                var snap = _textView.TextBuffer.CurrentSnapshot;
                for (int i = 0; i < snap.LineCount; i++)
                {
                    var lb = snap.GetLineFromLineNumber(i).GetLineBreakText();
                    if (!string.IsNullOrEmpty(lb)) return lb;
                }
            }
            catch { }
            return "\r\n";
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

                // T039 / FR-030 — snippet items expand via the engine by SHORTCODE (DisplayText); never
                // insert the raw body (InsertText). Mirror CommitItem case 4 for the space-commit path.
                // Replace the shortcode AND the just-typed space so no stray space survives the expansion.
                if (item.ObjectType == 4)
                {
                    int snippetReplaceLen = caretPos - start;
                    DismissPopup();
                    TryExpandSnippetAtPosition(item.DisplayText, start, snippetReplaceLen);
                    return;
                }

                // Replace: partial text + space → insertText + space. Functions get the same
                // add-parens treatment as the Enter/Tab commit path (PR #248 review finding #7).
                var span = new Span(start, beforeSpace - start); // exclude the space itself
                var insertText = ApplyFunctionParens(item, snapshot, beforeSpace, start, item.InsertText,
                    out int caretBetweenParens);
                _textView.TextBuffer.Replace(span, insertText);
                MoveCaretIntoParens(caretBetweenParens);
                DismissPopup();

                // Auto-trigger for keywords that expect objects (FROM, JOIN, etc.)
                if (item.ObjectType == 3 && IsObjectExpectingKeyword(item.InsertText))
                {
                    _expectsObjects = true;
                    AutoTriggerCompletion();
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
            // T027 — the object-definition cache lives only for one popup session: clearing on close
            // keeps the within-session re-selection benefit while picking up mid-session DDL changes
            // on the next open (and bounds its growth).
            _definitionCache.Clear();
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

        /// <summary>
        /// Keywords after which a space should open completion in OBJECT mode. Internal so the
        /// list is directly testable — it is easy to extend the engine's clause analysis and
        /// forget this shell-side gate, which is exactly how FROM-less DELETE regressed.
        /// </summary>
        internal static bool IsObjectExpectingKeyword(string word)
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
                // FROM / INTO are optional: `DELETE t` and `INSERT t VALUES (…)` are valid T-SQL
                // and put a table name directly after the keyword. Without these the shell only
                // reached object mode via FROM/INTO, so those forms offered nothing. The engine
                // already serves both positions (ClauseType.Delete; DetectInsertClauseType keeps
                // bare INSERT on the keyword position *with* the insertable-object list).
                //
                // MERGE deliberately stays out: CursorContextAnalyzer knows MERGE only as a
                // statement boundary, never as a clause, so `MERGE |` falls through to the generic
                // statement-start keyword list. Forcing object mode there pops ALTER/BACKUP/BEGIN…
                // where a table name belongs. Add MERGE here only once the engine has a MERGE
                // clause type — see ObjectExpectingKeywordTests for the pinned expectation.
                case "DELETE":
                case "INSERT":
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
                // Suggestions › Behavior › "Make popups transparent when Ctrl is held" — the flag
                // is latched onto the popup at show time (LatchPopupSettings); no settings read here.
                if (!_adornment.Popup.CtrlTransparencyEnabled)
                {
                    _adornment.PopupOpacity = 1.0;
                    return;
                }

                bool ctrlDown = (System.Windows.Input.Keyboard.Modifiers
                                 & System.Windows.Input.ModifierKeys.Control) != 0;
                _adornment.PopupOpacity = ctrlDown ? 0.3 : 1.0;
            }
        }

        private static bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '#' || c == '@';
        }

        /// <summary>True when Ctrl is currently held — used to distinguish Ctrl+Up/Down (category nav) from plain arrows.</summary>
        private static bool CtrlHeld =>
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0;

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

            // Suggestions › Tooltips › "Show the object definition box" — when off, never fetch
            // QuickInfo or show the definition panel. Latched at popup show; no settings read on
            // this per-arrow-key path.
            if (!_showDefinitionBoxLatched)
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

                            // T027 — fill the Script tab with the object's real CREATE definition.
                            LoadScriptTab(item);
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
            _currentDefinitionItem = null;   // T027 — drop any in-flight Script-tab fetch result
            try
            {
                _adornment.HideDefinition();
            }
            catch { }
        }

        /// <summary>
        /// Spec 030 T027 / FR-017 — eagerly populate the definition panel's Script tab with the
        /// object's CREATE script. Gated to definition-bearing item types; identity comes from the
        /// engine's authoritative <see cref="CompletionItemModel.SourceObject"/> (clean even for
        /// decorated FK-join items). Cached per full name; a response is applied only if the selection
        /// has not moved on (reference guard). Runs on the UI thread (called from the QuickInfo block).
        /// </summary>
        private void LoadScriptTab(CompletionItemModel item)
        {
            _currentDefinitionItem = item;
            if (item == null) return;

            // Only Table / View / Function / Procedure have a CREATE definition.
            if (item.ObjectType != 0 && item.ObjectType != 1 && item.ObjectType != 5 && item.ObjectType != 6)
            {
                _adornment.DefinitionPanel.SetScript(null, "No definition for this item type");
                return;
            }

            if (!TryParseObjectIdentity(item.SourceObject, out var objectName, out var schemaName))
            {
                _adornment.DefinitionPanel.SetScript(null, "No definition available");
                return;
            }

            var cacheKey = (schemaName != null ? schemaName + "." : string.Empty) + objectName;
            if (_definitionCache.TryGetValue(cacheKey, out var cachedDef))
            {
                _adornment.DefinitionPanel.SetScript(cachedDef, null);
                return;
            }

            var target = item;
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var client = Ipc.EngineLifecycle.Manager?.Client;
                    if (client == null || !client.IsConnected)
                    {
                        ResetScriptTabIfCurrent(target, "Definition unavailable");
                        return;
                    }

                    var response = await client.SendRequestAsync<
                        AkmlSql.Core.Ipc.Messages.GetObjectDefinitionResponse,
                        AkmlSql.Core.Ipc.Messages.GetObjectDefinitionRequest>(
                        AkmlSql.Core.Ipc.MessageTypes.GetObjectDefinition,
                        new AkmlSql.Core.Ipc.Messages.GetObjectDefinitionRequest
                        {
                            SessionId = _sessionId,
                            ObjectName = objectName,
                            SchemaName = schemaName,
                            PeekOnly = true
                        },
                        timeoutMs: 5000);

                    _textView.VisualElement.Dispatcher.Invoke(() =>
                    {
                        // Selection moved on, or the panel was dismissed — drop a stale result.
                        if (!ReferenceEquals(_currentDefinitionItem, target)) return;
                        if (!_adornment.DefinitionPanel.HasContent) return;

                        if (response != null && response.Success && !string.IsNullOrWhiteSpace(response.Definition))
                        {
                            _definitionCache[cacheKey] = response.Definition;
                            _adornment.DefinitionPanel.SetScript(response.Definition, null);
                        }
                        else
                        {
                            _adornment.DefinitionPanel.SetScript(null, response?.Error ?? "Definition not found");
                        }
                    });
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Object-definition (Script tab) fetch failed");
                    // Timeout / disconnect / engine error all throw here — clear the placeholder so the
                    // Script tab doesn't sit on "-- Loading definition…" forever (symmetric with success).
                    ResetScriptTabIfCurrent(target, "Definition unavailable");
                }
            });
        }

        /// <summary>
        /// Replaces the Script tab's loading placeholder with an unavailable message, but only if the
        /// target is still the selected item and the panel is still showing. Marshals to the UI thread;
        /// tolerant of a tearing-down view.
        /// </summary>
        private void ResetScriptTabIfCurrent(CompletionItemModel target, string reason)
        {
            try
            {
                _textView.VisualElement.Dispatcher.Invoke(() =>
                {
                    if (!ReferenceEquals(_currentDefinitionItem, target)) return;
                    if (!_adornment.DefinitionPanel.HasContent) return;
                    _adornment.DefinitionPanel.SetScript(null, reason);
                });
            }
            catch { /* view tearing down */ }
        }

        /// <summary>
        /// Parses the engine's <c>schema.object</c> SourceObject into (objectName, schemaName?).
        /// Rejects empty / whitespace-bearing names (defensive — SourceObject is normally clean, but
        /// this guards against any decorated value slipping through).
        /// </summary>
        private static bool TryParseObjectIdentity(string sourceObject, out string objectName, out string schemaName)
        {
            objectName = string.Empty;
            schemaName = null;
            if (string.IsNullOrWhiteSpace(sourceObject)) return false;

            var raw = sourceObject.Replace("[", string.Empty).Replace("]", string.Empty).Trim();
            if (raw.Length == 0) return false;

            var parts = raw.Split('.');
            var name = parts[parts.Length - 1].Trim();
            if (name.Length == 0 || HasWhitespace(name)) return false;
            objectName = name;

            if (parts.Length >= 2)
            {
                var sch = parts[parts.Length - 2].Trim();
                if (sch.Length > 0 && !HasWhitespace(sch)) schemaName = sch;
            }
            return true;
        }

        private static bool HasWhitespace(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (char.IsWhiteSpace(s[i])) return true;
            return false;
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
                    if (client == null || !client.IsConnected)
                    {
                        _textView.VisualElement.Dispatcher.Invoke(() => _wildcardPending = false);
                        return;
                    }

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
        /// Spec 030 T033 / FR-013 — open the Column Picker: fetch the in-scope columns at the
        /// caret (reusing the WildcardExpansion engine path with an empty qualifier — it resolves
        /// the FROM-clause tables and returns their columns grouped, no '*' required) and show the
        /// shared checkbox popup in picker mode.
        /// </summary>
        private void TriggerColumnPicker()
        {
            int caretPos;
            string docText;
            try
            {
                caretPos = _textView.Caret.Position.BufferPosition.Position;
                docText = _textView.TextBuffer.CurrentSnapshot.GetText();
            }
            catch { return; }
            if (string.IsNullOrEmpty(docText)) return;

            _columnPickerMode = true;
            _wildcardPending = true;

            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var client = Ipc.EngineLifecycle.Manager?.Client;
                    if (client == null || !client.IsConnected)
                    {
                        // Engine down — clear the picker flags so they don't strand and misroute a
                        // later real wildcard ('*') commit into CommitColumnPicker.
                        _textView.VisualElement.Dispatcher.Invoke(() => { _wildcardPending = false; _columnPickerMode = false; });
                        return;
                    }

                    var response = await client.SendRequestAsync<
                        AkmlSql.Core.Ipc.Messages.WildcardExpansionResponse,
                        AkmlSql.Core.Ipc.Messages.WildcardExpansionRequest>(
                        AkmlSql.Core.Ipc.MessageTypes.WildcardExpansion,
                        new AkmlSql.Core.Ipc.Messages.WildcardExpansionRequest
                        {
                            SessionId = _sessionId,
                            CursorOffset = caretPos,
                            DocumentText = docText,
                            Qualifier = string.Empty   // all in-scope tables
                        },
                        timeoutMs: 5000);

                    if (response?.Success == true && response.Tables != null && response.Tables.Length > 0)
                    {
                        _textView.VisualElement.Dispatcher.Invoke(() =>
                        {
                            if (!_wildcardPending || !_columnPickerMode) return;
                            _wildcardPending = false;
                            // Typing during the async fetch may have reopened the suggestions box —
                            // dismiss it so only the picker is visible (no two popups at once).
                            if (_adornment.Popup.IsOpen) DismissPopup();

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
                        _textView.VisualElement.Dispatcher.Invoke(() => { _wildcardPending = false; _columnPickerMode = false; });
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Column picker RPC failed");
                    try { _textView.VisualElement.Dispatcher.Invoke(() => { _wildcardPending = false; _columnPickerMode = false; }); }
                    catch { }
                }
            });
        }

        /// <summary>
        /// FR-013 — insert the picker's checked columns as a comma list at the caret (no '*' to
        /// replace). Qualifies columns when more than one table is in scope.
        /// </summary>
        private void CommitColumnPicker()
        {
            try
            {
                var columns = _adornment.WildcardPopup.GetCheckedColumns();
                if (columns == null || columns.Count == 0)
                {
                    DismissWildcardPopup();
                    return;
                }

                // Insert at the LIVE caret. The picker popup is non-focusable, so the caret stays in
                // the editor; using it (not a stale offset captured before the async fetch) lands the
                // columns where the user is now, even if they typed while the fetch was in flight.
                int insertPos = _textView.Caret.Position.BufferPosition.Position;

                bool useQualifier = columns.Select(c => c.Qualifier)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;

                var parts = columns
                    .Select(c => useQualifier ? c.Qualifier + "." + c.ColumnName : c.ColumnName)
                    .ToList();
                var text = string.Join(", ", parts);

                _textView.TextBuffer.Insert(insertPos, text);
                DismissWildcardPopup();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to commit column picker");
                DismissWildcardPopup();
            }
        }

        /// <summary>
        /// Replace * or alias.* with the checked columns, formatted multi-line.
        /// </summary>
        private void CommitWildcardExpansion()
        {
            // FR-013: the shared popup is acting as the Column Picker — insert at the caret instead.
            if (_columnPickerMode)
            {
                CommitColumnPicker();
                return;
            }
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
            _columnPickerMode = false;
        }

        #endregion
    }
}
