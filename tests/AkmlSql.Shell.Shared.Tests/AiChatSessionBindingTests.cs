#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ai;
using AkmlSql.Shell.Shared.Refactoring;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 036 (US1) T008, rewritten for spec 046 (US1, FR-001 – FR-007): the chat panel reads ONE
    /// binding (<see cref="ChatBinding"/>) for its header, its send gate and its refusal text, so
    /// the header can never name a database while Send refuses — the reported defect. The binding
    /// comes through <see cref="AiChatPanel.TestBindingResolver"/>, so these tests never depend on a
    /// live SSMS host; the decision itself is covered by <see cref="ChatBindingResolverTests"/>.
    ///
    /// <para>Shares the "AkmlSql ThemeRegistry" collection with the other panel-constructing
    /// classes: <see cref="AiChatPanel"/> merges the process-global ThemeRegistry dictionary.</para>
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class AiChatSessionBindingTests : IDisposable
    {
        private const string QueryA = @"C:\Temp\SQLQuery1.sql";
        private const string QueryB = @"C:\Temp\SQLQuery2.sql";

        // Deterministic usable agent (same rationale as AiChatPanelCopyButtonTests: never the
        // machine's real config.json — send must not be gated by an empty/raced config).
        private static readonly AppSettings SeededSettings = Seed();
        private readonly Func<AppSettings>? _priorProvider;
        private readonly Func<ChatBinding?, ChatBinding>? _priorResolver;

        /// <summary>What the fake resolver hands the panel on its next refresh.</summary>
        private ChatBinding _next = ChatBinding.ForNoDocument();

        public AiChatSessionBindingTests()
        {
            _priorProvider = AiChatPanel.TestSettingsProvider;
            _priorResolver = AiChatPanel.TestBindingResolver;
            AiChatPanel.TestSettingsProvider = () => SeededSettings;
            AiChatPanel.TestBindingResolver = _ => _next;
        }

        public void Dispose()
        {
            AiChatPanel.TestSettingsProvider = _priorProvider;
            AiChatPanel.TestBindingResolver = _priorResolver;
        }

        private static AppSettings Seed()
        {
            var settings = new AppSettings();
            settings.Ai.Agents.Add(new AiAgent
            {
                Id = "test-agent-1",
                Name = "TestClaude",
                Provider = "anthropic",
                Model = "claude-sonnet-4-6",
                ApiKey = "sk-test",
                Enabled = true,
            });
            settings.Ai.ActiveAgentId = "test-agent-1";
            return settings;
        }

        private static ChatBinding BoundA() => ChatBinding.ForBound("s1", QueryA, "srv01", "Sales");
        private static ChatBinding BoundB() => ChatBinding.ForBound("s2", QueryB, "srv02", "Inventory");

        private const string EngineDownText = "engine is not connected";

        private static readonly string NotConnectedText =
            "SQLQuery1.sql is not connected to a database (or is still connecting). " +
            "Connect it — Query ▸ Connection ▸ Connect — then ask again.";

        private static readonly string NotQueryWindowText =
            "The active window (ExecutionPlan1.sqlplan) is not a query window, and no connected query " +
            "window has been used yet. Click into a connected query window, then ask again.";

        private static readonly string NoDocumentText =
            "No query window is open. Open a query window, connect it to a database, then ask again.";

        [Fact]
        public void Real_session_resolution_returns_null_outside_a_shell_host()
        {
            // The editor-context AI commands keep this resolver (spec 046 changes only the chat);
            // outside VS/SSMS it must still report "unbound" rather than fabricate an id (R1/R2).
            Assert.Null(RefactorCommandHelper.TryGetActiveRealSessionId());
        }

        [StaFact]
        public void Header_names_the_bound_window_and_its_tooltip_names_the_document()
        {
            _next = BoundA();
            var panel = new AiChatPanel();

            var header = HeaderLabel(panel);
            Assert.Equal("srv01.Sales", header.Text);
            Assert.Equal("SQLQuery1.sql — srv01.Sales", header.ToolTip);
        }

        [StaFact]
        public void Header_says_not_connected_for_every_unbound_state()
        {
            _next = ChatBinding.ForNotConnected("s1", QueryA);
            var panel = new AiChatPanel();
            Assert.Equal(AiChatPanel.NotConnectedHeaderText, HeaderLabel(panel).Text);
            Assert.Equal("SQLQuery1.sql is not connected", HeaderLabel(panel).ToolTip);

            _next = ChatBinding.ForNoDocument();
            panel.RefreshBinding();
            Assert.Equal(AiChatPanel.NotConnectedHeaderText, HeaderLabel(panel).Text);
            Assert.Null(HeaderLabel(panel).ToolTip);
        }

        [StaFact]
        public void A_bound_send_passes_the_binding_gate()
        {
            // Off-host EngineLifecycle.Manager is null, so a send that passes the binding gate
            // surfaces the engine-down error — and never one of the three refusal texts.
            _next = BoundA();
            var panel = new AiChatPanel();

            Send(panel, "which tables are largest?");

            var texts = FindVisibleTexts(panel);
            Assert.Contains(texts, t => t.Contains(EngineDownText));
            Assert.DoesNotContain(texts, t => t == NotConnectedText || t == NotQueryWindowText || t == NoDocumentText);
            Assert.DoesNotContain(texts, t => t.Contains("No database connection"));
        }

        [StaFact]
        public void Not_connected_refuses_with_its_text_and_logs_one_line()
            => AssertRefusal(ChatBinding.ForNotConnected("s1", QueryA), NotConnectedText,
                ChatBindingState.NotConnected, "SQLQuery1.sql", queryWindow: true, session: true);

        [StaFact]
        public void Not_a_query_window_refuses_with_its_text_and_logs_one_line()
            => AssertRefusal(ChatBinding.ForNotQueryWindow(@"C:\Temp\ExecutionPlan1.sqlplan"), NotQueryWindowText,
                ChatBindingState.NotQueryWindow, "ExecutionPlan1.sqlplan", queryWindow: false, session: false);

        [StaFact]
        public void No_document_refuses_with_its_text_and_logs_one_line()
            => AssertRefusal(ChatBinding.ForNoDocument(), NoDocumentText,
                ChatBindingState.NoDocument, "(none)", queryWindow: false, session: false);

        [StaFact]
        public void A_database_change_mid_conversation_adds_one_notice()
        {
            _next = BoundA();
            var panel = new AiChatPanel();
            SeedHistory(panel, ("user", "q"), ("assistant", "a"));

            _next = BoundB();
            panel.RefreshBinding();
            panel.RefreshBinding();   // same binding again — no second notice

            var notices = Notices(panel);
            Assert.Equal(new[] { "Now answering about srv02.Inventory (SQLQuery2.sql)." }, notices);
        }

        [StaFact]
        public void A_transient_not_connected_window_between_two_databases_still_gets_its_notice()
        {
            // A new query window is "not connected" for about a second before SSMS adds the
            // connection to its caption — the notice compares with the LAST BOUND database.
            _next = BoundA();
            var panel = new AiChatPanel();
            SeedHistory(panel, ("user", "q"), ("assistant", "a"));

            _next = ChatBinding.ForNotConnected("s2", QueryB);
            panel.RefreshBinding();
            _next = BoundB();
            panel.RefreshBinding();

            Assert.Equal(new[] { "Now answering about srv02.Inventory (SQLQuery2.sql)." }, Notices(panel));
        }

        [StaFact]
        public void No_notice_before_the_conversation_starts_or_when_only_the_window_changes()
        {
            _next = BoundA();
            var panel = new AiChatPanel();

            _next = BoundB();                                   // empty conversation: header only
            panel.RefreshBinding();
            Assert.Empty(Notices(panel));

            SeedHistory(panel, ("user", "q"), ("assistant", "a"));
            _next = ChatBinding.ForBound("s3", @"C:\Temp\SQLQuery3.sql", "SRV02", "inventory");   // same database
            panel.RefreshBinding();
            Assert.Empty(Notices(panel));
        }

        // ── helpers ────────────────────────────────────────────────────────────

        private void AssertRefusal(ChatBinding binding, string expectedText, ChatBindingState state,
            string document, bool queryWindow, bool session)
        {
            _next = binding;
            var panel = new AiChatPanel();

            var sink = new CollectingSink();
            var priorLogger = Log.Logger;
            Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
            try
            {
                Send(panel, "what tables do I have?");
            }
            finally
            {
                Log.Logger = priorLogger;
            }

            var texts = FindVisibleTexts(panel);
            Assert.Contains(expectedText, texts);
            Assert.DoesNotContain(texts, t => t.Contains(EngineDownText));

            var refusal = Assert.Single(sink.Events, e =>
                e.MessageTemplate.Text.StartsWith("AiChatPanel: refused to send", StringComparison.Ordinal));
            Assert.Equal(LogEventLevel.Information, refusal.Level);
            Assert.Equal(state, Scalar(refusal, "Reason"));
            Assert.Equal(document, Scalar(refusal, "Document"));
            Assert.Equal(queryWindow, Scalar(refusal, "IsQueryWindow"));
            Assert.Equal(session, Scalar(refusal, "HasSession"));
            Assert.Equal(false, Scalar(refusal, "Connected"));

            // FR-005: the line names what was resolved, never where it lives or what was asked.
            var rendered = refusal.RenderMessage();
            Assert.DoesNotContain(@"C:\Temp", rendered);
            Assert.DoesNotContain("what tables", rendered);
        }

        private static object? Scalar(LogEvent e, string name)
            => e.Properties.TryGetValue(name, out var value) && value is ScalarValue scalar ? scalar.Value : null;

        private sealed class CollectingSink : ILogEventSink
        {
            public List<LogEvent> Events { get; } = new List<LogEvent>();
            public void Emit(LogEvent logEvent)
            {
                lock (Events) Events.Add(logEvent);
            }
        }

        private static void Send(AiChatPanel panel, string text)
        {
            FindChatInput(panel).Text = text;
            FindSendButton(panel).RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        }

        private static void SeedHistory(AiChatPanel panel, params (string Role, string Content)[] turns)
        {
            var field = typeof(AiChatPanel).GetField("_history", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            var history = (List<ChatTurnDto>)field!.GetValue(panel)!;
            foreach (var (role, content) in turns)
                history.Add(new ChatTurnDto { Role = role, Content = content });
        }

        private static List<string> Notices(AiChatPanel panel)
            => FindAll<TextBlock>(panel)
                .Where(tb => AutomationProperties.GetName(tb) == AiChatPanel.BindingNoticeAutomationName)
                .Select(tb => tb.Text)
                .ToList();

        private static TextBlock HeaderLabel(AiChatPanel panel)
            => Assert.Single(FindAll<TextBlock>(panel),
                tb => AutomationProperties.GetName(tb) == AiChatPanel.HeaderBindingAutomationName);

        /// <summary>All user-visible text: TextBlocks plus every read-only text host (answers are rich text).</summary>
        private static List<string> FindVisibleTexts(DependencyObject root) => ChatPanelTestText.VisibleTexts(root);

        private static TextBox FindChatInput(DependencyObject root)
        {
            foreach (var tb in FindAll<TextBox>(root))
            {
                if (AutomationProperties.GetName(tb) == "Chat input")
                    return tb;
            }
            throw new InvalidOperationException("Chat input TextBox not found");
        }

        private static Button FindSendButton(DependencyObject root)
        {
            foreach (var b in FindAll<Button>(root))
            {
                if ("Send".Equals(b.Content as string, StringComparison.Ordinal))
                    return b;
            }
            throw new InvalidOperationException("Send button not found");
        }

        private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            var found = new List<T>();
            Walk(root, found);
            return found;
        }

        private static void Walk<T>(DependencyObject node, List<T> found) where T : DependencyObject
        {
            foreach (var child in LogicalTreeHelper.GetChildren(node))
            {
                if (child is T match)
                    found.Add(match);
                if (child is DependencyObject d)
                    Walk(d, found);
            }
        }
    }
}
