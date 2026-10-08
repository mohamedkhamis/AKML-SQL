#nullable enable
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Editor.Completion;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T015, OPT-01) — when typing opens the suggestions box: the trigger delay, the
    /// "Trigger after dot" switch, and Ctrl+Space staying immediate.
    /// </summary>
    public class CompletionTriggerPolicyTests
    {
        private static IntelliSenseSettings Settings(int delay = 100, bool afterDot = true, bool auto = true, bool enabled = true)
            => new IntelliSenseSettings { TriggerDelayMs = delay, AfterDot = afterDot, AutoTrigger = auto, Enabled = enabled };

        [Fact]
        public void Zero_delay_is_immediate()
            => Assert.Equal(TriggerDecision.Immediate, CompletionTriggerPolicy.Decide('a', false, Settings(delay: 0)));

        [Fact]
        public void A_delay_is_honoured()
            => Assert.Equal(TriggerDecision.Delayed(1000), CompletionTriggerPolicy.Decide('a', false, Settings(delay: 1000)));

        [Fact]
        public void Dot_is_ignored_when_trigger_after_dot_is_off()
            => Assert.Equal(TriggerDecision.None, CompletionTriggerPolicy.Decide('.', false, Settings(afterDot: false)));

        [Fact]
        public void Dot_is_immediate_whatever_the_delay()
        {
            // The columns after a dot are what was asked for; the delay is for letters.
            Assert.Equal(TriggerDecision.Immediate, CompletionTriggerPolicy.Decide('.', false, Settings(delay: 1000)));
            Assert.Equal(TriggerDecision.Immediate, CompletionTriggerPolicy.Decide('.', false, Settings(delay: 0)));
        }

        [Fact]
        public void The_default_is_immediate()
            => Assert.Equal(TriggerDecision.Immediate, CompletionTriggerPolicy.Decide('a', false, new IntelliSenseSettings()));

        [Fact]
        public void Ctrl_space_is_always_immediate()
        {
            Assert.Equal(TriggerDecision.Immediate, CompletionTriggerPolicy.Decide(' ', true, Settings(delay: 1000)));
            Assert.Equal(TriggerDecision.Immediate, CompletionTriggerPolicy.Decide('\0', true, Settings(auto: false, afterDot: false)));
        }

        [Theory]
        [InlineData(' ')]
        [InlineData(';')]
        [InlineData(',')]
        public void Non_identifier_characters_do_not_trigger(char typed)
            => Assert.Equal(TriggerDecision.None, CompletionTriggerPolicy.Decide(typed, false, Settings()));

        [Fact]
        public void A_keyword_context_trigger_follows_the_delay()
            => Assert.Equal(TriggerDecision.Delayed(100), CompletionTriggerPolicy.Decide(' ', false, Settings(), contextTrigger: true));

        [Fact]
        public void Nothing_triggers_while_auto_trigger_or_intellisense_is_off()
        {
            Assert.Equal(TriggerDecision.None, CompletionTriggerPolicy.Decide('a', false, Settings(auto: false)));
            Assert.Equal(TriggerDecision.None, CompletionTriggerPolicy.Decide('a', false, Settings(enabled: false)));
        }
    }
}
