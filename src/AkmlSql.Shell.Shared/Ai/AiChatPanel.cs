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

        private readonly StackPanel _conversationPanel;
        private readonly ScrollViewer _scrollViewer;
        private readonly TextBox _inputBox;
        private readonly Button _sendButton;
        private readonly TextBlock _headerLabel;
        private readonly TextBlock _thinkingIndicator;
        private readonly Border _statusStrip;
        private readonly TextBlock _schemaStatusLabel;
        private readonly TextBlock _privacyNoteLabel;
        private readonly DispatcherTimer _bindingTimer;
        private readonly List<ChatTurnDto> _history = new();
        private string _currentDatabase = string.Empty;
        private string? _boundSessionId;
        private bool _schemaReady;
        private string _lastPrivacyMode = string.Empty;
        private DateTime _privacyModeReadAtUtc = DateTime.MinValue;
        private bool _isSending;

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

            var headerStack = new StackPanel { Orientation = Orientation.Horizontal };

            var titleLabel = new TextBlock
            {
                Text = "AI Chat",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            headerStack.Children.Add(titleLabel);

            _headerLabel = new TextBlock
            {
                Text = string.Empty,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(Spacing.Md, 0, 0, 0)
            };
            _headerLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            headerStack.Children.Add(_headerLabel);

            // Spec 036 (US3, FR-018): copy the entire conversation, each turn attributed to its
            // speaker, built from _history at click time. Lives in the header so it is reachable
            // regardless of scroll position.
            var copyConversationButton = new Button
            {
                Content = "⧉ Conversation",
                ToolTip = "Copy the whole conversation to the clipboard",
                FontSize = 11,
                Padding = new Thickness(Spacing.Sm, 0, Spacing.Sm, 2),
                Margin = new Thickness(Spacing.Md, 0, 0, 0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.75,
                FocusVisualStyle = FocusVisualStyles.HighStakes
            };
            copyConversationButton.SetResourceReference(Button.ForegroundProperty, ThemeTokens.TextSecondary);
            System.Windows.Automation.AutomationProperties.SetName(copyConversationButton, "Copy conversation");
            copyConversationButton.MouseEnter += (s, _) => ((Button)s).Opacity = 1.0;
            copyConversationButton.MouseLeave += (s, _) => ((Button)s).Opacity = 0.75;
            copyConversationButton.Click += OnCopyConversationClick;
            headerStack.Children.Add(copyConversationButton);

            headerBar.Child = headerStack;
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

            // ──── Bottom: Input bar with TextBox + Send button ────
            var inputBar = new DockPanel
            {
                Margin = new Thickness(0)
            };
            inputBar.SetResourceReference(DockPanel.BackgroundProperty, ThemeTokens.SurfacePanel);

            _sendButton = new Button
            {
                Content = "Send",
                MinWidth = 60,
                Padding = new Thickness(Spacing.Md, 6, Spacing.Md, 6),
                Margin = new Thickness(Spacing.Xs, Spacing.Xs, Spacing.Xs, Spacing.Xs),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 12,
                FocusVisualStyle = FocusVisualStyles.HighStakes
            };
            _sendButton.SetResourceReference(Button.BackgroundProperty, ThemeTokens.AccentPrimary);
            _sendButton.SetResourceReference(Button.ForegroundProperty, ThemeTokens.TextOnAccent);
            _sendButton.Click += OnSendClick;
            DockPanel.SetDock(_sendButton, Dock.Right);
            inputBar.Children.Add(_sendButton);

            _inputBox = new TextBox
            {
                AcceptsReturn = false,
                AcceptsTab = false,
                TextWrapping = TextWrapping.Wrap,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(Spacing.Sm, 6, Spacing.Sm, 6),
                Margin = new Thickness(Spacing.Xs, Spacing.Xs, 0, Spacing.Xs),
                FontSize = 12,
                BorderThickness = new Thickness(1),
                FocusVisualStyle = FocusVisualStyles.HighStakes
            };
            System.Windows.Automation.AutomationProperties.SetName(_inputBox, "Chat input");
            _inputBox.SetResourceReference(TextBox.BackgroundProperty, ThemeTokens.SurfaceInput);
            _inputBox.SetResourceReference(TextBox.ForegroundProperty, ThemeTokens.TextPrimary);
            _inputBox.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, ThemeTokens.TextPrimary);
            _inputBox.SetResourceReference(TextBox.BorderBrushProperty, ThemeTokens.BorderDefault);
            _inputBox.KeyDown += OnInputKeyDown;
            inputBar.Children.Add(_inputBox);

            DockPanel.SetDock(inputBar, Dock.Bottom);
            rootPanel.Children.Add(inputBar);

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

            // Add a welcome message
            AddAssistantMessage("Hello! I'm your AI SQL assistant. Ask me about queries, optimization, schema, or database best practices.");

            // Spec 036 (US1, FR-027): the binding follows the ACTIVE EDITOR, which changes without
            // notice to a tool window — re-resolve on a light poll while visible, immediately on
            // load, and at every send (data-model V12). Same polling idiom as SchemaProgressMargin.
            _bindingTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(BindingRefreshIntervalMs)
            };
            _bindingTimer.Tick += (_, _) => RefreshBinding();
            Loaded += (_, _) => { RefreshBinding(); _bindingTimer.Start(); };
            Unloaded += (_, _) => _bindingTimer.Stop();

            RefreshBinding();
        }

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
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !_isSending)
            {
                e.Handled = true;
                _ = SendMessageAsync();
            }
        }

        private void OnSendClick(object sender, RoutedEventArgs e)
        {
            if (!_isSending)
            {
                _ = SendMessageAsync();
            }
        }

        private async Task SendMessageAsync()
        {
            var text = _inputBox.Text?.Trim();
            if (string.IsNullOrEmpty(text))
                return;

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
                    SessionId = sessionId,
                    Message = text,
                    History = new List<ChatTurnDto>(_history)
                };

                var response = await manager.Client.SendRequestAsync<AiChatResponse, AiChatRequest>(
                    MessageTypes.AiChat, request,
                    timeoutMs: AiIpcTimeouts.ForAiRequestMs(ConfigManager.Load()));

                if (response.Success && !string.IsNullOrEmpty(response.Response))
                {
                    // Add to history
                    _history.Add(new ChatTurnDto { Role = "user", Content = text });
                    _history.Add(new ChatTurnDto { Role = "assistant", Content = response.Response });

                    // Add assistant response to conversation
                    AddAssistantMessage(response.Response, response.CodeActions);

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
                    AddAssistantMessage($"Error: {error}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AiChatPanel: failed to send message");
                AddAssistantMessage($"Error: {AiIpcTimeouts.DescribeFailure(ex, ConfigManager.Load())}");
            }
            finally
            {
                _isSending = false;
                _sendButton.IsEnabled = true;
                _thinkingIndicator.Visibility = Visibility.Collapsed;
            }
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
        /// </summary>
        private void AddAssistantMessage(string text, List<CodeActionDto>? codeActions = null)
        {
            var bubble = CreateMessageBubble(text, isUser: false);
            _conversationPanel.Children.Add(bubble);

            // Add code-action buttons (e.g., "Copy this SQL"). Border + foreground both use TextLink
            // so the affordance reads as clickable in either theme; background is SurfaceElevated.
            if (codeActions != null && codeActions.Count > 0)
            {
                // Spec 036 (US3, FR-015): with several SQL blocks in one message each copy action
                // must say which block it belongs to. Single-block messages keep the engine label.
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
                        Margin = new Thickness(Spacing.Md, 2, Spacing.Md, 2),
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
                    _conversationPanel.Children.Add(actionButton);
                }
            }

            ScrollToBottom();
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
        /// </summary>
        private static Border CreateMessageBubble(string text, bool isUser)
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
                Opacity = 0.55,
                FocusVisualStyle = FocusVisualStyles.HighStakes
            };
            copyButton.SetResourceReference(Button.ForegroundProperty, ThemeTokens.TextSecondary);
            System.Windows.Automation.AutomationProperties.SetName(copyButton, "Copy message");
            copyButton.MouseEnter += (s, _) => ((Button)s).Opacity = 1.0;
            copyButton.MouseLeave += (s, _) => ((Button)s).Opacity = 0.55;
            copyButton.Click += OnCopyMessageClick;

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(textHost, 0);
            Grid.SetColumn(copyButton, 1);
            layout.Children.Add(textHost);
            layout.Children.Add(copyButton);

            var bubble = new Border
            {
                Child = layout,
                Padding = new Thickness(10, Spacing.Sm, 10, Spacing.Sm),
                Margin = new Thickness(
                    isUser ? 60 : Spacing.Sm,  // Left margin
                    Spacing.Xs,
                    isUser ? Spacing.Sm : 60,  // Right margin
                    Spacing.Xs),
                CornerRadius = new CornerRadius(Spacing.Sm),
                HorizontalAlignment = isUser
                    ? HorizontalAlignment.Right
                    : HorizontalAlignment.Left,
                MaxWidth = 500
            };
            bubble.SetResourceReference(Border.BackgroundProperty,
                isUser ? ThemeTokens.ChatUserBubble : ThemeTokens.ChatAssistantBubble);

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
            foreach (var turn in _history)
            {
                var speaker = string.Equals(turn.Role, "user", StringComparison.OrdinalIgnoreCase)
                    ? "You"
                    : "Assistant";
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

        /// <summary>Shows a transient confirmation/failure on a copy button, reverting after 1.5 s.</summary>
        private static void FlashButtonContent(Button button, string feedback)
        {
            var original = button.Content;
            button.Content = feedback;
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1500)
            };
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                button.Content = original;
            };
            timer.Start();
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
        /// Scrolls the conversation area to the bottom to show the latest message.
        /// </summary>
        private void ScrollToBottom()
        {
            _scrollViewer.ScrollToEnd();
        }
    }
}
