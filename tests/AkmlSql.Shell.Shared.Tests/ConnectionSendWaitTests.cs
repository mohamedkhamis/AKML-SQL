#nullable enable
using System.Threading.Tasks;
using AkmlSql.Shell.Shared.Editor;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// A query tab open when SSMS starts finds its connection before the engine's pipe is up. Its
    /// ConnectionChanged used to be dropped then, so that tab never got a schema: no column
    /// suggestions and no Expand wildcards on Format SQL for as long as it stayed open.
    /// </summary>
    public sealed class ConnectionSendWaitTests
    {
        [Fact]
        public async Task The_connection_is_sent_once_the_engine_connects()
        {
            var checks = 0;
            var sent = 0;

            var ok = await ConnectionWiringHelper.WhenEngineReadyAsync(
                () => ++checks >= 3, () => { sent++; return Task.CompletedTask; }, attempts: 20, delayMs: 1);

            Assert.True(ok);
            Assert.Equal(1, sent);
            Assert.Equal(3, checks);
        }

        [Fact]
        public async Task An_engine_already_connected_gets_it_at_once()
        {
            var sent = 0;

            var ok = await ConnectionWiringHelper.WhenEngineReadyAsync(
                () => true, () => { sent++; return Task.CompletedTask; }, attempts: 20, delayMs: 10_000);

            Assert.True(ok);
            Assert.Equal(1, sent);
        }

        [Fact]
        public async Task An_engine_that_never_connects_is_given_up_on_without_sending()
        {
            var sent = 0;

            var ok = await ConnectionWiringHelper.WhenEngineReadyAsync(
                () => false, () => { sent++; return Task.CompletedTask; }, attempts: 3, delayMs: 1);

            Assert.False(ok);
            Assert.Equal(0, sent);
        }
    }
}
