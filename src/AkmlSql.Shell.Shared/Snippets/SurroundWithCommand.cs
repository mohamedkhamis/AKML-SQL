#nullable enable
using System;
using System.ComponentModel.Design;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Refactoring;
using AkmlSql.Shell.Shared.Ui.Theme;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Serilog;
using Constants = AkmlSql.Core.Constants;
using WinForms = System.Windows.Forms;

namespace AkmlSql.Shell.Shared.Snippets
{
    /// <summary>
    /// Spec 030 T045 / FR-034 — "Surround With". Requires a non-empty selection, shows a picker of the
    /// snippets whose <c>surroundsWith</c> flag is set, then expands the chosen snippet with the selection
    /// supplied as <c>$SELECTEDTEXT$</c> and REPLACES the selection span with the expanded text. The engine
    /// (T040/T047) already resolves $SELECTEDTEXT$ and reports the post-expansion selection range; this
    /// command supplies SelectedText (the Tab-expand path never does) and applies a selection-aware insert
    /// that the editor completion path does not provide. Bound to Ctrl+K, Ctrl+S
    /// (<see cref="CommandIds.CmdSnippetSurroundWith"/> = 0x091C).
    /// </summary>
    internal sealed class SurroundWithCommand
    {
        private SurroundWithCommand(Package package, OleMenuCommandService commandService)
        {
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdSnippetSurroundWith);
            var item  = new OleMenuCommand(Execute, cmdId);
            item.BeforeQueryStatus += (s, _) => { if (s is OleMenuCommand c) { c.Visible = true; c.Enabled = true; } };
            commandService.AddCommand(item);
        }

        public static SurroundWithCommand? Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
            => Instance = new SurroundWithCommand(package, commandService);

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var ctx = RefactorCommandHelper.TryGetActiveEditor();
                if (ctx == null || string.IsNullOrEmpty(ctx.DocumentText) || ctx.SelectionLength <= 0)
                {
                    WinForms.MessageBox.Show("Select the code you want to surround first.",
                        Constants.ProductName, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                    return;
                }

                int selStart = ctx.SelectionStart;
                int selLen   = ctx.SelectionLength;
                if (selStart < 0 || selLen < 0 || selStart + selLen > ctx.DocumentText.Length)
                {
                    WinForms.MessageBox.Show("The selection is no longer valid — try selecting the code again.",
                        Constants.ProductName, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                    return;
                }
                var selectionText = ctx.DocumentText.Substring(selStart, selLen);

                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                {
                    WinForms.MessageBox.Show("The AKML SQL engine is not running yet — try again in a moment.",
                        Constants.ProductName, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                    return;
                }

                // List snippets and keep only those flagged surroundsWith.
                SnippetListResponse? list = null;
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    list = await client.SendRequestAsync<SnippetListResponse, SnippetListRequest>(
                        MessageTypes.SnippetList,
                        new SnippetListRequest { Query = string.Empty, SourceFilter = 0 },
                        timeoutMs: 10_000);
                });

                var candidates = (list?.Snippets ?? Array.Empty<SnippetInfo>())
                    .Where(s => s.SurroundsWith)
                    .OrderBy(s => s.Shortcode, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (candidates.Length == 0)
                {
                    WinForms.MessageBox.Show(
                        "No surround-with snippets are defined. Create a snippet with \"Surrounds With\" enabled " +
                        "and use $SELECTEDTEXT$ in its body to mark where the selection should go.",
                        Constants.ProductName, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                    return;
                }

                var chosen = PickSnippet(candidates);
                if (chosen == null) return;

                SnippetExpandResponse? response = null;
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    response = await client.SendRequestAsync<SnippetExpandResponse, SnippetExpandRequest>(
                        MessageTypes.SnippetExpand,
                        new SnippetExpandRequest
                        {
                            SessionId      = ctx.SessionId,
                            Shortcode      = chosen.Shortcode,
                            SelectedText   = selectionText, // the Tab-expand path never sets this
                            FormatOnExpand = false
                        },
                        timeoutMs: 10_000);
                });

                if (response == null || !response.Success || string.IsNullOrEmpty(response.ExpandedText))
                {
                    WinForms.MessageBox.Show("Surround With failed: " + (response?.ErrorMessage ?? "no response from the engine."),
                        Constants.ProductName, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                    return;
                }

                // Re-capture the live selection from the view NOW (after the modal picker + two IPC
                // round-trips). The user may have dismissed the dialog and clicked elsewhere, making the
                // previously-captured selStart/selLen stale or outside the current snapshot.
                {
                    var liveView = ctx.View;
                    if (liveView.Selection.IsEmpty)
                    {
                        WinForms.MessageBox.Show(
                            "The selection was lost while the picker was open — try selecting the code again.",
                            Constants.ProductName, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                        return;
                    }
                    int liveStart = liveView.Selection.Start.Position.Position;
                    int liveLen   = liveView.Selection.End.Position.Position - liveStart;
                    int liveDocLen = liveView.TextBuffer.CurrentSnapshot.Length;
                    if (liveStart < 0 || liveLen <= 0 || liveStart + liveLen > liveDocLen)
                    {
                        WinForms.MessageBox.Show(
                            "The selection is no longer valid — try selecting the code again.",
                            Constants.ProductName, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                        return;
                    }
                    selStart = liveStart;
                    selLen   = liveLen;
                }

                ApplySurround(ctx.View, selStart, selLen, response);
                Log.Information("SurroundWith: applied snippet '{Shortcode}' around the selection", chosen.Shortcode);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SurroundWithCommand.Execute failed");
                WinForms.MessageBox.Show("Surround With failed: " + ex.Message,
                    Constants.ProductName, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Replaces the selection span [selStart, selStart+selLen) with the expanded snippet text,
        /// normalizing the engine's LF-joined text to the document newline, then placing the caret at
        /// <c>CursorOffset</c> and re-selecting [SelectionStartOffset, SelectionEndOffset] when both are
        /// >= 0. All three engine offsets are into the LF text, so each is shifted independently by the
        /// number of LFs preceding it once LF grows to CRLF (the editor's InsertSnippetExpansion only
        /// shifts the caret — it ignores the selection range and replaces an abbreviation, not a selection,
        /// so it cannot be reused as-is). Must run on the UI thread.
        /// </summary>
        private static void ApplySurround(IWpfTextView view, int selStart, int selLen, SnippetExpandResponse response)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var snapshot = view.TextBuffer.CurrentSnapshot;
                if (selStart < 0 || selLen < 0 || selStart + selLen > snapshot.Length) return;

                string text = response.ExpandedText ?? string.Empty;
                int cursor   = response.CursorOffset;
                int rangeBeg = response.SelectionStartOffset;
                int rangeEnd = response.SelectionEndOffset;
                if (cursor < 0) cursor = text.Length; // caret clamps to end when $CURSOR$ absent

                string nl = GetBufferNewLine(view);
                if (nl != "\n" && text.IndexOf('\n') >= 0)
                {
                    // Each offset is into the LF text → shift by (LFs before it) * (nl.Length - 1).
                    cursor   = ShiftForNewline(text, cursor,   nl);
                    rangeBeg = ShiftForNewline(text, rangeBeg, nl);
                    rangeEnd = ShiftForNewline(text, rangeEnd, nl);
                    text = text.Replace("\r\n", "\n").Replace("\n", nl);
                }

                view.TextBuffer.Replace(new Span(selStart, selLen), text);

                var after = view.TextBuffer.CurrentSnapshot;

                // Re-select the returned range first (caret then lands at CursorOffset).
                if (rangeBeg >= 0 && rangeEnd >= 0 && rangeEnd >= rangeBeg)
                {
                    int absBeg = selStart + rangeBeg;
                    int absEnd = selStart + rangeEnd;
                    if (absBeg >= 0 && absEnd <= after.Length)
                    {
                        view.Selection.Select(
                            new SnapshotSpan(after, absBeg, absEnd - absBeg),
                            isReversed: false);
                    }
                }
                else
                {
                    view.Selection.Clear();
                }

                int caretAbs = selStart + cursor;
                if (caretAbs >= 0 && caretAbs <= after.Length)
                    view.Caret.MoveTo(new SnapshotPoint(after, caretAbs));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "SurroundWith: apply failed at {Pos}", selStart);
            }
        }

        /// <summary>
        /// Returns <paramref name="offset"/> shifted right by (number of STANDALONE '\n' before it) *
        /// (nl.Length-1), accounting for LF→CRLF growth. Only standalone LFs grow: the surround path
        /// injects the user's selection ($SELECTEDTEXT$) verbatim, so the engine text is MIXED — body
        /// line-joins are bare '\n' (grow to CRLF) but the embedded selection's own '\r\n' pairs survive
        /// the Replace("\r\n","\n").Replace("\n",nl) round-trip with NET-ZERO growth. Counting the '\n'
        /// inside a '\r\n' would add a spurious +1 per embedded CRLF (compounding), drifting the caret /
        /// reselection. Returns the offset unchanged when it is negative (absent marker).
        /// </summary>
        private static int ShiftForNewline(string lfText, int offset, string nl)
        {
            if (offset < 0) return offset;
            int upto = Math.Min(offset, lfText.Length);
            int lfBefore = 0;
            for (int i = 0; i < upto; i++)
                if (lfText[i] == '\n' && (i == 0 || lfText[i - 1] != '\r')) lfBefore++;
            return offset + lfBefore * (nl.Length - 1);
        }

        private static string GetBufferNewLine(IWpfTextView view)
        {
            try
            {
                var snap = view.TextBuffer.CurrentSnapshot;
                for (int i = 0; i < snap.LineCount; i++)
                {
                    var lb = snap.GetLineFromLineNumber(i).GetLineBreakText();
                    if (!string.IsNullOrEmpty(lb)) return lb;
                }
            }
            catch { }
            return "\r\n";
        }

        // -------------------------------------------------------------------
        // Minimal themed snippet picker (programmatic WPF, no XAML)
        // -------------------------------------------------------------------

        private static SnippetInfo? PickSnippet(SnippetInfo[] candidates)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var picker = new SurroundPickerDialog(candidates);
            return picker.ShowModal() == true ? picker.Selected : null;
        }

        private sealed class SurroundPickerDialog : DialogWindow
        {
            private readonly ListBox _list;
            public SnippetInfo? Selected { get; private set; }

            public SurroundPickerDialog(SnippetInfo[] candidates)
            {
                Title = "AKML SQL - Surround With";
                Width = 460;
                Height = 380;
                MinWidth = 360;
                MinHeight = 260;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                HasHelpButton = false;

                var res = ThemeRegistry.Instance.Resources;
                var bg          = (SolidColorBrush)res[ThemeTokens.SurfaceCanvas];
                var fg          = (SolidColorBrush)res[ThemeTokens.TextPrimary];
                var border      = (SolidColorBrush)res[ThemeTokens.BorderDefault];
                var editorPanel = (SolidColorBrush)res[ThemeTokens.SurfaceElevated];
                var placeholder = (SolidColorBrush)res[ThemeTokens.TextPlaceholder];
                var accent      = (SolidColorBrush)res[ThemeTokens.AccentPrimary];

                var grid = new Grid { Background = bg, Margin = new Thickness(8) };
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var heading = new TextBlock
                {
                    Text = "Choose a snippet to surround the selection:",
                    Foreground = fg,
                    Margin = new Thickness(2, 0, 0, 6),
                };
                Grid.SetRow(heading, 0);
                grid.Children.Add(heading);

                _list = new ListBox
                {
                    Background = editorPanel,
                    Foreground = fg,
                    BorderBrush = border,
                    BorderThickness = new Thickness(1),
                };
                foreach (var s in candidates)
                {
                    var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 4, 2) };
                    panel.Children.Add(new TextBlock
                    {
                        Text = s.Shortcode ?? "(no shortcode)",
                        FontWeight = FontWeights.SemiBold,
                        Foreground = accent,
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                    panel.Children.Add(new TextBlock
                    {
                        Text = "  -  " + (s.Name ?? string.Empty),
                        Foreground = placeholder,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    });
                    _list.Items.Add(new ListBoxItem { Content = panel, Tag = s });
                }
                if (_list.Items.Count > 0) _list.SelectedIndex = 0;
                _list.MouseDoubleClick += (s, _) => Commit();
                _list.KeyDown += (s, ev) => { if (ev.Key == Key.Enter) { Commit(); ev.Handled = true; } };
                Grid.SetRow(_list, 1);
                grid.Children.Add(_list);

                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 8, 0, 0),
                };
                var ok = new Button { Content = "Surround", MinWidth = 80, Padding = new Thickness(12, 4, 12, 4), IsDefault = true };
                ok.Background = accent;
                ok.Foreground = fg;
                ok.Click += (s, _) => Commit();
                buttons.Children.Add(ok);

                var cancel = new Button { Content = "Cancel", MinWidth = 80, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(12, 4, 12, 4), IsCancel = true };
                cancel.Click += (s, _) => { DialogResult = false; Close(); };
                buttons.Children.Add(cancel);

                Grid.SetRow(buttons, 2);
                grid.Children.Add(buttons);

                Content = grid;
                Loaded += (s, _) => _list.Focus();
            }

            private void Commit()
            {
                if (_list.SelectedItem is ListBoxItem lbi && lbi.Tag is SnippetInfo info)
                {
                    Selected = info;
                    DialogResult = true;
                    Close();
                }
            }
        }
    }
}
