#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Ui.Theme;
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
        private readonly StackPanel _conversationPanel;
        private readonly ScrollViewer _scrollViewer;
        private readonly TextBox _inputBox;
        private readonly Button _sendButton;
        private readonly TextBlock _headerLabel;
        private readonly TextBlock _thinkingIndicator;
        private readonly List<ChatTurnDto> _history = new();
        private string _currentDatabase = string.Empty;
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

            headerBar.Child = headerStack;
            DockPanel.SetDock(headerBar, Dock.Top);
            rootPanel.Children.Add(headerBar);

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

            // Detect current database context
            DetectDatabaseContext();
        }

        /// <summary>
        /// Detects the current database name from config or connection state and displays it
        /// in the header. Best-effort; full database change detection can be added as a follow-up.
        /// </summary>
        private void DetectDatabaseContext()
        {
            try
            {
                // Try to get database name from the current active session or config
                var settings = ConfigManager.Load();
                if (settings.Ai.Enabled)
                {
                    _headerLabel.Text = settings.Ai.Provider != null
                        ? $"Provider: {settings.Ai.Provider}"
                        : string.Empty;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "AiChatPanel: failed to detect database context");
            }
        }

        /// <summary>
        /// Updates the header to show the current database name.
        /// Can be called externally when the active connection changes.
        /// </summary>
        public void SetDatabaseContext(string databaseName)
        {
            _currentDatabase = databaseName;
            _headerLabel.Text = !string.IsNullOrEmpty(databaseName)
                ? $"Database: {databaseName}"
                : string.Empty;
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
                var manager = EngineLifecycle.Manager;
                if (manager?.Client == null || !manager.Client.IsConnected)
                {
                    AddAssistantMessage("Error: AI engine is not connected. Please check that the AKML SQL engine is running.");
                    return;
                }

                var request = new AiChatRequest
                {
                    SessionId = Guid.NewGuid().ToString("N"),
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
                foreach (var action in codeActions)
                {
                    var actionButton = new Button
                    {
                        Content = action.Label,
                        Tag = action.Code,
                        Margin = new Thickness(Spacing.Md, 2, Spacing.Md, 2),
                        Padding = new Thickness(Spacing.Sm, Spacing.Xs, Spacing.Sm, Spacing.Xs),
                        FontSize = 11,
                        BorderThickness = new Thickness(1),
                        Cursor = Cursors.Hand,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        FocusVisualStyle = FocusVisualStyles.HighStakes
                    };
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
        /// with <see cref="ThemeTokens.ChatAssistantBubble"/>. The copy button exists because
        /// bubbles are TextBlocks — not selectable — so without it a chat message cannot be
        /// copied at all (web-edition parity; the code-action buttons only copy their SQL).
        /// </summary>
        private static Border CreateMessageBubble(string text, bool isUser)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Padding = new Thickness(0)
            };
            textBlock.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);

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
            Grid.SetColumn(textBlock, 0);
            Grid.SetColumn(copyButton, 1);
            layout.Children.Add(textBlock);
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
        /// "Copied" state on its button (reverts after 1.5 s).
        /// </summary>
        private static void OnCopyMessageClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is string message))
                return;

            try
            {
                Clipboard.SetText(message);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "AiChatPanel: failed to copy message to clipboard");
                return;
            }

            var original = button.Content;
            button.Content = "✓ Copied";
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
        /// </summary>
        private static void OnCodeActionClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string code)
            {
                try
                {
                    Clipboard.SetText(code);
                    button.Content = "Copied!";
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "AiChatPanel: failed to copy to clipboard");
                }
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
