using System;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ai;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// A timed-out AI IPC request must not surface as the bare "A task was canceled" — the
    /// user cannot act on that. It should say it timed out, for how long it waited, and where
    /// to look. Provider errors (quota, key, model) keep their original message.
    /// </summary>
    public class AiFailureMessageTests
    {
        [Fact]
        public void Timeout_cancellation_is_described_with_the_wait_and_a_pointer()
        {
            var settings = new AppSettings();
            settings.Ai.Timeout = 90;

            var msg = AiIpcTimeouts.DescribeFailure(new TaskCanceledException("A task was canceled."), settings);

            Assert.Contains("timed out", msg);
            Assert.Contains("120", msg);           // 90 s provider + 30 s margin
            Assert.DoesNotContain("A task was canceled", msg);
        }

        [Fact]
        public void Provider_errors_keep_their_original_message()
        {
            var ex = new InvalidOperationException("You exceeded your current quota, please check your plan and billing details.");

            Assert.Equal(ex.Message, AiIpcTimeouts.DescribeFailure(ex, new AppSettings()));
        }
    }
}
