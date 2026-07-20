using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ai;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// The shell's IPC wait for an AI request must exceed the provider timeout the engine
    /// honours (<see cref="AiSettings.Timeout"/>). AiChatPanel hard-coded 30 s while the
    /// engine gave the provider 90 s, so any answer slower than 30 s surfaced as
    /// "Error: A task was canceled" even though the provider was still generating.
    /// </summary>
    public class AiIpcTimeoutsTests
    {
        [Theory]
        [InlineData(90, 120_000)]   // the shipped default config: 90 s provider + 30 s margin
        [InlineData(45, 75_000)]
        [InlineData(300, 330_000)]
        public void Ipc_wait_is_provider_timeout_plus_margin(int providerTimeoutSec, int expectedMs)
        {
            var settings = new AppSettings();
            settings.Ai.Timeout = providerTimeoutSec;

            Assert.Equal(expectedMs, AiIpcTimeouts.ForAiRequestMs(settings));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void Nonsense_provider_timeout_falls_back_to_the_default(int broken)
        {
            var settings = new AppSettings();
            settings.Ai.Timeout = broken;

            Assert.Equal(120_000, AiIpcTimeouts.ForAiRequestMs(settings));
        }

        [Fact]
        public void Null_settings_fall_back_to_the_default()
        {
            Assert.Equal(120_000, AiIpcTimeouts.ForAiRequestMs(null));
        }
    }
}
