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
using Serilog;
using Task = System.Threading.Tasks.Task;

namespace AkmlSql.Shell.Shared.Ai
{
    /// <summary>
    /// T052 / T054: WPF UserControl for the AI Chat panel.
    /// Built entirely in code (no XAML) since this is a shared project (.projitems).
    /// Layout: header with database context, scrollable conversation area, input bar with Send button.
    /// </summary>
    internal sealed class AiChatPanel : UserControl
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
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                Padding = new Thickness(8, 6, 8, 6)
            };

            var headerStack = new StackPanel { Orientation = Orientation.Horizontal };

            var titleLabel = new TextBlock
            {
                Text = "AI Chat",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            headerStack.Children.Add(titleLabel);

            _headerLabel = new TextBlock
            {
                Text = string.Empty,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 170)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            headerStack.Children.Add(_headerLabel);

            headerBar.Child = headerStack;
            DockPanel.SetDock(headerBar, Dock.Top);
            rootPanel.Children.Add(headerBar);

            // ──── Bottom: Input bar with TextBox + Send button ────
            var inputBar = new DockPanel
            {
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
                Margin = new Thickness(0)
            };

            _sendButton = new Button
            {
                Content = "Send",
                MinWidth = 60,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(4, 4, 4, 4),
                Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 12
            };
            _sendButton.Click += OnSendClick;
            DockPanel.SetDock(_sendButton, Dock.Right);
            inputBar.Children.Add(_sendButton);

            _inputBox = new TextBox
            {
                AcceptsReturn = false,
                AcceptsTab = false,
                TextWrapping = TextWrapping.Wrap,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(4, 4, 0, 4),
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromRgb(51, 51, 55)),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70))
            };
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
                Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 140)),
                FontSize = 12,
                Margin = new Thickness(12, 8, 12, 8),
                Visibility = Visibility.Collapsed
            };

            var innerStack = new StackPanel { Orientation = Orientation.Vertical };
            innerStack.Children.Add(_conversationPanel);
            innerStack.Children.Add(_thinkingIndicator);

            _scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                Content = innerStack
            };

            rootPanel.Children.Add(_scrollViewer);

            Content = rootPanel;

            // Add a welcome message
            AddAssistantMessage("Hello! I'm your AI SQL assistant. Ask me about queries, optimization, schema, or database best practices.");

            // T054: Detect current database context
            DetectDatabaseContext();
        }

        /// <summary>
        /// T054: Detects the current database name from config or connection state
        /// and displays it in the header. This is a best-effort approach; full database
        /// change detection can be added as a follow-up.
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
                    MessageTypes.AiChat, request, timeoutMs: 30000);

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
                AddAssistantMessage($"Error: {ex.Message}");
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

            // Add "Copy" buttons for any code actions
            if (codeActions != null && codeActions.Count > 0)
            {
                foreach (var action in codeActions)
                {
                    var actionButton = new Button
                    {
                        Content = action.Label,
                        Tag = action.Code,
                        Margin = new Thickness(12, 2, 12, 2),
                        Padding = new Thickness(8, 4, 8, 4),
                        FontSize = 11,
                        Background = new SolidColorBrush(Color.FromRgb(60, 60, 65)),
                        Foreground = new SolidColorBrush(Color.FromRgb(78, 201, 176)),
                        BorderThickness = new Thickness(1),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(78, 201, 176)),
                        Cursor = Cursors.Hand,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    actionButton.Click += OnCodeActionClick;
                    _conversationPanel.Children.Add(actionButton);
                }
            }

            ScrollToBottom();
        }

        /// <summary>
        /// Creates a message bubble (Border containing a TextBlock) for the conversation.
        /// User messages appear on the right with blue background; assistant messages on the left with dark background.
        /// </summary>
        private static Border CreateMessageBubble(string text, bool isUser)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = Brushes.White,
                Padding = new Thickness(0)
            };

            var bubble = new Border
            {
                Child = textBlock,
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(
                    isUser ? 60 : 8,  // Left margin
                    4,
                    isUser ? 8 : 60,  // Right margin
                    4),
                CornerRadius = new CornerRadius(8),
                Background = isUser
                    ? new SolidColorBrush(Color.FromRgb(0, 99, 177))
                    : new SolidColorBrush(Color.FromRgb(51, 51, 55)),
                HorizontalAlignment = isUser
                    ? HorizontalAlignment.Right
                    : HorizontalAlignment.Left,
                MaxWidth = 500
            };

            return bubble;
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
