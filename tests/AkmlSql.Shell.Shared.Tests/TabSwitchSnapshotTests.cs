#nullable enable
using AkmlSql.Shell.Shared.History;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (HIS-05) — switching away from a query tab snapshots its edited text so History shows
    /// and finds it. The check used to compare the tab with the last active one, which every run and
    /// switch sets to that very tab, so the usual run → edit → switch never snapshotted.
    /// </summary>
    public sealed class TabSwitchSnapshotTests
    {
        private const string A = @"C:\Users\me\AppData\Local\Temp\SQLQuery3.sql";
        private const string B = @"C:\Work\Northwind checks.sql";

        [Theory]
        [InlineData(A, B)]       // to another tab
        [InlineData(A, null)]    // to SQL History or another tool window
        [InlineData(B, A)]       // and back
        public void Leaving_a_tab_is_a_switch(string lost, string? gained)
            => Assert.True(ExecutionCapture.IsSwitchAway(lost, gained));

        [Theory]
        [InlineData(A, A)]
        [InlineData(A, @"c:\users\ME\appdata\local\temp\sqlquery3.SQL")]
        [InlineData(null, B)]
        [InlineData("", B)]
        public void Focus_staying_in_the_same_document_is_not(string? lost, string? gained)
            => Assert.False(ExecutionCapture.IsSwitchAway(lost, gained));
    }
}
