#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using AkmlSql.Core.Models.Tabs;
using AkmlSql.Shell.Shared.History;
using AkmlSql.Shell.Shared.Tabs;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (HIS-14) — SSMS raises DocumentClosing up to three times for one tab close: first
    /// before the "save changes?" prompt (which the user can Cancel), then twice as the tab goes;
    /// closes of several tabs interleave. SQL History acts once the tab has gone (PendingCloses);
    /// Reopen Closed Tab keeps one entry per close (ClosedTabStack).
    /// </summary>
    public sealed class RepeatedCloseFilterTests
    {
        private const string Tab = @"C:\Users\me\AppData\Local\Temp\SQLQuery5.sql";
        private const string Other = @"C:\Users\me\AppData\Local\Temp\SQLQuery6.sql";
        private static readonly DateTime T0 = new DateTime(2026, 10, 4, 9, 32, 19, DateTimeKind.Utc);

        // ── SQL History: PendingCloses ─────────────────────────────────────────────────────

        [Fact]
        public void A_close_is_acted_on_once_its_tab_has_gone_with_the_latest_text()
        {
            var closes = new PendingCloses();
            Assert.True(closes.Begin(Tab, T0, out var close));
            close.Content = "SELECT 1";
            close.SessionKey = "tab-1";

            Assert.Empty(closes.TakeFinished(_ => true, T0.AddSeconds(1)));   // the prompt is up

            Assert.False(closes.Begin(Tab, T0.AddSeconds(2), out var again));  // reported again
            again.Content = "SELECT 1 -- edited";

            var done = Assert.Single(closes.TakeFinished(_ => false, T0.AddSeconds(3)));
            Assert.Equal("SELECT 1 -- edited", done.Content);
            Assert.Equal("tab-1", done.SessionKey);
            Assert.False(closes.Any);
        }

        [Fact]
        public void A_close_cancelled_at_the_prompt_is_never_acted_on()
        {
            // The tab stays open: it used to be marked closed and get a draft anyway.
            var closes = new PendingCloses();
            closes.Begin(Tab, T0, out _);

            Assert.True(closes.Cancel(Tab));   // it ran afterwards

            Assert.Empty(closes.TakeFinished(_ => false, T0.AddMinutes(1)));
        }

        [Fact]
        public void A_cancelled_close_with_no_sign_of_it_is_dropped_after_a_while()
        {
            var closes = new PendingCloses();
            closes.Begin(Tab, T0, out _);

            Assert.Empty(closes.TakeFinished(_ => true, T0 + PendingCloses.GiveUpAfter));
            Assert.False(closes.Any);
            Assert.True(closes.Begin(Tab, T0 + PendingCloses.GiveUpAfter, out _));   // a later close starts afresh
        }

        [Fact]
        public void Interleaved_closes_of_several_tabs_are_each_acted_on_once()
        {
            var closes = new PendingCloses();
            Assert.True(closes.Begin(Tab, T0, out _));
            Assert.True(closes.Begin(Other, T0, out _));
            Assert.False(closes.Begin(Tab, T0.AddSeconds(1), out _));
            Assert.False(closes.Begin(Other, T0.AddSeconds(1), out _));

            var done = closes.TakeFinished(_ => false, T0.AddSeconds(2));

            Assert.Equal(new[] { Tab, Other }, done.Select(c => c.Name).OrderBy(n => n));
        }

        [Fact]
        public void A_tab_closed_again_after_its_close_finished_is_a_new_close()
        {
            var closes = new PendingCloses();
            closes.Begin(Tab, T0, out _);
            closes.TakeFinished(_ => false, T0.AddSeconds(1));

            Assert.True(closes.Begin(Tab, T0.AddSeconds(30), out _));
        }

        [Fact]
        public void A_close_acted_on_at_once_still_recognises_its_repeats()
        {
            // Shutdown acts at once; the repeats that follow must not act again.
            var closes = new PendingCloses();
            closes.Begin(Tab, T0, out var close);
            close.Done = true;

            Assert.False(closes.Begin(Tab, T0.AddSeconds(1), out _));
            Assert.Empty(closes.TakeFinished(_ => false, T0.AddSeconds(2)));
        }

        // ── Reopen Closed Tab: ClosedTabStack ────────────────────────────────────────────────

        [Fact]
        public void A_tab_closed_three_times_by_ssms_is_one_reopenable_entry()
        {
            var stack = new ClosedTabStack();

            stack.Push(Entry(Tab, "SELECT 1", T0));
            stack.Push(Entry(Tab, "SELECT 1", T0.AddSeconds(0.6)));
            stack.Push(Entry(Tab, "SELECT 1", T0.AddSeconds(0.6)));

            Assert.Equal(1, stack.Count);
        }

        [Fact]
        public void Interleaved_closes_of_two_tabs_are_two_entries()
        {
            var stack = new ClosedTabStack();

            stack.Push(Entry(Tab, "SELECT 1", T0));
            stack.Push(Entry(Other, "SELECT 2", T0));
            stack.Push(Entry(Tab, "SELECT 1", T0.AddSeconds(1)));
            stack.Push(Entry(Other, "SELECT 2", T0.AddSeconds(1)));

            Assert.Equal(2, stack.Count);
        }

        [Fact]
        public void A_tab_reopened_and_closed_again_goes_back_on_the_stack()
        {
            var stack = new ClosedTabStack();
            stack.Push(Entry(Tab, "SELECT 1", T0));
            stack.Pop();

            stack.Push(Entry(Tab, "SELECT 1", T0.AddSeconds(5)));

            Assert.Equal(1, stack.Count);
        }

        [Fact]
        public void Other_text_or_a_later_close_is_a_new_entry()
        {
            Assert.False(RepeatedCloseFilter.IsSameClose(Tab, "SELECT 1", T0, Tab, "SELECT 2", T0.AddSeconds(1)));
            Assert.False(RepeatedCloseFilter.IsSameClose(Tab, "SELECT 1", T0, Other, "SELECT 1", T0.AddSeconds(1)));
            Assert.False(RepeatedCloseFilter.IsSameClose(Tab, "SELECT 1", T0, Tab, "SELECT 1", T0 + RepeatedCloseFilter.Window));
            Assert.True(RepeatedCloseFilter.IsSameClose(Tab, "SELECT 1", T0, Tab, "SELECT 1", T0.AddSeconds(4)));
        }

        private static ClosedTabEntry Entry(string path, string text, DateTime at) =>
            new ClosedTabEntry(text, path, null, null, null, at, System.IO.Path.GetFileName(path));
    }
}
