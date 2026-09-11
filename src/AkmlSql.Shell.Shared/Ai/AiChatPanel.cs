#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Editor;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Refactoring;
using AkmlSql.Shell.Shared.Ui.Theme;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Task = System.Threading.Tasks.Task;

namespace AkmlSql.Shell.Shared.Ai
{
    /// <summary>
    /// WPF UserControl for the AI Chat panel.
    /// Built entirely in code (no XAML) since this is a shared project (.projitems).
    /// Layout: header with database context, scrollable conversation area, input bar with Send button.
    /// </summary>
    internal sealed class AiChatPanel : ThemeAwareUserControl
    {
        /// <summary>
        /// Spec 036 (US1, FR-028): shown when a message cannot be sent because no editor session
        /// is bound. The chat follows the active SQL editor's connection, so the fix is to connect
        /// one — never send under a fabricated id (R1).
        /// </summary>
        internal const string NoConnectionMessage =
            "No database connection — this chat answers questions about the database the active " +
            "SQL editor is connected to. Open a query window and connect it to a database, then ask again.";

        /// <summary>Header text when no editor connection is bound (FR-027).</summary>
        internal const string NotConnectedHeaderText = "Not connected";

        private const int BindingRefreshIntervalMs = 2000;
        private const int PrivacyModeRefreshSeconds = 30;

        /// <summary>Spec 037 (research R9): the AI-config read behind RefreshConfiguration is
        /// cached this long — the same idiom AiCommandVisibility uses (CacheDurationMs = 5000),
        /// so the 2-second binding tick never becomes per-tick disk I/O in the host. The
        /// per-agent can-answer verdicts computed from that read are cached with it (see
        /// <see cref="_canAnswerCache"/>): the disk read was never the only per-tick cost, the
        /// DPAPI unwrap behind <see cref="AiChatEmptyState.CanAnswer"/> was the other one.</summary>
        private const int ConfigCacheSeconds = 5;

        private readonly StackPanel _conversationPanel;
        private readonly ScrollViewer _scrollViewer;
        private readonly TextBox _inputBox;
        private readonly TextBlock _inputPlaceholder;
        private readonly Button _sendButton;
        private readonly TextBlock _headerLabel;
        private readonly AiAgentPicker _agentPicker;
        private readonly TextBlock _thinkingIndicator;
        private readonly Border _statusStrip;
        private readonly TextBlock _schemaStatusLabel;
        private readonly TextBlock _privacyNoteLabel;
        private readonly DispatcherTimer _bindingTimer;
        private readonly List<ChatTurnDto> _history = new();

        /// <summary>
        /// Spec 037 (US3, FR-043): per-turn agent names parallel to <see cref="_history"/> —
        /// null for user turns and for answers from an older engine (no <c>AgentName</c>).
        /// <c>ChatTurnDto</c> has no room for a name, so attribution rides alongside; the copy
        /// path tolerates a shorter list (seeded history) by treating the missing name as null.
        /// </summary>
        private readonly List<string?> _historyAgentNames = new();
        private string _currentDatabase = string.Empty;
        private string? _boundSessionId;
        private bool _schemaReady;
        private string _lastPrivacyMode = string.Empty;
        private DateTime _privacyModeReadAtUtc = DateTime.MinValue;
        private bool _isSending;

        // Spec 037 (US1): the empty-state/greeting render and its cheap change detection.
        private Border? _greetingBubble;
        private AiChatEmptyState? _emptyStateCard;
        private bool _agentUsable;
        private string _offendingAgentId = string.Empty;
        private string _configSignature = string.Empty;
        private AppSettings? _cachedSettings;
        private DateTime _configReadAtUtc = DateTime.MinValue;

        /// <summary>
        /// Spec 037 (US1, research R9): the can-answer verdict per agent of
        /// <see cref="_cachedSettings"/>, keyed by agent instance (reference equality —
        /// <c>AiAgent</c> is a plain class). <see cref="AiChatEmptyState.CanAnswer"/> ends in a
        /// synchronous DPAPI unwrap, and the signature is recomputed on every 2-second tick, so
        /// with the 20-agent maximum an uncached probe means 20 crypto round-trips on the UI
        /// thread, 30 times a minute. Cleared in exactly the one place <see cref="_cachedSettings"/>
        /// is re-read, so an edited key, an added agent or a removed one is still picked up on
        /// the next read — immediately on the forceRefresh path.
        /// </summary>
        private readonly Dictionary<AiAgent, bool> _canAnswerCache = new();

        /// <summary>Spec 037 (US3): the resolved chat agent's name (S3), kept by
        /// <see cref="RenderAgentState"/> so the send path can attribute each answer — and so a
        /// mid-flight picker switch cannot rewrite who an in-flight answer was expected from.</summary>
        private string? _resolvedChatAgentName;

        /// <summary>Spec 037 (US5, FR-057): the resolved chat agent's id, kept alongside
        /// <see cref="_resolvedChatAgentName"/> so a configuration-caused live failure can
        /// deep-link Options to the agent that produced it.</summary>
        private string? _resolvedChatAgentId;

        /// <summary>
        /// Spec 037 (US6, FR-049): the chat assignment seen by the last configuration refresh,
        /// tracked so a refresh that finds it cleared — normalisation dropped the dangling id on
        /// load (V16), or another host removed or disabled the assigned agent — can say so once,
        /// in the conversation, instead of silently swapping who answers. The picker's own writes
        /// seed it (a user-initiated change is not a cleared assignment), and the transition
        /// itself is the dedupe: the panel adds no second, shell-side counter to the
        /// once-per-feature-per-engine-process one (V22).
        /// </summary>
        private string _lastChatAssignment = string.Empty;

        /// <summary>
        /// Test seam: when set, the panel reads AI configuration from this delegate instead of
        /// <see cref="ConfigManager.Load()"/>, so panel tests never depend on the machine's real
        /// config.json. Production code never sets it.
        /// </summary>
        internal static Func<AppSettings>? TestSettingsProvider { get; set; }

        /// <summary>
        /// The panel's one route into Options (FR-017, FR-041): defaults to
        /// <see cref="Commands.OptionsCommand.ShowOptions"/>. Tests substitute a recording fake
        /// because the real route opens a modal dialog; production code never re-sets it.
        /// </summary>
        internal static Func<string?, string?, bool> ShowOptionsRoute { get; set; }
            = Commands.OptionsCommand.ShowOptions;

        /// <summary>Accessible name of the per-answer attribution caption (FR-042).</summary>
        internal const string AttributionAutomationName = "Answer attribution";

        /// <summary>Accessible name of the cleared-assignment notice line (FR-049).</summary>
        internal const string ClearedAssignmentNoticeAutomationName = "Chat agent notice";

        private static AppSettings LoadSettings()
            => TestSettingsProvider?.Invoke() ?? ConfigManager.Load();

        public AiChatPanel()
        {
            // Root layout
            var rootPanel = new DockPanel { LastChildFill = true };

            // ──── Top: Header bar showing AI Chat title and current database ────
            var headerBar = new Border
            {
                Padding = new Thickness(Spacing.Sm, 6, Spacing.Sm, 6)
            };
            headerBar.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceElevated);

            var headerGrid = new Grid();
            // [title | * db name (ellipsis) | agent picker | copy] — the star column takes the
            // squeeze so a long server.database never pushes the agent dropdown off the edge
            // (user feedback: "Gemini" ended up clipped at the panel's right edge).
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleLabel = new TextBlock
            {
                Text = "AI Chat",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, Spacing.Sm, 0)
            };
            titleLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            Grid.SetColumn(titleLabel, 0);
            headerGrid.Children.Add(titleLabel);

            _headerLabel = new TextBlock
            {
                Text = string.Empty,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, Spacing.Sm, 0),
                // Ellipsize; SetDatabaseContext puts the full name on the tooltip.
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _headerLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            Grid.SetColumn(_headerLabel, 1);
            headerGrid.Children.Add(_headerLabel);

            // Spec 037 (US3, FR-037/FR-038/FR-044): the agent picker lives in the header — the
            // panel's "who and what" strip — beside the database label and the ⧉ button, so the
            // answering agent is visible and changeable without leaving the panel (FR-044: shown
            // even with exactly one agent). Populated by RenderAgentState.
            _agentPicker = new AiAgentPicker
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0)
            };
            _agentPicker.AgentSelected += (_, agentId) => OnChatAgentSelected(agentId);
            _agentPicker.AddAgentRequested += (_, _) => OnChatAgentAddRequested();
            Grid.SetColumn(_agentPicker, 2);
            headerGrid.Children.Add(_agentPicker);

            // Spec 036 (US3, FR-018): copy the entire conversation, each turn attributed to its
            // speaker, built from _history at click time. Lives in the header so it is reachable
            // regardless of scroll position.
            var copyConversationButton = new Button
            {
                Content = "⧉ Conversation",
                ToolTip = "Copy the whole conversation to the clipboard",
                FontSize = 11,
                Padding = new Thickness(Spacing.Sm, 0, Spacing.Sm, 2),
                Margin = new Thickness(Spacing.Sm, 0, 0, 0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.9,
                FocusVisualStyle = FocusVisualStyles.HighStakes
            };
            copyConversationButton.SetResourceReference(Button.ForegroundProperty, ThemeTokens.TextSecondary);
            System.Windows.Automation.AutomationProperties.SetName(copyConversationButton, "Copy conversation");
            copyConversationButton.MouseEnter += (s, _) => ((Button)s).Opacity = 1.0;
            copyConversationButton.MouseLeave += (s, _) => ((Button)s).Opacity = 0.9;
            copyConversationButton.Click += OnCopyConversationClick;
            Grid.SetColumn(copyConversationButton, 3);
            headerGrid.Children.Add(copyConversationButton);

            headerBar.Child = headerGrid;
            DockPanel.SetDock(headerBar, Dock.Top);
            rootPanel.Children.Add(headerBar);

            // ──── Top (below header): status strip — schema-loading note (FR-029) and the
            // privacy-mode consequence note (FR-030). Collapsed unless one has something to say.
            _schemaStatusLabel = new TextBlock
            {
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(Spacing.Sm, 2, Spacing.Sm, 2),
                Visibility = Visibility.Collapsed
            };
            _schemaStatusLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);

            _privacyNoteLabel = new TextBlock
            {
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(Spacing.Sm, 2, Spacing.Sm, 2),
                Visibility = Visibility.Collapsed
            };
            _privacyNoteLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);

            var statusStack = new StackPanel { Orientation = Orientation.Vertical };
            statusStack.Children.Add(_schemaStatusLabel);
            statusStack.Children.Add(_privacyNoteLabel);

            _statusStrip = new Border
            {
                Child = statusStack,
                Visibility = Visibility.Collapsed
            };
            _statusStrip.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceElevated);
            DockPanel.SetDock(_statusStrip, Dock.Top);
            rootPanel.Children.Add(_statusStrip);

            // ──── Bottom: the composer — multi-line input + Send, visually separated from the
            // conversation by a divider. The old flat single-line bar blended into the panel:
            // no visible input border, no placeholder, no hint how to add a line. Enter sends,
            // Shift+Enter inserts a newline; the box grows 2..6 lines before scrolling.
            _sendButton = new Button
            {
                Content = "Send",
                MinWidth = 64,
                Padding = new Thickness(Spacing.Md, 6, Spacing.Md, 6),
                Margin = new Thickness(Spacing.Xs, Spacing.Sm, Spacing.Sm, Spacing.Sm),
                Cursor = Cursors.Hand,
                FontSize = 12,
                // Stretch to the composer's full height so button and growing input read as one
                // bar (the old bottom-aligned chip floated oddly beside a 2-line box).
                VerticalAlignment = VerticalAlignment.Stretch,
                FocusVisualStyle = FocusVisualStyles.HighStakes
            };
            ThemedButton.ApplyPrimary(_sendButton);
            _sendButton.Click += OnSendClick;
            DockPanel.SetDock(_sendButton, Dock.Right);

            _inputBox = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = false,
                TextWrapping = TextWrapping.Wrap,
                MinLines = 2,
                MaxLines = 6,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalContentAlignment = VerticalAlignment.Top,
                Padding = new Thickness(Spacing.Sm, 6, Spacing.Sm, 6),
                Margin = new Thickness(Spacing.Sm, Spacing.Sm, 0, Spacing.Sm),
                FontSize = 12,
                BorderThickness = new Thickness(1),
                FocusVisualStyle = FocusVisualStyles.HighStakes
            };
            System.Windows.Automation.AutomationProperties.SetName(_inputBox, "Chat input");
            _inputBox.SetResourceReference(TextBox.BackgroundProperty, ThemeTokens.SurfaceInput);
            _inputBox.SetResourceReference(TextBox.ForegroundProperty, ThemeTokens.TextPrimary);
            _inputBox.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, ThemeTokens.TextPrimary);
            _inputBox.SetResourceReference(TextBox.BorderBrushProperty, ThemeTokens.BorderStrong);
            _inputBox.KeyDown += OnInputKeyDown;

            // Placeholder overlay (the SnippetManagerDialog idiom): visible only while the box
            // is empty; IsHitTestVisible=false so clicks fall through to the TextBox.
            _inputPlaceholder = new TextBlock
            {
                Text = "Ask a question about this database…   (Enter sends · Shift+Enter for a new line)",
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Top,
                // Match the TextBox's inner text origin exactly: margin Sm + border 1 + padding Sm.
                Margin = new Thickness(Spacing.Sm + Spacing.Sm + 1, Spacing.Sm + 7, Spacing.Sm, 0)
            };
            _inputPlaceholder.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPlaceholder);
            // FR-015: the placeholder is a SIBLING of the TextBox, so the composer's IsEnabled
            // does not reach it — both signals must run through one method, or a gated composer
            // keeps inviting a question it cannot answer.
            _inputBox.TextChanged += (_, _) => UpdatePlaceholderVisibility();
            _inputBox.IsEnabledChanged += (_, _) => UpdatePlaceholderVisibility();

            var inputGrid = new Grid();
            inputGrid.Children.Add(_inputBox);
            inputGrid.Children.Add(_inputPlaceholder);

            var inputBar = new DockPanel
            {
                Margin = new Thickness(0)
            };
            inputBar.SetResourceReference(DockPanel.BackgroundProperty, ThemeTokens.SurfacePanel);
            inputBar.Children.Add(_sendButton);
            inputBar.Children.Add(inputGrid);

            var composerFrame = new Border
            {
                Child = inputBar,
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
            composerFrame.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderSplitter);

            DockPanel.SetDock(composerFrame, Dock.Bottom);
            rootPanel.Children.Add(composerFrame);

            // ──── Center: Scrollable conversation area ────
            _conversationPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0)
            };

            _thinkingIndicator = new TextBlock
            {
                Text = "Thinking...",
                FontStyle = FontStyles.Italic,
                FontSize = 12,
                Margin = new Thickness(Spacing.Md, Spacing.Sm, Spacing.Md, Spacing.Sm),
                Visibility = Visibility.Collapsed
            };
            _thinkingIndicator.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);

            var innerStack = new StackPanel { Orientation = Orientation.Vertical };
            innerStack.Children.Add(_conversationPanel);
            innerStack.Children.Add(_thinkingIndicator);

            _scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = innerStack
            };
            _scrollViewer.SetResourceReference(ScrollViewer.BackgroundProperty, ThemeTokens.SurfacePanel);

            rootPanel.Children.Add(_scrollViewer);

            Content = rootPanel;

            // Spec 037 (US1, FR-015/FR-018): no more unconditional greeting — with no usable
            // agent the panel shows the onboarding card and disables sending instead of
            // inviting a question it cannot answer; with one, the greeting names it.
            RefreshConfiguration(forceRefresh: true);

            // Spec 036 (US1, FR-027): the binding follows the ACTIVE EDITOR, which changes without
            // notice to a tool window — re-resolve on a light poll while visible, immediately on
            // load, and at every send (data-model V12). Same polling idiom as SchemaProgressMargin.
            _bindingTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(BindingRefreshIntervalMs)
            };
            _bindingTimer.Tick += (_, _) =>
            {
                RefreshBinding();
                RefreshConfiguration();   // spec 037 (R9): 5 s cached read; no-op when unchanged
            };
            Loaded += (_, _) => { RefreshBinding(); RefreshConfiguration(forceRefresh: true); _bindingTimer.Start(); };
            Unloaded += (_, _) => _bindingTimer.Stop();

            RefreshBinding();
            UpdatePlaceholderVisibility();   // FR-015: the initial state comes from neither event
        }

        /// <summary>
        /// FR-015: the placeholder invites a question, so it may show only while the composer can
        /// take one — empty AND enabled. Both signals (text, enabled) route here so they cannot
        /// diverge and leave an inviting placeholder over a composer that refuses input.
        /// </summary>
        private void UpdatePlaceholderVisibility()
            => _inputPlaceholder.Visibility =
                _inputBox.IsEnabled && string.IsNullOrEmpty(_inputBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

        /// <summary>
        /// Spec 036 (US1, FR-027/FR-029/FR-030): re-resolves the chat's binding to the active
        /// editor (header shows the bound <c>server.database</c>), polls the schema-loading signal
        /// (the same <see cref="MessageTypes.SchemaStatusRequest"/> the editor margin polls — no
        /// second progress mechanism), and surfaces the privacy-mode consequence.
        /// </summary>
        internal void RefreshBinding()
        {
            try
            {
                var sessionId = RefactorCommandHelper.TryGetActiveRealSessionId();
                if (!string.Equals(sessionId, _boundSessionId, StringComparison.Ordinal))
                {
                    _boundSessionId = sessionId;
                    _schemaReady = false; // rebinding restarts the loading-state poll
                }

                if (sessionId == null)
                {
                    SetDatabaseContext(string.Empty);
                    _schemaStatusLabel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // server.database for the header — the same caption detection the connection
                    // wiring uses (SsmsConnectionDetector).
                    var conn = SsmsConnectionDetector.TryDetectConnection(ServiceProvider.GlobalProvider);
                    if (conn != null && !string.IsNullOrEmpty(conn.Database))
                    {
                        SetDatabaseContext($"{conn.Server}.{conn.Database}");
                    }
                    else if (string.IsNullOrEmpty(_currentDatabase))
                    {
                        SetDatabaseContext(string.Empty);
                    }

                    if (!_schemaReady)
                    {
                        _ = PollSchemaStatusAsync(sessionId);
                    }
                }

                UpdatePrivacyNote();
                UpdateStatusStripVisibility();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "AiChatPanel: binding refresh failed");
            }
        }

        /// <summary>
        /// Spec 037 (US1, FR-015/FR-019/FR-020, research R9): re-reads AI configuration through
        /// the 5-second cache and re-renders the empty state / greeting when — and only when —
        /// the signature (agent count, active id, chat assignment, per-agent usability) changed.
        /// Runs on the existing 2-second binding tick (no new timer), immediately on
        /// <c>Loaded</c>, and immediately after the card's button returns from Options.
        /// </summary>
        internal void RefreshConfiguration() => RefreshConfiguration(forceRefresh: false);

        /// <summary>
        /// <paramref name="forceRefresh"/>: bypass the 5-second cache — the FR-019 path. The
        /// transition the user just made in Options must be visible the moment the dialog
        /// closes, not up to 5 seconds later.
        /// </summary>
        internal void RefreshConfiguration(bool forceRefresh)
        {
            try
            {
                AppSettings settings;
                if (forceRefresh || _cachedSettings == null ||
                    (DateTime.UtcNow - _configReadAtUtc).TotalSeconds >= ConfigCacheSeconds)
                {
                    _cachedSettings = LoadSettings();
                    _configReadAtUtc = DateTime.UtcNow;
                    // The verdicts belong to the settings they were computed from — drop them
                    // with the read that replaces those settings, never later (R9).
                    _canAnswerCache.Clear();
                }
                settings = _cachedSettings;

                var signature = ComputeSignature(settings.Ai);
                if (string.Equals(signature, _configSignature, StringComparison.Ordinal))
                    return;   // unchanged — the every-2-seconds tick stays cheap
                _configSignature = signature;

                RenderAgentState(settings.Ai);
                NoteClearedChatAssignment(settings.Ai);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "AiChatPanel: configuration refresh failed");
            }
        }

        /// <summary>
        /// The change-detection signature: agent count, active id, chat assignment, and each
        /// agent's id + name + can-answer bit. The name rides along because the greeting and the
        /// picker both render it (FR-037) — a rename must re-render without a panel rebuild.
        /// Anything not in it (a slider value, a reassigned key) changes nothing the panel
        /// renders, so it rightly costs no re-render.
        /// <para>
        /// The can-answer bit comes from <see cref="CanAnswerCached"/>, not straight from
        /// <see cref="AiChatEmptyState.CanAnswer"/>: this runs on every 2-second tick and that
        /// predicate ends in a synchronous DPAPI unwrap (R9). Instance, not static, for that
        /// memo alone.
        /// </para>
        /// </summary>
        private string ComputeSignature(AiSettings ai)
        {
            var sb = new System.Text.StringBuilder();
            var agents = ai.Agents;
            sb.Append(agents?.Count ?? 0).Append('|');
            sb.Append(ai.ActiveAgentId ?? string.Empty).Append('|');
            sb.Append(ai.FeatureAgents?.Chat ?? string.Empty).Append('|');
            if (agents != null)
            {
                foreach (var agent in agents)
                {
                    sb.Append(agent?.Id ?? string.Empty)
                      .Append('|')
                      .Append(agent?.Name ?? string.Empty)
                      .Append(CanAnswerCached(agent) ? '1' : '0')
                      .Append(';');
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// <see cref="AiChatEmptyState.CanAnswer"/>, memoised for the lifetime of the current
        /// <see cref="_cachedSettings"/> — see <see cref="_canAnswerCache"/> for why the raw
        /// predicate must not run per tick.
        /// </summary>
        private bool CanAnswerCached(AiAgent? agent)
        {
            if (agent == null) return false;
            if (_canAnswerCache.TryGetValue(agent, out var cached)) return cached;
            var canAnswer = AiChatEmptyState.CanAnswer(agent);
            _canAnswerCache[agent] = canAnswer;
            return canAnswer;
        }

        /// <summary>
        /// FR-015/FR-018/FR-021: the card when no agent can answer (gating sending), the
        /// greeting naming the resolved chat agent (S3) when one can. The empty state gates
        /// SENDING, not binding — header, schema poll and privacy note are untouched (FR-045).
        /// Also syncs the header picker to the resolved chat agent (FR-037).
        /// </summary>
        private void RenderAgentState(AiSettings ai)
        {
            var chatAgent = AiAgentResolver.ResolveFor(ai, AiFeature.Chat);
            var canAnswer = CanAnswerCached(chatAgent);   // memoised — no per-render DPAPI unwrap (R9)
            _agentUsable = canAnswer;
            _resolvedChatAgentName = chatAgent?.Name;
            _resolvedChatAgentId = chatAgent?.Id;
            _agentPicker.SetAgents(ai.Agents, chatAgent?.Id);

            if (canAnswer && chatAgent != null)
            {
                _offendingAgentId = string.Empty;
                if (_emptyStateCard != null)
                {
                    _conversationPanel.Children.Remove(_emptyStateCard);
                    _emptyStateCard = null;
                }
                if (_greetingBubble != null)
                    _conversationPanel.Children.Remove(_greetingBubble);
                _greetingBubble = CreateMessageBubble(GreetingText(chatAgent.Name), isUser: false);
                _conversationPanel.Children.Insert(0, _greetingBubble);
            }
            else
            {
                var (reasonText, offendingAgentId) = AiChatEmptyState.DetectReason(ai);
                _offendingAgentId = offendingAgentId;
                if (_greetingBubble != null)
                {
                    _conversationPanel.Children.Remove(_greetingBubble);
                    _greetingBubble = null;
                }
                if (_emptyStateCard != null)
                    _conversationPanel.Children.Remove(_emptyStateCard);
                _emptyStateCard = new AiChatEmptyState(reasonText);
                _emptyStateCard.AddAgentRequested += (_, _) => OnAddAgentRequested();
                // FR-021: an agent can break MID-conversation (key rotated, disabled from another
                // host), and the scrollback is then pinned at the bottom — inserted at the top the
                // reason and its Add button land off-screen beside a silently dead composer. Append
                // and scroll, the same as the FR-049 notice; on a first run the panel is empty, so
                // this is the position Insert(0) had.
                _conversationPanel.Children.Add(_emptyStateCard);
                ScrollToBottom();
            }

            _inputBox.IsEnabled = canAnswer;
            _sendButton.IsEnabled = canAnswer && !_isSending;
        }

        private static string GreetingText(string? agentName)
            => string.IsNullOrWhiteSpace(agentName)
                ? "Hello! I'm your AI SQL assistant. Ask me about queries, optimization, schema, or database best practices."
                : $"Hello! I'm your AI SQL assistant, powered by {agentName}. Ask me about queries, optimization, schema, or database best practices.";

        /// <summary>
        /// FR-049 / V16: when a refresh finds the chat assignment cleared — normalisation dropped
        /// the dangling id on load, or another host removed or disabled the assigned agent — the
        /// picker now follows the active agent; say so once, plainly, in the conversation idiom
        /// (the same small secondary line the attribution caption uses), rather than silently
        /// swapping who answers. Fires on the non-empty → empty transition only, so the
        /// unchanged-signature early-out in <see cref="RefreshConfiguration(bool)"/> bounds it to
        /// once per actual change — the once-per-feature-per-engine-process scope the engine
        /// enforces (V22) stays the only counter.
        /// </summary>
        private void NoteClearedChatAssignment(AiSettings ai)
        {
            var previous = _lastChatAssignment;
            var current = ai.FeatureAgents?.Chat ?? string.Empty;
            _lastChatAssignment = current;
            if (previous.Length == 0 || current.Length != 0) return;

            // The cleared id may still name a (now unusable) agent — name it when it does,
            // matching the name-the-agent spirit of FR-021/FR-057.
            string? clearedName = null;
            if (ai.Agents != null)
            {
                foreach (var agent in ai.Agents)
                {
                    if (agent != null && string.Equals(agent.Id, previous, StringComparison.Ordinal))
                    {
                        clearedName = agent.Name;
                        break;
                    }
                }
            }

            var newName = _resolvedChatAgentName;   // just set by RenderAgentState
            var subject = clearedName != null
                ? $"{clearedName} can no longer answer"
                : "The agent assigned to chat is no longer available";
            var notice = newName != null
                ? $"{subject} — chat will use the active agent, {newName}."
                : $"{subject}.";
            AddConversationNotice(notice);
        }

        /// <summary>
        /// A plainly stated system line in the conversation (FR-049). It is NOT part of
        /// <see cref="_history"/>: the copy-conversation path carries user and assistant turns,
        /// not configuration notices.
        /// </summary>
        private void AddConversationNotice(string text)
        {
            var notice = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(Spacing.Md, 2, Spacing.Md, 2)
            };
            notice.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            System.Windows.Automation.AutomationProperties.SetName(notice, ClearedAssignmentNoticeAutomationName);
            _conversationPanel.Children.Add(notice);
            ScrollToBottom();
        }

        /// <summary>
        /// FR-017: the card's one button deep-links Options to the AI Assistance page — on the
        /// offending agent when there is one, on an implicit Add ("") when there is none — via
        /// the shared save-and-notify path. The panel must never call
        /// <see cref="ConfigManager.Save(AppSettings)"/> itself: skipping the
        /// AnalysisSettingsChanged notification would leave the engine serving stale settings
        /// and FR-019's "no restart" would silently fail.
        /// </summary>
        private void OnAddAgentRequested()
        {
            ShowOptionsRoute("AI Assistance", _offendingAgentId);
            RefreshConfiguration(forceRefresh: true);   // immediate — do not wait for the poll
        }

        /// <summary>
        /// Spec 037 (US3, FR-038/FR-039/FR-040, research R7): the picker changes the agent
        /// <b>for chat</b>, and nothing else — it writes <c>FeatureAgents.Chat</c>, never
        /// <c>ActiveAgentId</c> (a ghost-text assignment must not be undone by trying a
        /// different chat model). Picking the agent that is already active clears the
        /// assignment to "" so chat keeps following the active agent (S3). Persisted through
        /// the shared save-and-notify path — the panel never calls
        /// <see cref="ConfigManager.Save(AppSettings)"/> itself. The conversation is NOT
        /// cleared and a mid-flight switch is allowed: the choice takes effect from the next
        /// message, and the in-flight answer keeps the agent that produced it.
        /// </summary>
        private void OnChatAgentSelected(string agentId)
        {
            try
            {
                var settings = LoadSettings();
                var ai = settings.Ai;
                var assignment = string.Equals(agentId, ai.ActiveAgentId, StringComparison.Ordinal)
                    ? string.Empty
                    : agentId;
                ai.FeatureAgents ??= new FeatureAgentAssignments();
                if (string.Equals(ai.FeatureAgents.Chat ?? string.Empty, assignment, StringComparison.Ordinal))
                {
                    // No change — nothing to persist, but the picker may still be sitting on the
                    // trailing "Add agent…" row: adding the FIRST agent makes it active, so the
                    // assignment stays "" and the header would keep advertising "Add agent…" as
                    // the answering agent (FR-037/FR-041). Re-sync without writing config — and
                    // when the signature genuinely did not change the refresh early-outs, so
                    // re-render explicitly (the cancel branch of OnChatAgentAddRequested's
                    // precedent).
                    var signatureBefore = _configSignature;
                    RefreshConfiguration(forceRefresh: true);
                    if (string.Equals(signatureBefore, _configSignature, StringComparison.Ordinal))
                        RenderAgentState(ai);
                    return;
                }

                ai.FeatureAgents.Chat = assignment;
                Commands.OptionsCommand.SaveAndNotify(settings);
                _lastChatAssignment = assignment;   // user-initiated — not a cleared-assignment notice (FR-049)
                RefreshConfiguration(forceRefresh: true);   // re-resolve greeting + picker now
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AiChatPanel: failed to persist chat agent selection");
            }
        }

        /// <summary>
        /// Spec 037 (US3, FR-041): the picker's trailing "Add agent…" entry opens Options on the
        /// AI Assistance page (no agent preselected) via the same shared route as the onboarding
        /// card; when the dialog saved a new agent AND that agent can answer, it becomes the
        /// picker's selection — persisted like any other selection (FR-040). A still-blank new
        /// agent is never auto-selected: pinning <c>FeatureAgents.Chat</c> to an unusable id
        /// would fire the "can no longer answer" notice on the next normalised load, seconds
        /// after adding. A cancel simply re-syncs the picker to the resolved agent.
        /// </summary>
        private void OnChatAgentAddRequested()
        {
            try
            {
                var before = new HashSet<string>(StringComparer.Ordinal);
                var agents = LoadSettings().Ai.Agents;
                if (agents != null)
                {
                    foreach (var agent in agents)
                    {
                        if (agent?.Id != null) before.Add(agent.Id);
                    }
                }

                ShowOptionsRoute("AI Assistance", null);

                AiAgent? added = null;
                var after = LoadSettings().Ai.Agents;
                if (after != null)
                {
                    foreach (var agent in after)
                    {
                        if (agent?.Id != null && !before.Contains(agent.Id))
                        {
                            added = agent;
                            break;
                        }
                    }
                }

                if (added != null && AiChatEmptyState.CanAnswer(added))
                {
                    OnChatAgentSelected(added.Id);   // persists + refreshes
                }
                else if (added != null)
                {
                    // Added but cannot answer yet (no provider/model/key): leave the picker on
                    // the resolved agent. The agent count changed the signature, so go through
                    // the refresh — it re-renders and keeps _configSignature honest.
                    RefreshConfiguration(forceRefresh: true);
                }
                else
                {
                    // Cancelled (or nothing added): the settings signature is unchanged, so
                    // RefreshConfiguration would early-out — re-render explicitly to revert
                    // the picker off the Add entry and back onto the resolved agent.
                    RenderAgentState(LoadSettings().Ai);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AiChatPanel: Add agent route failed");
            }
        }

        /// <summary>
        /// Polls the engine's schema-loading state for the bound session (FR-029) via the existing
        /// <see cref="MessageTypes.SchemaStatusRequest"/> contract. While the cache is not complete
        /// the panel tells the user the schema is still loading; the engine reads the live cache at
        /// answer time, so the answer uses the schema once it is available.
        /// </summary>
        private async Task PollSchemaStatusAsync(string sessionId)
        {
            try
            {
                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                    return;

                var resp = await client.SendRequestAsync<SchemaStatusResponse, SchemaStatusRequest>(
                    MessageTypes.SchemaStatusRequest, new SchemaStatusRequest { SessionId = sessionId },
                    timeoutMs: 3000);

                if (!string.Equals(sessionId, _boundSessionId, StringComparison.Ordinal))
                    return; // rebound while the poll was in flight — the next tick owns the strip

                // Phase: 0 = NotLoaded, 1 = PhaseA (objects), 2 = PhaseB (columns+FKs), 3 = Complete.
                _schemaReady = resp.Exists && resp.Phase >= 3;
                if (_schemaReady)
                {
                    _schemaStatusLabel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    var dbName = !string.IsNullOrEmpty(resp.DatabaseName) ? resp.DatabaseName : _currentDatabase;
                    _schemaStatusLabel.Text =
                        $"Schema for {dbName} is still loading — answers will use it as soon as it is ready.";
                    _schemaStatusLabel.Visibility = Visibility.Visible;
                }
                UpdateStatusStripVisibility();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "AiChatPanel: schema status poll failed");
            }
        }

        /// <summary>
        /// FR-030: when privacyMode is "anonymous", identifiers are hashed before anything leaves
        /// the machine (IdentifierMap non-empty engine-side), so the assistant cannot see real
        /// object names. Say that plainly and name the setting, instead of letting the user think
        /// the assistant is confused.
        /// </summary>
        private void UpdatePrivacyNote()
        {
            // config.json is a small disk read — cache it briefly so the 2 s binding poll stays cheap.
            if ((DateTime.UtcNow - _privacyModeReadAtUtc).TotalSeconds > PrivacyModeRefreshSeconds)
            {
                try
                {
                    _lastPrivacyMode = ConfigManager.Load().Ai.PrivacyMode ?? string.Empty;
                    _privacyModeReadAtUtc = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "AiChatPanel: failed to read privacy mode");
                    return; // keep showing whatever the previous read decided
                }
            }

            if (string.Equals(_lastPrivacyMode.Trim(), "anonymous", StringComparison.OrdinalIgnoreCase))
            {
                _privacyNoteLabel.Text =
                    "Privacy mode is 'anonymous': your object names are hashed before anything is sent, " +
                    "so the assistant cannot see real table or column names and may not name them in " +
                    "answers. Change privacyMode in AKML SQL → Options → AI Assistance to allow real names.";
                _privacyNoteLabel.Visibility = Visibility.Visible;
            }
            else
            {
                _privacyNoteLabel.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateStatusStripVisibility()
        {
            _statusStrip.Visibility =
                _schemaStatusLabel.Visibility == Visibility.Visible ||
                _privacyNoteLabel.Visibility == Visibility.Visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        /// <summary>
        /// Updates the header to show the current binding (<c>server.database</c>), or the explicit
        /// not-connected state when empty. Called by <see cref="RefreshBinding"/> whenever the active
        /// editor's connection changes (FR-027).
        /// </summary>
        public void SetDatabaseContext(string databaseName)
        {
            _currentDatabase = databaseName;
            _headerLabel.Text = !string.IsNullOrEmpty(databaseName)
                ? databaseName
                : NotConnectedHeaderText;
            // The header ellipsizes long server.database names — the tooltip keeps the full one.
            _headerLabel.ToolTip = !string.IsNullOrEmpty(databaseName) ? databaseName : null;
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            // Enter sends; Shift+Enter falls through and inserts a newline (multi-line composer).
            if (e.Key == Key.Enter
                && (Keyboard.Modifiers & ModifierKeys.Shift) == 0
                && !_isSending && _agentUsable)
            {
                e.Handled = true;
                _ = SendMessageAsync();
            }
        }

        private void OnSendClick(object sender, RoutedEventArgs e)
        {
            if (!_isSending && _agentUsable)
            {
                _ = SendMessageAsync();
            }
        }

        private async Task SendMessageAsync()
        {
            // FR-018: while no agent can answer, no request may be sent — the input and button
            // are disabled, and the handlers above refuse even a synthetic click.
            if (!_agentUsable)
                return;

            var text = _inputBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(text))
                return;

            // Who is expected to answer, captured NOW: a mid-flight picker switch re-resolves
            // _resolvedChatAgentName, but this in-flight answer must keep the agent it was
            // sent to (contract Part 3 — the in-flight answer keeps the agent that produced it).
            var expectedAgentName = _resolvedChatAgentName;
            var expectedAgentId = _resolvedChatAgentId;

            _inputBox.Text = string.Empty;
            _isSending = true;
            _sendButton.IsEnabled = false;
            _thinkingIndicator.Visibility = Visibility.Visible;

            // Add user message to conversation
            AddUserMessage(text);

            try
            {
                // Spec 036 (US1, FR-021/FR-028): bind to the ACTIVE EDITOR's real session, resolved
                // at send time on every message (V12). Refuse to send when unbound — a fabricated
                // id silently produced an empty-schema answer (R1).
                RefreshBinding();
                var sessionId = RefactorCommandHelper.TryGetActiveRealSessionId();
                if (string.IsNullOrEmpty(sessionId))
                {
                    AddAssistantMessage(NoConnectionMessage);
                    return;
                }

                var manager = EngineLifecycle.Manager;
                if (manager?.Client == null || !manager.Client.IsConnected)
                {
                    AddAssistantMessage("Error: AI engine is not connected. Please check that the AKML SQL engine is running.");
                    return;
                }

                var request = new AiChatRequest
                {
                    SessionId = sessionId!,
                    Message = text,
                    History = new List<ChatTurnDto>(_history)
                };

                var response = await manager.Client.SendRequestAsync<AiChatResponse, AiChatRequest>(
                    MessageTypes.AiChat, request,
                    timeoutMs: AiIpcTimeouts.ForAiRequestMs(ConfigManager.Load(), AiFeature.Chat));

                if (response.Success && !string.IsNullOrEmpty(response.Response))
                {
                    // Add to history — with the answering agent's name riding in the parallel
                    // list (FR-042/FR-043): null for the user turn, the response's AgentName
                    // (null from an older engine) for the assistant turn.
                    _history.Add(new ChatTurnDto { Role = "user", Content = text });
                    _historyAgentNames.Add(null);
                    _history.Add(new ChatTurnDto { Role = "assistant", Content = response.Response! });
                    _historyAgentNames.Add(response.AgentName);

                    // Add assistant response to conversation, attributed to whoever answered
                    AddAssistantMessage(response.Response!, response.CodeActions,
                        response.AgentName, expectedAgentName);

                    // Show latency info
                    if (response.LatencyMs > 0)
                    {
                        Log.Debug("AiChatPanel: response received in {LatencyMs}ms, tokens={Tokens}",
                            response.LatencyMs, response.TokensUsed);
                    }
                }
                else
                {
                    var error = response.ErrorMessage ?? "Unknown error";
                    AddLiveFailureMessage(error, expectedAgentName, expectedAgentId);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AiChatPanel: failed to send message");
                AddLiveFailureMessage(AiIpcTimeouts.DescribeFailure(ex, ConfigManager.Load(), AiFeature.Chat),
                    expectedAgentName, expectedAgentId);
            }
            finally
            {
                _isSending = false;
                // Restore the gated state, not a blanket true — while no agent can answer the
                // button stays disabled (FR-018).
                _sendButton.IsEnabled = _agentUsable;
                _thinkingIndicator.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Spec 037 (US5, FR-057): the render for a failed LIVE request. The text names the
        /// agent when — and only when — the failure is configuration-caused
        /// (<see cref="AiIpcTimeouts.DescribeLiveFailure"/> and
        /// <see cref="AiIpcTimeouts.IsConfigurationCaused"/> decide together), and the route to
        /// fix it is a real button deep-linking Options to that agent through the shared
        /// <see cref="ShowOptionsRoute"/> path — the panel never saves settings itself. A
        /// non-configuration failure (quota, timeout, engine down) renders its bare message and
        /// offers no route: naming an agent there would blame it for a state its settings
        /// cannot fix.
        /// </summary>
        private void AddLiveFailureMessage(string error, string? agentName, string? agentId)
        {
            AddAssistantMessage($"Error: {AiIpcTimeouts.DescribeLiveFailure(error, agentName)}");

            if (string.IsNullOrEmpty(agentId) || !AiIpcTimeouts.IsConfigurationCaused(error))
                return;

            // An agent may carry an id and a blank name (the editor writes the name unguarded),
            // and "Open  settings" is what a screen reader would then read out — fall back to the
            // name-free wording, the same guard DescribeLiveFailure applies to the message itself.
            var label = string.IsNullOrWhiteSpace(agentName)
                ? "Open AI agent settings"
                : $"Open {agentName} settings";
            var routeButton = new Button
            {
                Content = label,
                Margin = new Thickness(Spacing.Md, 2, Spacing.Md, 2),
                Padding = new Thickness(Spacing.Sm, Spacing.Xs, Spacing.Sm, Spacing.Xs),
                FontSize = 11,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
                FocusVisualStyle = FocusVisualStyles.HighStakes
            };
            routeButton.SetResourceReference(Button.BackgroundProperty, ThemeTokens.SurfaceElevated);
            routeButton.SetResourceReference(Button.ForegroundProperty, ThemeTokens.TextLink);
            routeButton.SetResourceReference(Button.BorderBrushProperty, ThemeTokens.TextLink);
            System.Windows.Automation.AutomationProperties.SetName(routeButton, label);
            var targetAgentId = agentId!;
            routeButton.Click += (_, _) => ShowOptionsRoute("AI Assistance", targetAgentId);
            _conversationPanel.Children.Add(routeButton);
            ScrollToBottom();
        }

        /// <summary>
        /// Adds a user message bubble to the conversation panel.
        /// </summary>
        private void AddUserMessage(string text)
        {
            var bubble = CreateMessageBubble(text, isUser: true);
            _conversationPanel.Children.Add(bubble);
            ScrollToBottom();
        }

        /// <summary>
        /// Adds an assistant message bubble to the conversation panel,
        /// optionally with code action buttons.
        /// <para>
        /// Spec 037 (US3, FR-042): <paramref name="agentName"/> is the agent that ACTUALLY
        /// answered (<c>AiChatResponse.AgentName</c>); <paramref name="selectedAgentName"/> is
        /// the agent the message was sent to. The bubble carries a small attribution caption —
        /// nothing when <paramref name="agentName"/> is null (an older engine — no guessed
        /// name), and a plainly stated fallback line when the two differ (FR-052).
        /// </para>
        /// </summary>
        private void AddAssistantMessage(string text, List<CodeActionDto>? codeActions = null,
            string? agentName = null, string? selectedAgentName = null)
        {
            var bubble = CreateMessageBubble(text, isUser: false,
                attribution: AttributionText(agentName, selectedAgentName));
            _conversationPanel.Children.Add(bubble);

            // Add code-action buttons (e.g., "Copy this SQL"). Border + foreground both use TextLink
            // so the affordance reads as clickable in either theme; background is SurfaceElevated.
            if (codeActions != null && codeActions.Count > 0)
            {
                // Spec 036 (US3, FR-015): with several SQL blocks in one message each copy action
                // must say which block it belongs to. Single-block messages keep the engine label.
                // Each block's copy and insert buttons share one horizontal row (the row owns the
                // outer margin; the copy button keeps only a trailing gap against the insert one).
                var blockNumber = 0;
                foreach (var action in codeActions)
                {
                    blockNumber++;
                    var label = codeActions.Count > 1
                        ? $"Copy SQL block {blockNumber} of {codeActions.Count}"
                        : action.Label;

                    var actionButton = new Button
                    {
                        Content = label,
                        Tag = action.Code,
                        Margin = new Thickness(0, 0, Spacing.Sm, 0),
                        Padding = new Thickness(Spacing.Sm, Spacing.Xs, Spacing.Sm, Spacing.Xs),
                        FontSize = 11,
                        BorderThickness = new Thickness(1),
                        Cursor = Cursors.Hand,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        FocusVisualStyle = FocusVisualStyles.HighStakes
                    };
                    // FR-020: every copy control is keyboard-reachable (Button is a tab stop by
                    // default) and carries an accessible name.
                    System.Windows.Automation.AutomationProperties.SetName(actionButton, label);
                    actionButton.SetResourceReference(Button.BackgroundProperty, ThemeTokens.SurfaceElevated);
                    actionButton.SetResourceReference(Button.ForegroundProperty, ThemeTokens.TextLink);
                    actionButton.SetResourceReference(Button.BorderBrushProperty, ThemeTokens.TextLink);
                    actionButton.Click += OnCodeActionClick;

                    // Spec 037: the insert companion drops this block's SQL into the active query
                    // editor at the caret — same visual idiom as the copy button.
                    var insertLabel = codeActions.Count > 1
                        ? $"⇩ Insert SQL block {blockNumber} of {codeActions.Count}"
                        : "⇩ Insert into query";
                    var insertButton = new Button
                    {
                        Content = insertLabel,
                        Tag = action.Code,
                        Padding = new Thickness(Spacing.Sm, Spacing.Xs, Spacing.Sm, Spacing.Xs),
                        FontSize = 11,
                        BorderThickness = new Thickness(1),
                        Cursor = Cursors.Hand,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        FocusVisualStyle = FocusVisualStyles.HighStakes
                    };
                    System.Windows.Automation.AutomationProperties.SetName(insertButton, insertLabel);
                    insertButton.SetResourceReference(Button.BackgroundProperty, ThemeTokens.SurfaceElevated);
                    insertButton.SetResourceReference(Button.ForegroundProperty, ThemeTokens.TextLink);
                    insertButton.SetResourceReference(Button.BorderBrushProperty, ThemeTokens.TextLink);
                    insertButton.Click += OnInsertSqlClick;

                    var row = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(Spacing.Md, 2, Spacing.Md, 2)
                    };
                    row.Children.Add(actionButton);
                    row.Children.Add(insertButton);
                    _conversationPanel.Children.Add(row);
                }
            }

            ScrollToBottom();
        }

        /// <summary>
        /// Spec 037 (US3/US4, FR-042/FR-052, contract Part 3): the attribution caption for one
        /// assistant answer. <paramref name="agentName"/> is who answered (the engine's
        /// <c>AiChatResponse.AgentName</c> — since T073 the engine names the TRUE answerer: the
        /// resolved agent, a fallback-chain agent, or the offline provider); <paramref name="selectedAgentName"/>
        /// is who the message was sent to. Null/blank agent name → <c>null</c> (render nothing
        /// rather than a guessed name — the older-engine case). A different answerer means the
        /// engine's fallback flag fired — stated plainly, never hidden behind a silently
        /// swapped name.
        /// </summary>
        internal static string? AttributionText(string? agentName, string? selectedAgentName)
        {
            if (string.IsNullOrWhiteSpace(agentName)) return null;
            if (string.IsNullOrWhiteSpace(selectedAgentName) ||
                string.Equals(agentName, selectedAgentName, StringComparison.Ordinal))
                return agentName;
            return $"{agentName} answered — {selectedAgentName} was unavailable.";
        }

        /// <summary>
        /// Creates a message bubble (Border containing the text plus a per-message copy button)
        /// for the conversation. User messages right-align with
        /// <see cref="ThemeTokens.ChatUserBubble"/> background; assistant messages left-align
        /// with <see cref="ThemeTokens.ChatAssistantBubble"/>.
        /// <para>
        /// Spec 036 (US3, FR-017): the text host is a read-only, borderless, transparent
        /// <see cref="TextBox"/> — the standard WPF way to make static text selectable (pointer
        /// drag + Ctrl+C copies exactly the selection). A read-only TextBox shows no caret and,
        /// with <c>AcceptsReturn</c> off, does not swallow the Enter key the input box binds to
        /// send (research R9). The per-message copy button is preserved (FR-016).
        /// </para>
        /// <para>
        /// Spec 037 (US3, FR-042): <paramref name="attribution"/> — already rendered by
        /// <see cref="AttributionText"/> — adds a small caption above the text saying which
        /// agent produced the answer. Null adds nothing (older-engine answers stay caption-free).
        /// </para>
        /// </summary>
        private static Border CreateMessageBubble(string text, bool isUser, string? attribution = null)
        {
            var textHost = new TextBox
            {
                Text = text,
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                AcceptsReturn = false,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FocusVisualStyle = FocusVisualStyles.HighStakes
            };
            textHost.SetResourceReference(TextBox.ForegroundProperty, ThemeTokens.TextPrimary);
            System.Windows.Automation.AutomationProperties.SetName(textHost, "Message text");

            var copyButton = new Button
            {
                Content = "⧉",
                Tag = text,
                ToolTip = "Copy message",
                FontSize = 11,
                Padding = new Thickness(4, 0, 4, 2),
                Margin = new Thickness(Spacing.Sm, 0, 0, 0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top,
                Opacity = 0.8,
                FocusVisualStyle = FocusVisualStyles.HighStakes
            };
            copyButton.SetResourceReference(Button.ForegroundProperty, ThemeTokens.TextSecondary);
            System.Windows.Automation.AutomationProperties.SetName(copyButton, "Copy message");
            copyButton.MouseEnter += (s, _) => ((Button)s).Opacity = 1.0;
            copyButton.MouseLeave += (s, _) => ((Button)s).Opacity = 0.8;
            copyButton.Click += OnCopyMessageClick;

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(textHost, 0);
            Grid.SetColumn(copyButton, 1);
            layout.Children.Add(textHost);
            layout.Children.Add(copyButton);

            UIElement bubbleContent = layout;
            if (!string.IsNullOrEmpty(attribution))
            {
                var caption = new TextBlock
                {
                    Text = attribution,
                    FontSize = 10,
                    FontStyle = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 2)
                };
                caption.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
                System.Windows.Automation.AutomationProperties.SetName(caption, AttributionAutomationName);

                var stack = new StackPanel { Orientation = Orientation.Vertical };
                stack.Children.Add(caption);
                stack.Children.Add(layout);
                bubbleContent = stack;
            }

            var bubble = new Border
            {
                Child = bubbleContent,
                Padding = new Thickness(10, Spacing.Sm, 10, Spacing.Sm),
                Margin = new Thickness(
                    isUser ? 60 : Spacing.Sm,  // Left margin
                    Spacing.Xs,
                    isUser ? Spacing.Sm : 60,  // Right margin
                    Spacing.Xs),
                CornerRadius = new CornerRadius(Spacing.Sm),
                // Visible edge: on the white light-theme panel a borderless slate bubble is
                // indistinguishable from the background (user feedback).
                BorderThickness = new Thickness(1),
                HorizontalAlignment = isUser
                    ? HorizontalAlignment.Right
                    : HorizontalAlignment.Left,
                MaxWidth = 500
            };
            bubble.SetResourceReference(Border.BackgroundProperty,
                isUser ? ThemeTokens.ChatUserBubble : ThemeTokens.ChatAssistantBubble);
            bubble.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderStrong);

            return bubble;
        }

        /// <summary>
        /// Copies the whole message text of a bubble to the clipboard and shows a transient
        /// "Copied" state on its button (reverts after 1.5 s). Spec 036 (US3, FR-019): a failed
        /// copy tells the user ("⚠ Copy failed", transient) and the bubble stays put, re-copyable.
        /// </summary>
        private static void OnCopyMessageClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is string message))
                return;

            if (!TryCopyToClipboard(message))
            {
                FlashButtonContent(button, "⚠ Copy failed");
                return;
            }

            FlashButtonContent(button, "✓ Copied");
        }

        /// <summary>
        /// Spec 036 (US3, FR-018): copies the entire conversation from <see cref="_history"/>,
        /// every turn attributed to its speaker, order preserved.
        /// <para>
        /// Spec 037 (US3, FR-043): an assistant turn's speaker is the agent that produced it
        /// (from the parallel <see cref="_historyAgentNames"/> list), so a mixed conversation
        /// pastes as "You:" / "Claude (work):" / "Kimi:" blocks. A turn without a recorded
        /// name — an older engine, or history seeded before attribution existed — keeps the
        /// pre-attribution "Assistant" label. Spacing, trailing trim, the clipboard funnel and
        /// the flash are unchanged.
        /// </para>
        /// </summary>
        private void OnCopyConversationClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button))
                return;

            if (_history.Count == 0)
            {
                FlashButtonContent(button, "Nothing to copy");
                return;
            }

            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < _history.Count; i++)
            {
                var turn = _history[i];
                var agentName = i < _historyAgentNames.Count ? _historyAgentNames[i] : null;
                var speaker = string.Equals(turn.Role, "user", StringComparison.OrdinalIgnoreCase)
                    ? "You"
                    : (string.IsNullOrWhiteSpace(agentName) ? "Assistant" : agentName);
                sb.Append(speaker).Append(':').AppendLine();
                sb.AppendLine(turn.Content);
                sb.AppendLine();
            }

            if (!TryCopyToClipboard(sb.ToString().TrimEnd()))
            {
                FlashButtonContent(button, "⚠ Copy failed");
                return;
            }

            FlashButtonContent(button, "✓ Copied");
        }

        /// <summary>FR-019: one clipboard funnel for the panel — success or a logged failure.</summary>
        private static bool TryCopyToClipboard(string text)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "AiChatPanel: failed to copy to clipboard");
                return false;
            }
        }

        /// <summary>
        /// The true pre-flash content of one flashing button and its single live countdown.
        /// Kept off the button's <c>Tag</c>, which already carries the code/message payload.
        /// </summary>
        private sealed class FlashState
        {
            internal object? Original;
            internal DispatcherTimer? Timer;
        }

        /// <summary>Per-button flash state; weak on the button so a discarded bubble still collects.</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, FlashState> FlashStates
            = new System.Runtime.CompilerServices.ConditionalWeakTable<Button, FlashState>();

        /// <summary>Shows a transient confirmation/failure on a copy button, reverting after 1.5 s.</summary>
        private static void FlashButtonContent(Button button, string feedback)
        {
            // A feedback string must never be captured as the "original": a second click inside
            // the window used to stack a second timer whose original was the first flash's text,
            // latching e.g. "✓ Inserted" on the button forever. Stash the true original on the
            // FIRST flash only, and restart the one timer instead of stacking another.
            var state = FlashStates.GetValue(button, _ => new FlashState());
            if (state.Timer == null)
            {
                state.Original = button.Content;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                timer.Tick += (_, __) =>
                {
                    timer.Stop();
                    state.Timer = null;
                    button.Content = state.Original;
                };
                state.Timer = timer;
                button.Content = feedback;
                timer.Start();
                return;
            }

            state.Timer.Stop();   // still flashing — this click restarts the one countdown
            button.Content = feedback;
            state.Timer.Start();
        }

        /// <summary>
        /// Handles a code action button click by copying the SQL to clipboard.
        /// FR-019: a failed copy says so and the action stays re-copyable.
        /// </summary>
        private static void OnCodeActionClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string code)
            {
                if (!TryCopyToClipboard(code))
                {
                    FlashButtonContent(button, "⚠ Copy failed");
                    return;
                }
                button.Content = "Copied!";
            }
        }

        /// <summary>
        /// Spec 037: the insert companion to <see cref="OnCodeActionClick"/> — drops the block's
        /// SQL into the active query editor at the caret. When no query editor is active the
        /// inserter reports false and the button says so transiently (the flash reverts after
        /// 1.5 s, so the action stays retryable).
        /// </summary>
        private static void OnInsertSqlClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string code)
            {
                if (AiEditorSqlInserter.TryInsertAtCaret(code))
                {
                    FlashButtonContent(button, "✓ Inserted");
                    return;
                }
                FlashButtonContent(button, "⚠ No active query");
            }
        }

        /// <summary>
        /// Scrolls the conversation area to the bottom to show the latest message.
        /// </summary>
        private void ScrollToBottom()
        {
            _scrollViewer.ScrollToEnd();
        }
    }
}
