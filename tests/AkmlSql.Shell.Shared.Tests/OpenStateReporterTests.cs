#nullable enable
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.History;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T060, HIS-02, FR-011) — when a query is marked open or closed: after it runs,
    /// when its tab gains focus, and when its tab closes (but not while SSMS shuts down, so the
    /// queries open at exit can be restored).
    /// </summary>
    public class OpenStateReporterTests
    {
        [Fact]
        public void Recorded_opens_the_session_for_this_shell()
        {
            var request = OpenStateReporter.OnRecorded("key-A", 4242);

            Assert.Equal(HistoryActions.SetOpenStatus, request.Action);
            Assert.True(request.IsOpen);
            Assert.Equal("key-A", request.SessionKey);
            Assert.Equal(4242, request.OwnerPid);
        }

        [Fact]
        public void Activated_opens_the_session_or_does_nothing_without_a_key()
        {
            var request = OpenStateReporter.OnActivated("key-A", 4242);
            Assert.NotNull(request);
            Assert.True(request!.IsOpen);
            Assert.Equal("key-A", request.SessionKey);
            Assert.Equal(4242, request.OwnerPid);

            Assert.Null(OpenStateReporter.OnActivated(null, 4242));
        }

        [Fact]
        public void Closing_closes_the_session_unless_there_is_no_key_or_ssms_is_shutting_down()
        {
            var request = OpenStateReporter.OnClosing("key-A", 4242, shuttingDown: false);
            Assert.NotNull(request);
            Assert.False(request!.IsOpen);
            Assert.Equal("key-A", request.SessionKey);
            Assert.Equal(4242, request.OwnerPid);

            Assert.Null(OpenStateReporter.OnClosing(null, 4242, shuttingDown: false));
            Assert.Null(OpenStateReporter.OnClosing("key-A", 4242, shuttingDown: true));
        }
    }
}
