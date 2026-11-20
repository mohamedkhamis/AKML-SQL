#nullable enable
using System;
using System.Collections.Generic;
using AkmlSql.Shell.Shared.Ai;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 046 (US1, FR-001 – FR-004, FR-006) — the chat's binding decision, tested off-host.
    /// <see cref="ChatBindingResolver.Decide"/> is the whole rule (contracts/chat-binding.md
    /// Part 1): the chat follows the active DOCUMENT (the query window tab), never the pane inside
    /// it that last had the keyboard, and keeps the last bound window while a non-query document is
    /// active. The reported defect — a refusal after clicking into the Messages pane — cannot recur
    /// through this function: the Messages pane is not a document, so the active document stays the
    /// query window and row 1 binds it.
    /// </summary>
    public class ChatBindingResolverTests
    {
        private const string QueryA = @"C:\Users\me\AppData\Local\Temp\SQLQuery1.sql";
        private const string QueryB = @"C:\Users\me\AppData\Local\Temp\SQLQuery2.sql";
        private const string PlanDoc = @"C:\Users\me\AppData\Local\Temp\ExecutionPlan1.sqlplan";

        private static Func<string, TrackedViewInfo?> Tracked(params (string Path, TrackedViewInfo Info)[] views)
        {
            var map = new Dictionary<string, TrackedViewInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var (path, info) in views) map[path] = info;
            return path => map.TryGetValue(path, out var info) ? info : null;
        }

        private static TrackedViewInfo Connected(string session, string server = "srv01", string database = "Sales")
            => new TrackedViewInfo(session, server, database);

        private static TrackedViewInfo Disconnected(string session) => new TrackedViewInfo(session, null, null);

        // ── Row 1: the active document is a tracked query window with a connection ──────────

        [Fact]
        public void A_connected_active_query_window_is_bound()
        {
            var binding = ChatBindingResolver.Decide(QueryA, Tracked((QueryA, Connected("s1"))), previous: null);

            Assert.Equal(ChatBindingState.Bound, binding.State);
            Assert.Equal("s1", binding.SessionId);
            Assert.Equal("srv01", binding.Server);
            Assert.Equal("Sales", binding.Database);
            Assert.Equal(QueryA, binding.DocumentPath);
            Assert.Equal("SQLQuery1.sql", binding.DocumentName);
            Assert.False(binding.IsSticky);
        }

        [Fact]
        public void Path_matching_ignores_case()
        {
            var binding = ChatBindingResolver.Decide(QueryA.ToUpperInvariant(),
                Tracked((QueryA, Connected("s1"))), previous: null);

            Assert.Equal(ChatBindingState.Bound, binding.State);
        }

        // ── Row 2: tracked, but the caption carries no connection ────────────────────────────

        [Fact]
        public void A_tracked_window_without_a_connection_is_not_connected_and_keeps_its_session()
        {
            var binding = ChatBindingResolver.Decide(QueryA, Tracked((QueryA, Disconnected("s1"))), previous: null);

            Assert.Equal(ChatBindingState.NotConnected, binding.State);
            Assert.Equal("s1", binding.SessionId);
            Assert.Null(binding.Server);
            Assert.Null(binding.Database);
            Assert.Equal("SQLQuery1.sql", binding.DocumentName);
        }

        [Fact]
        public void The_active_window_wins_over_a_sticky_previous_even_when_it_is_disconnected()
        {
            var previous = ChatBindingResolver.Decide(QueryA, Tracked((QueryA, Connected("s1"))), null);

            var binding = ChatBindingResolver.Decide(QueryB,
                Tracked((QueryA, Connected("s1")), (QueryB, Disconnected("s2"))), previous);

            Assert.Equal(ChatBindingState.NotConnected, binding.State);
            Assert.Equal("s2", binding.SessionId);
        }

        // ── Rows 3–4: the active document is not a tracked query window ──────────────────────

        [Fact]
        public void A_non_query_document_keeps_the_previous_bound_window_sticky()
        {
            var views = Tracked((QueryA, Connected("s1")));
            var previous = ChatBindingResolver.Decide(QueryA, views, null);

            var binding = ChatBindingResolver.Decide(PlanDoc, views, previous);

            Assert.Equal(ChatBindingState.Bound, binding.State);
            Assert.True(binding.IsSticky);
            Assert.Equal("s1", binding.SessionId);
            Assert.Equal(QueryA, binding.DocumentPath);
        }

        [Fact]
        public void A_sticky_binding_reads_the_previous_windows_current_database()
        {
            var previous = ChatBindingResolver.Decide(QueryA, Tracked((QueryA, Connected("s1", database: "Sales"))), null);

            var binding = ChatBindingResolver.Decide(PlanDoc,
                Tracked((QueryA, Connected("s1", database: "Inventory"))), previous);

            Assert.True(binding.IsSticky);
            Assert.Equal("Inventory", binding.Database);
        }

        [Fact]
        public void A_non_query_document_with_no_usable_previous_is_not_a_query_window()
        {
            var binding = ChatBindingResolver.Decide(PlanDoc, Tracked(), previous: null);

            Assert.Equal(ChatBindingState.NotQueryWindow, binding.State);
            Assert.Null(binding.SessionId);
            Assert.Equal("ExecutionPlan1.sqlplan", binding.DocumentName);
        }

        [Fact]
        public void A_previous_window_that_closed_is_dropped()
        {
            var previous = ChatBindingResolver.Decide(QueryA, Tracked((QueryA, Connected("s1"))), null);

            var binding = ChatBindingResolver.Decide(PlanDoc, Tracked(), previous);   // QueryA no longer tracked

            Assert.Equal(ChatBindingState.NotQueryWindow, binding.State);
            Assert.Null(binding.SessionId);
        }

        [Fact]
        public void A_previous_window_that_lost_its_connection_is_dropped()
        {
            var previous = ChatBindingResolver.Decide(QueryA, Tracked((QueryA, Connected("s1"))), null);

            var binding = ChatBindingResolver.Decide(PlanDoc, Tracked((QueryA, Disconnected("s1"))), previous);

            Assert.Equal(ChatBindingState.NotQueryWindow, binding.State);
        }

        [Fact]
        public void A_not_connected_previous_is_never_sticky()
        {
            var previous = ChatBindingResolver.Decide(QueryA, Tracked((QueryA, Disconnected("s1"))), null);

            var binding = ChatBindingResolver.Decide(PlanDoc, Tracked((QueryA, Disconnected("s1"))), previous);

            Assert.Equal(ChatBindingState.NotQueryWindow, binding.State);
        }

        // ── Rows 5–6: no active document ─────────────────────────────────────────────────────

        [Fact]
        public void No_document_keeps_a_usable_previous_sticky()
        {
            var views = Tracked((QueryA, Connected("s1")));
            var previous = ChatBindingResolver.Decide(QueryA, views, null);

            var binding = ChatBindingResolver.Decide(null, views, previous);

            Assert.Equal(ChatBindingState.Bound, binding.State);
            Assert.True(binding.IsSticky);
        }

        [Fact]
        public void No_document_and_no_previous_is_no_document()
        {
            var binding = ChatBindingResolver.Decide(null, Tracked(), previous: null);

            Assert.Equal(ChatBindingState.NoDocument, binding.State);
            Assert.Null(binding.SessionId);
            Assert.Null(binding.DocumentName);
            Assert.Null(binding.DocumentPath);
        }

        // ── Derived values ───────────────────────────────────────────────────────────────────

        [Fact]
        public void Binding_key_is_server_dot_database_for_bound_and_empty_otherwise()
        {
            var bound = ChatBindingResolver.Decide(QueryA, Tracked((QueryA, Connected("s1", "SRV01", "Sales"))), null);
            var other = ChatBindingResolver.Decide(QueryB, Tracked((QueryB, Connected("s2", "srv01", "SALES"))), null);
            var disconnected = ChatBindingResolver.Decide(QueryA, Tracked((QueryA, Disconnected("s1"))), null);

            Assert.Equal("SRV01.Sales", bound.BindingKey);
            Assert.True(ChatBinding.SameKey(bound, other), "binding keys compare ordinal-ignore-case");
            Assert.Equal(string.Empty, disconnected.BindingKey);
        }

        [Fact]
        public void Header_text_and_tooltip_follow_the_state()
        {
            var bound = ChatBindingResolver.Decide(QueryA, Tracked((QueryA, Connected("s1"))), null);
            var disconnected = ChatBindingResolver.Decide(QueryA, Tracked((QueryA, Disconnected("s1"))), null);
            var none = ChatBindingResolver.Decide(null, Tracked(), null);

            Assert.Equal("srv01.Sales", bound.HeaderText);
            Assert.Equal("SQLQuery1.sql — srv01.Sales", bound.HeaderToolTip);
            Assert.Equal("Not connected", disconnected.HeaderText);
            Assert.Equal("SQLQuery1.sql is not connected", disconnected.HeaderToolTip);
            Assert.Equal("Not connected", none.HeaderText);
            Assert.Null(none.HeaderToolTip);
        }

        [Fact]
        public void Refusal_texts_are_the_contract_wording()
        {
            var disconnected = ChatBindingResolver.Decide(QueryA, Tracked((QueryA, Disconnected("s1"))), null);
            var notQuery = ChatBindingResolver.Decide(PlanDoc, Tracked(), null);
            var none = ChatBindingResolver.Decide(null, Tracked(), null);

            Assert.Equal(
                "SQLQuery1.sql is not connected to a database (or is still connecting). " +
                "Connect it — Query ▸ Connection ▸ Connect — then ask again.",
                disconnected.RefusalText);
            Assert.Equal(
                "The active window (ExecutionPlan1.sqlplan) is not a query window, and no connected query " +
                "window has been used yet. Click into a connected query window, then ask again.",
                notQuery.RefusalText);
            Assert.Equal(
                "No query window is open. Open a query window, connect it to a database, then ask again.",
                none.RefusalText);
        }

        [Fact]
        public void Only_supplied_session_ids_ever_appear()
        {
            // Spec 036 R1: never a fabricated id. Every row's SessionId is either null or one the
            // tracked-view lookup handed in.
            var supplied = new HashSet<string> { "s1", "s2" };
            var views = Tracked((QueryA, Connected("s1")), (QueryB, Disconnected("s2")));
            var bound = ChatBindingResolver.Decide(QueryA, views, null);
            var results = new[]
            {
                bound,
                ChatBindingResolver.Decide(QueryB, views, bound),
                ChatBindingResolver.Decide(PlanDoc, views, bound),
                ChatBindingResolver.Decide(PlanDoc, Tracked(), null),
                ChatBindingResolver.Decide(null, views, bound),
                ChatBindingResolver.Decide(null, Tracked(), null),
            };

            foreach (var result in results)
            {
                Assert.True(result.SessionId == null || supplied.Contains(result.SessionId),
                    $"unexpected session id '{result.SessionId}' for state {result.State}");
            }
        }
    }
}
