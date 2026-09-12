#nullable enable
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ai;
using AkmlSql.Shell.Shared.Commands;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 037 (US1, FR-022) — the guards <see cref="AiNoAgentGate"/> asks BEFORE its CanAnswer
    /// sweep. The gate replaced the per-command configuration check on the five deliberately
    /// invoked AI surfaces (Explain, Fix, Optimize, Index Analysis, Text-to-SQL) but initially
    /// only asked "can any agent answer?" — dropping the master AI toggle and the privacy-disabled
    /// check. With a perfectly usable agent but AI switched off, a keyboard shortcut therefore
    /// sailed past the gate, opened the prompt dialog, and reached the engine, which answers with a
    /// bare "AI assistance is disabled" that <c>AiIpcTimeouts.IsConfigurationCaused</c> classifies
    /// as non-actionable — no explanation and no route to the setting that caused it.
    ///
    /// <para><see cref="AiNoAgentGate.ShouldStopAsync"/> itself switches to the main thread and
    /// shows a modal MessageBox, so it is not directly drivable in a test run. What IS testable —
    /// and what the fix actually turns on — is the decision layer: the two predicates the gate now
    /// consults, shared with <see cref="AiCommandVisibility"/> so the menu and the gate cannot
    /// drift apart, plus the wordings that must tell the user WHICH setting stopped them.</para>
    /// </summary>
    public class AiNoAgentGateTests
    {
        // ── The master AI toggle ─────────────────────────────────────────────

        [Fact]
        public void Master_toggle_off_is_not_enabled()
        {
            var settings = new AppSettings();
            settings.Ai.Enabled = false;

            Assert.False(AiCommandVisibility.IsAiEnabled(settings));
        }

        [Fact]
        public void Master_toggle_on_is_enabled()
        {
            var settings = new AppSettings();
            settings.Ai.Enabled = true;

            Assert.True(AiCommandVisibility.IsAiEnabled(settings));
        }

        // ── Privacy mode ─────────────────────────────────────────────────────

        [Theory]
        [InlineData("disabled")]
        [InlineData("Disabled")]
        [InlineData("DISABLED")]
        public void Privacy_disabled_blocks_regardless_of_case(string mode)
        {
            // Case-insensitive on purpose: the engine's privacy transformer lower-cases the
            // trimmed mode, so a hand-edited config.json must gate identically on both sides.
            var settings = new AppSettings();
            settings.Ai.Enabled = true;
            settings.Ai.PrivacyMode = mode;

            Assert.False(AiCommandVisibility.IsPrivacyPermitting(settings));
        }

        [Theory]
        [InlineData("schemaOnly")]
        [InlineData("fullyLocal")]
        [InlineData("full")]
        public void Other_privacy_modes_permit(string mode)
        {
            var settings = new AppSettings();
            settings.Ai.Enabled = true;
            settings.Ai.PrivacyMode = mode;

            Assert.True(AiCommandVisibility.IsPrivacyPermitting(settings));
        }

        [Fact]
        public void Default_settings_permit_privacy_but_leave_ai_off()
        {
            // The shipped default: privacy is permissive, the master switch is opt-in. A gate that
            // only checked privacy would let the default configuration through.
            var settings = new AppSettings();

            Assert.True(AiCommandVisibility.IsPrivacyPermitting(settings));
            Assert.False(AiCommandVisibility.IsAiEnabled(settings));
        }

        // ── Null safety: a failed/empty settings read must not throw ─────────

        [Fact]
        public void Null_settings_are_neither_enabled_nor_permitting()
        {
            Assert.False(AiCommandVisibility.IsAiEnabled(null));
            Assert.False(AiCommandVisibility.IsPrivacyPermitting(null));
        }

        // ── Wording ──────────────────────────────────────────────────────────

        /// <summary>
        /// The whole point of the guards is that the user learns WHICH setting stopped them. If
        /// these ever collapsed onto the FR-021 no-agent wordings the gate would send someone to
        /// add an agent they already have.
        /// </summary>
        [Fact]
        public void Gate_wordings_name_the_setting_and_differ_from_the_no_agent_reasons()
        {
            Assert.NotEqual(AiChatEmptyState.NoAgentsText, AiNoAgentGate.AiTurnedOffText);
            Assert.NotEqual(AiChatEmptyState.AllDisabledText, AiNoAgentGate.AiTurnedOffText);
            Assert.NotEqual(AiChatEmptyState.NoAgentsText, AiNoAgentGate.PrivacyDisabledText);
            Assert.NotEqual(AiChatEmptyState.AllDisabledText, AiNoAgentGate.PrivacyDisabledText);
            Assert.NotEqual(AiNoAgentGate.AiTurnedOffText, AiNoAgentGate.PrivacyDisabledText);

            // AllDisabledText is a PER-AGENT state ("every agent is turned off"); the master
            // switch is a different fault and must not borrow its wording.
            Assert.Contains("turned off", AiNoAgentGate.AiTurnedOffText);
            Assert.Contains("privacy", AiNoAgentGate.PrivacyDisabledText, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
