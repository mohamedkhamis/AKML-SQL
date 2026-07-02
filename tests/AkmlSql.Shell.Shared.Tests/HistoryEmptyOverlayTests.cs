using AkmlSql.Shell.Shared.History;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// PR #248 review finding #6: the "History unavailable" overlay rendered on top of a
    /// still-populated list (the disconnect path never clears Entries). These tests pin the
    /// extracted pure decision — the overlay must never be drawn over visible rows.
    /// </summary>
    public class HistoryEmptyOverlayTests
    {
        [Fact]
        public void Loading_HidesOverlay()
        {
            Assert.False(HistoryToolWindowControl.ShouldShowEmptyOverlay(
                isLoading: true, isDisconnected: false, entryCount: 0, out _));
        }

        [Fact]
        public void DisconnectedWithEmptyList_ShowsUnavailableMessage()
        {
            Assert.True(HistoryToolWindowControl.ShouldShowEmptyOverlay(
                isLoading: false, isDisconnected: true, entryCount: 0, out var message));
            Assert.Contains("engine", message);
        }

        [Fact]
        public void DisconnectedWithRowsStillLoaded_DoesNotDrawOverThem()
        {
            // Engine dies after a successful load: the stale rows stay readable; no overlay on top.
            Assert.False(HistoryToolWindowControl.ShouldShowEmptyOverlay(
                isLoading: false, isDisconnected: true, entryCount: 42, out _));
        }

        [Fact]
        public void ConnectedEmptyResult_ShowsNoQueriesFound()
        {
            Assert.True(HistoryToolWindowControl.ShouldShowEmptyOverlay(
                isLoading: false, isDisconnected: false, entryCount: 0, out var message));
            Assert.Contains("No queries", message);
        }

        [Fact]
        public void ConnectedWithRows_HidesOverlay()
        {
            Assert.False(HistoryToolWindowControl.ShouldShowEmptyOverlay(
                isLoading: false, isDisconnected: false, entryCount: 3, out _));
        }
    }
}
