using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Navigation;
using System.ComponentModel.Composition;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Analysis;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// T067: MEF-exported provider that creates QuickInfoSource instances for T-SQL buffers.
    /// </summary>
    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    // SSMS 22 query editor reports content type "SQL"; register all three so quick-info fires in SSMS.
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [ContentType("T-SQL")]
    [Name("AkmlSqlQuickInfoSource")]
    [Order(Before = "default")]
    internal class QuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
    {
        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
        {
            return new QuickInfoSource(textBuffer);
        }
    }

    /// <summary>
    /// Spec 030 T025 / FR-009 — hover tooltips. Sends <see cref="MessageTypes.RequestQuickInfo"/>
    /// to the engine (whose <c>QuickInfoHandler</c> resolves the identifier against the live,
    /// continuously-synced session document) and renders the metadata.
    /// <para>
    /// Implements the async MEF quick-info contract (<see cref="IAsyncQuickInfoSource"/>). SSMS 22's
    /// editor no longer supports the legacy synchronous <c>IQuickInfoSource</c> — it emits a
    /// "uses API that is no longer supported" warning at load — so this awaits the async IPC directly
    /// inside <see cref="GetQuickInfoItemAsync"/>, replacing the earlier cache-and-retrigger bridge
    /// the synchronous contract required.
    /// </para>
    /// </summary>
    internal class QuickInfoSource : IAsyncQuickInfoSource
    {
        private readonly ITextBuffer _buffer;
        private readonly string? _sessionId;
        private bool _disposed;

        // Spec 030 T055 (FR-028) — the AnalysisController for this buffer; its CurrentIssues feeds the
        // Ctrl-hover issue-details popup with no IPC round-trip. Resolved lazily (it may be created
        // after this source) and never subscribed to, so there is no lifecycle to manage.
        private AnalysisController _analysisController;

        public QuickInfoSource(ITextBuffer buffer)
        {
            _buffer = buffer;
            buffer.Properties.TryGetProperty("AkmlSqlSessionId", out _sessionId);
            buffer.Properties.TryGetProperty(typeof(AnalysisController), out _analysisController);
        }

        /// <summary>Current analysis issues for this buffer, resolving the controller lazily (it can be
        /// created after this QuickInfoSource). Empty when analysis is off / no controller exists.</summary>
        private CodeIssueInfo[] GetCurrentIssues()
        {
            var controller = _analysisController;
            if (controller == null && _buffer.Properties.TryGetProperty(typeof(AnalysisController), out controller))
                _analysisController = controller;
            return controller?.CurrentIssues ?? Array.Empty<CodeIssueInfo>();
        }

        /// <summary>
        /// Invoked on the UI thread by the async quick-info broker. The trigger point, modifier state
        /// and word span are captured up front (UI-thread work); the object-metadata path then awaits
        /// the engine off-thread and marshals back before building content.
        /// </summary>
        public async Task<QuickInfoItem?> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
        {
            if (_disposed || string.IsNullOrEmpty(_sessionId))
                return null;

            try
            {
                var snapshot = _buffer.CurrentSnapshot;
                var point = session.GetTriggerPoint(snapshot);
                if (point == null)
                    return null;

                int position = point.Value.Position;

                // Spec 030 T055 (FR-028) — Ctrl held: show the analysis issue's rule description +
                // reference link for the squiggle under the cursor, instead of object metadata. When
                // Ctrl is down we stay in "issue mode": if no issue covers the position, show nothing
                // (rather than falling back to the object-metadata hover). This path is fully
                // synchronous, so it stays on the UI thread where the WPF panel is built.
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                    return TryBuildIssueItem(snapshot, position);

                // Word span under the cursor (also what the tooltip applies to).
                int start = position;
                while (start > 0 && IsIdentifierChar(snapshot[start - 1])) start--;
                int end = position;
                while (end < snapshot.Length && IsIdentifierChar(snapshot[end])) end++;
                if (start == end)
                    return null;

                var applicableToSpan = snapshot.CreateTrackingSpan(start, end - start, SpanTrackingMode.EdgeInclusive);

                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                    return null;

                var response = await client.SendRequestAsync<QuickInfoResponse, QuickInfoRequest>(
                    MessageTypes.RequestQuickInfo,
                    new QuickInfoRequest { SessionId = _sessionId, CursorOffset = position },
                    timeoutMs: 1500).ConfigureAwait(false);

                var text = BuildText(response);
                if (string.IsNullOrEmpty(text))
                    return null;

                return new QuickInfoItem(applicableToSpan, text);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "QuickInfo: augment failed");
                return null;
            }
        }

        private static string? BuildText(QuickInfoResponse? response)
        {
            if (response == null)
                return null;

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(response.Header))
            {
                if (!string.IsNullOrEmpty(response.ObjectType))
                    sb.Append('[').Append(response.ObjectType).Append("] ");
                sb.AppendLine(response.Header);
            }
            if (response.Details != null)
            {
                foreach (var d in response.Details)
                {
                    if (string.IsNullOrEmpty(d?.Label) && string.IsNullOrEmpty(d?.Value)) continue;
                    sb.Append("  ").Append(d!.Label);
                    if (!string.IsNullOrEmpty(d.Value)) sb.Append(": ").Append(d.Value);
                    sb.AppendLine();
                }
            }
            if (!string.IsNullOrEmpty(response.Description))
                sb.AppendLine().Append(response.Description);

            var text = sb.ToString().TrimEnd();
            return text.Length > 0 ? text : null;
        }

        /// <summary>
        /// Spec 030 T055 (FR-028) — builds a <see cref="QuickInfoItem"/> for the analysis issue(s) whose
        /// span covers <paramref name="position"/> (rule id + description + optional reference link).
        /// Returns <c>null</c> when no issue covers the position.
        /// </summary>
        private QuickInfoItem? TryBuildIssueItem(ITextSnapshot snapshot, int position)
        {
            var issues = GetCurrentIssues();
            if (issues.Length == 0)
                return null;

            StackPanel container = null;
            ITrackingSpan applicableToSpan = null;
            foreach (var issue in issues)
            {
                int start = Math.Max(0, Math.Min(issue.StartOffset, snapshot.Length));
                int end   = Math.Max(start, Math.Min(issue.EndOffset, snapshot.Length));
                if (position < start || position > end)
                    continue;

                if (container == null)
                {
                    container = new StackPanel();
                    applicableToSpan = snapshot.CreateTrackingSpan(start, end - start, SpanTrackingMode.EdgeInclusive);
                }
                container.Children.Add(BuildIssuePanel(issue));
            }

            return container == null ? null : new QuickInfoItem(applicableToSpan, container);
        }

        /// <summary>Builds the WPF content for one issue: a UIElement (not a string) so the reference
        /// link is clickable. Colours are inherited from the QuickInfo presenter's theme.</summary>
        private static UIElement BuildIssuePanel(CodeIssueInfo issue)
        {
            var panel = new StackPanel { MaxWidth = 480 };

            panel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(issue.RuleId) ? "Analysis issue" : issue.RuleId,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            });

            // FR-028: the offending rule's description; fall back to the per-finding message when the
            // rule has no catalog description.
            var body = !string.IsNullOrEmpty(issue.Description) ? issue.Description : issue.Message;
            if (!string.IsNullOrEmpty(body))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = body,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            // The specific finding text, when it differs from the rule description.
            if (!string.IsNullOrEmpty(issue.Message) && !string.Equals(issue.Message, body, StringComparison.Ordinal))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = issue.Message,
                    TextWrapping = TextWrapping.Wrap,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            if (IsHttpUrl(issue.ReferenceUrl))
            {
                var link = new Hyperlink(new Run("Learn more")) { NavigateUri = new Uri(issue.ReferenceUrl) };
                link.RequestNavigate += OnReferenceNavigate;
                panel.Children.Add(new TextBlock(link) { Margin = new Thickness(0, 4, 0, 0) });
            }

            return panel;
        }

        private static void OnReferenceNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                // Only ever launch http/https — never a file:// or custom scheme from issue data.
                if (e.Uri != null && IsHttpUrl(e.Uri.AbsoluteUri))
                    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "QuickInfo: failed to open reference URL");
            }
            e.Handled = true;
        }

        private static bool IsHttpUrl(string url)
            => !string.IsNullOrEmpty(url)
               && Uri.TryCreate(url, UriKind.Absolute, out var u)
               && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

        private static bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '#' || c == '@' || c == '.';
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
