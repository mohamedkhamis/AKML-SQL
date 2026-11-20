#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AkmlSql.Core.Text;
using AkmlSql.Shell.Shared.Ui.Theme;
using Typography = AkmlSql.Shell.Shared.Ui.Theme.Typography;   // not System.Windows.Documents.Typography

namespace AkmlSql.Shell.Shared.Ai
{
    /// <summary>
    /// Spec 046 (US2, FR-020/FR-021, contracts/chat-rendering.md Part 2, research R6): renders a
    /// parsed chat answer (<see cref="ChatMarkdown"/>) the way Copilot Chat does — prose with real
    /// paragraphs, headings, lists, bold and inline code, and every fenced block in its own code
    /// panel carrying its own Copy and (for SQL) Insert into query actions. The answer used to be one
    /// plain text box showing the fences, and its actions sat in a detached row under the bubble.
    ///
    /// <para>Prose is a read-only <see cref="RichTextBox"/> (selectable; Ctrl+C copies the
    /// selection — spec 036 FR-017 kept) and code is a read-only, unwrapped <see cref="TextBox"/> in
    /// the editor font. All colours come from theme tokens through resource references, so the
    /// panel follows a theme switch. Both text hosts forward the mouse wheel to the conversation's
    /// scroll viewer: a text box's own scroll viewer otherwise swallows it and the conversation
    /// stops scrolling whenever the pointer is over an answer.</para>
    /// </summary>
    internal static class ChatMessageRenderer
    {
        /// <summary>Accessible name of every message text host (prose of an answer, a user's message).</summary>
        internal const string MessageTextAutomationName = "Message text";

        /// <summary>Accessible name of a code panel's text.</summary>
        internal const string CodeTextAutomationName = "Code text";

        internal const string CopyContent = "⧉ Copy";
        internal const string InsertContent = "⇩ Insert into query";
        internal const string CopySqlToolTip = "Copy this SQL";
        internal const string CopyCodeToolTip = "Copy this code";
        internal const string InsertToolTip = "Insert this SQL into the query window at the caret";

        internal const string CopiedFeedback = "✓ Copied";
        internal const string CopyFailedFeedback = "⚠ Copy failed";
        internal const string InsertedFeedback = "✓ Inserted";
        internal const string NoQueryWindowFeedback = "⚠ No query window";

        /// <summary>The accessible name of a block's Copy button (data-model.md › block actions).</summary>
        internal static string CopyLabel(ChatCodeBlock block, int count)
        {
            var noun = block.IsSql ? "SQL" : "code";
            return count > 1 ? $"Copy {noun} block {block.Index} of {count}" : $"Copy {noun}";
        }

        /// <summary>The accessible name of a SQL block's Insert button.</summary>
        internal static string InsertLabel(ChatCodeBlock block, int count)
            => count > 1 ? $"Insert SQL block {block.Index} of {count}" : "Insert into query";

        /// <summary>
        /// Builds the answer body. <paramref name="copy"/> and <paramref name="insert"/> do the work
        /// and report success; the renderer shows the outcome on the button (it reverts after 1.5 s,
        /// so a failed action stays retryable).
        /// </summary>
        internal static StackPanel Build(IReadOnlyList<ChatBlock> blocks, Func<string, bool> copy, Func<string, bool> insert)
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical };
            var codeCount = ChatMarkdown.CountCodeBlocks(blocks);
            FlowDocument? prose = null;

            foreach (var block in blocks)
            {
                if (block is ChatCodeBlock code)
                {
                    if (prose != null)
                    {
                        stack.Children.Add(ProseHost(prose));
                        prose = null;
                    }
                    stack.Children.Add(CodePanel(code, codeCount, copy, insert));
                    continue;
                }

                prose ??= NewDocument();
                prose.Blocks.Add(ToFlowBlock(block));
            }

            if (prose != null)
                stack.Children.Add(ProseHost(prose));
            return stack;
        }

        /// <summary>
        /// Sends a wheel turn over a text host to its parent, so the conversation scrolls. A text
        /// box's template scroll viewer marks the event handled even when it cannot scroll.
        /// </summary>
        internal static void ForwardMouseWheel(UIElement element)
        {
            element.PreviewMouseWheel += (sender, e) =>
            {
                if (e.Handled || !(sender is FrameworkElement source) || !(source.Parent is UIElement parent))
                    return;
                e.Handled = true;
                parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = sender
                });
            };
        }

        // ── prose ───────────────────────────────────────────────────────────────────────────

        private static FlowDocument NewDocument()
        {
            var document = new FlowDocument
            {
                PagePadding = new Thickness(0),
                FontFamily = Typography.UiFont,
                FontSize = 12,
                TextAlignment = TextAlignment.Left
            };
            document.SetResourceReference(FlowDocument.ForegroundProperty, ThemeTokens.TextPrimary);
            return document;
        }

        private static RichTextBox ProseHost(FlowDocument document)
        {
            var box = new RichTextBox
            {
                Document = document,
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                FocusVisualStyle = FocusVisualStyles.HighStakes
            };
            box.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            AutomationProperties.SetName(box, MessageTextAutomationName);
            ForwardMouseWheel(box);
            return box;
        }

        private static Block ToFlowBlock(ChatBlock block)
        {
            switch (block)
            {
                case ChatHeading heading:
                {
                    var paragraph = new Paragraph
                    {
                        Margin = new Thickness(0, 4, 0, 4),
                        FontWeight = Typography.WeightSemiBold,
                        FontSize = heading.Level == 1 ? Typography.H4 : heading.Level == 2 ? Typography.BodyStrong : Typography.Body
                    };
                    AddInlines(paragraph.Inlines, heading.Inlines);
                    return paragraph;
                }
                case ChatListBlock list:
                {
                    var flowList = new List
                    {
                        MarkerStyle = list.Ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                        StartIndex = Math.Max(1, list.Start),
                        Margin = new Thickness(0, 0, 0, 6),
                        Padding = new Thickness(18, 0, 0, 0)
                    };
                    foreach (var item in list.Items)
                    {
                        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
                        AddInlines(paragraph.Inlines, item);
                        flowList.ListItems.Add(new ListItem(paragraph));
                    }
                    return flowList;
                }
                case ChatParagraph prose:
                {
                    var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 6) };
                    AddInlines(paragraph.Inlines, prose.Inlines);
                    return paragraph;
                }
                default:
                    return new Paragraph();
            }
        }

        private static void AddInlines(InlineCollection target, IReadOnlyList<ChatInline> inlines)
        {
            foreach (var inline in inlines)
            {
                switch (inline.Kind)
                {
                    case ChatInlineKind.Bold:
                        target.Add(new Bold(new Run(inline.Text)));
                        break;
                    case ChatInlineKind.Code:
                    {
                        var run = new Run(inline.Text) { FontFamily = Typography.MonoFont };
                        run.SetResourceReference(TextElement.BackgroundProperty, ThemeTokens.SurfaceInputReadOnly);
                        target.Add(run);
                        break;
                    }
                    default:
                        AddTextWithBreaks(target, inline.Text);
                        break;
                }
            }
        }

        private static void AddTextWithBreaks(InlineCollection target, string text)
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0) target.Add(new LineBreak());
                if (lines[i].Length > 0) target.Add(new Run(lines[i]));
            }
        }

        // ── code panels ─────────────────────────────────────────────────────────────────────

        private static Border CodePanel(ChatCodeBlock code, int count, Func<string, bool> copy, Func<string, bool> insert)
        {
            var language = new TextBlock
            {
                Text = code.Language.Length > 0 ? code.Language : "sql",
                FontSize = Typography.Small,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(Spacing.Sm, 0, Spacing.Sm, 0)
            };
            language.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var copyButton = ActionButton(CopyContent, CopyLabel(code, count),
                code.IsSql ? CopySqlToolTip : CopyCodeToolTip, code.Code);
            copyButton.Click += (_, _) =>
                AiChatPanel.FlashButtonContent(copyButton, copy(code.Code) ? CopiedFeedback : CopyFailedFeedback);
            actions.Children.Add(copyButton);

            if (code.IsSql)
            {
                var insertButton = ActionButton(InsertContent, InsertLabel(code, count), InsertToolTip, code.Code);
                insertButton.Margin = new Thickness(Spacing.Xs, 0, 0, 0);
                insertButton.Click += (_, _) =>
                    AiChatPanel.FlashButtonContent(insertButton, insert(code.Code) ? InsertedFeedback : NoQueryWindowFeedback);
                actions.Children.Add(insertButton);
            }

            var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, Spacing.Xs, Spacing.Xs, Spacing.Xs) };
            DockPanel.SetDock(actions, Dock.Right);
            header.Children.Add(actions);
            header.Children.Add(language);

            var codeText = new TextBox
            {
                Text = code.Code,
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                AcceptsReturn = false,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = Typography.MonoFont,
                FontSize = 12,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(Spacing.Sm, 0, Spacing.Sm, Spacing.Sm),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                FocusVisualStyle = FocusVisualStyles.HighStakes
            };
            codeText.SetResourceReference(TextBox.ForegroundProperty, ThemeTokens.TextPrimary);
            AutomationProperties.SetName(codeText, CodeTextAutomationName);
            ForwardMouseWheel(codeText);

            var body = new StackPanel { Orientation = Orientation.Vertical };
            body.Children.Add(header);
            body.Children.Add(codeText);

            var panel = new Border
            {
                Child = body,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, Spacing.Xs, 0, Spacing.Sm)
            };
            panel.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceCanvas);
            panel.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderDefault);
            return panel;
        }

        private static Button ActionButton(string content, string automationName, string toolTip, string code)
        {
            var button = new Button
            {
                Content = content,
                Tag = code,
                ToolTip = toolTip,
                Padding = new Thickness(Spacing.Sm, 1, Spacing.Sm, 1),
                FontSize = Typography.Small,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                FocusVisualStyle = FocusVisualStyles.HighStakes
            };
            AutomationProperties.SetName(button, automationName);
            // Link text on the panel surface meets 4.5:1 in every theme; on the elevated surface it
            // reads 4.1:1 (the web contrast scan, ThemeContrastScanTests, measures the same pair).
            button.SetResourceReference(Button.BackgroundProperty, ThemeTokens.SurfacePanel);
            button.SetResourceReference(Button.ForegroundProperty, ThemeTokens.TextLink);
            button.SetResourceReference(Button.BorderBrushProperty, ThemeTokens.TextLink);
            return button;
        }
    }
}
