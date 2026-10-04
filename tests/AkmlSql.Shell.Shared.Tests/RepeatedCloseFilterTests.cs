#nullable enable
using System;
using AkmlSql.Core.Models.Tabs;
using AkmlSql.Shell.Shared.Tabs;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (HIS-14) — SSMS raises DocumentClosing up to three times for one tab close (before
    /// the save prompt, then twice as the tab goes away). Only the first counts: the others used to
    /// add two "Not executed" History rows for a query that had run, and two extra Reopen Closed Tab
    /// entries.
    /// </summary>
    public sealed class RepeatedCloseFilterTests
    {
        private const string Tab = @"C:\Users\me\AppData\Local\Temp\SQLQuery5.sql";
        private static readonly DateTime T0 = new DateTime(2026, 10, 4, 9, 32, 19, DateTimeKind.Utc);

        [Fact]
        public void The_same_close_reported_again_is_a_repeat()
        {
            var filter = new RepeatedCloseFilter();

            Assert.False(filter.IsRepeat(Tab, "SELECT 1", T0));
            Assert.True(filter.IsRepeat(Tab, "SELECT 1", T0.AddSeconds(0.6)));
            Assert.True(filter.IsRepeat(Tab, "SELECT 1", T0.AddSeconds(4)));
        }

        [Fact]
        public void Another_tab_or_other_text_is_a_new_close()
        {
            var filter = new RepeatedCloseFilter();
            filter.IsRepeat(Tab, "SELECT 1", T0);

            Assert.False(filter.IsRepeat(@"C:\Users\me\AppData\Local\Temp\SQLQuery6.sql", "SELECT 1", T0.AddSeconds(1)));
            Assert.False(filter.IsRepeat(@"C:\Users\me\AppData\Local\Temp\SQLQuery6.sql", "SELECT 2", T0.AddSeconds(2)));
        }

        [Fact]
        public void The_same_text_closed_again_after_the_window_is_a_new_close()
        {
            var filter = new RepeatedCloseFilter();
            filter.IsRepeat(Tab, "SELECT 1", T0);

            Assert.False(filter.IsRepeat(Tab, "SELECT 1", T0 + RepeatedCloseFilter.Window));
        }

        [Fact]
        public void A_tab_closed_three_times_by_ssms_is_one_reopenable_entry()
        {
            var stack = new ClosedTabStack();

            stack.Push(Entry("SELECT 1", T0));
            stack.Push(Entry("SELECT 1", T0.AddSeconds(0.6)));
            stack.Push(Entry("SELECT 1", T0.AddSeconds(0.6)));

            Assert.Equal(1, stack.Count);
        }

        [Fact]
        public void A_tab_reopened_and_closed_again_goes_back_on_the_stack()
        {
            var stack = new ClosedTabStack();
            stack.Push(Entry("SELECT 1", T0));
            stack.Pop();

            stack.Push(Entry("SELECT 1", T0.AddSeconds(5)));

            Assert.Equal(1, stack.Count);
        }

        private static ClosedTabEntry Entry(string text, DateTime at) =>
            new ClosedTabEntry(text, Tab, null, null, null, at, "SQLQuery5.sql");
    }
}
