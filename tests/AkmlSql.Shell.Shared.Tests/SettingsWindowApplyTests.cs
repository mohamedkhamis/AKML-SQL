#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Shell.Shared.Commands;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using Xunit;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 037 review finding — the settings dialog's Apply must behave as OK without the
    /// close: the same validate-first gate (FR-032 — a refusal selects the offending agent and
    /// saves nothing) and the same post-save notification (the
    /// <see cref="MessageTypes.AnalysisSettingsChanged"/> push that makes the engine drop its
    /// settings cache; skipping it leaves the engine serving stale settings after an Apply).
    ///
    /// <para>Apply writes through <c>ConfigManager.Save</c>, so the class redirects
    /// <c>AKML_APP_DATA_ROOT</c> (<see cref="AppDataIsolatedTest"/>); the notification is
    /// captured through <see cref="OptionsCommand.TestRpcAccessor"/> with a
    /// <see cref="FakeRpcClientAccessor"/>. The refusal messagebox is replaced by
    /// <see cref="SettingsWindow.ValidationRefusalReporter"/> so the test never meets a modal.
    /// </para>
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public class SettingsWindowApplyTests : AppDataIsolatedTest
    {
        public SettingsWindowApplyTests() : base("akml-apply-tests-") { }

        [StaFact]
        public void Apply_with_a_duplicate_agent_name_refuses_and_selects_the_offender()
        {
            var kimi = MakeAgent("Kimi");
            var claude = MakeAgent("Claude (work)");
            var settings = SettingsWith(kimi, claude);
            var (dialog, controls) = BuildPage(settings);
            string? refusal = null;
            dialog.ValidationRefusalReporter = m => refusal = m;
            var fake = new FakeRpcClientAccessor();
            var prior = OptionsCommand.TestRpcAccessor;
            OptionsCommand.TestRpcAccessor = fake;
            try
            {
                NameBox(controls).Text = "Claude (work)";   // renaming Kimi onto Claude
                InvokeApply(dialog);

                Assert.NotNull(refusal);
                Assert.Contains("Another agent is already named", refusal);
                // The offending agent is selected before the message is shown (FR-032)…
                Assert.Equal(kimi.Id, controls.SelectedAgentId);
                // …and nothing was saved: the settings object is untouched and the
                // save-and-notify path never ran.
                Assert.Equal("Kimi", settings.Ai.Agents[0].Name);
                Assert.Empty(fake.Notifications);
            }
            finally
            {
                OptionsCommand.TestRpcAccessor = prior;
            }
        }

        [StaFact]
        public void Apply_saves_and_raises_the_settings_changed_notification()
        {
            var (dialog, _) = BuildPage(SettingsWith(MakeAgent("A")));
            var fake = new FakeRpcClientAccessor();
            var prior = OptionsCommand.TestRpcAccessor;
            OptionsCommand.TestRpcAccessor = fake;
            // Where this test's AppData redirect points — computed up front so the assertion
            // does not re-read the (process-global) env var after the fact.
            var configPath = Path.Combine(TempRoot, Constants.AppDataFolderName, Constants.ConfigFileName);
            try
            {
                InvokeApply(dialog);

                // The notification OK's path sends — the FR-019 "no restart" mechanism.
                var notification = Assert.Single(fake.Notifications);
                Assert.Equal(MessageTypes.AnalysisSettingsChanged, notification.MessageType);

                // …and the save really reached disk (the shared save-and-notify path).
                Assert.True(File.Exists(configPath));
                Assert.Equal("A", ConfigManager.Load(configPath).Ai.Agents[0].Name);
            }
            finally
            {
                OptionsCommand.TestRpcAccessor = prior;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static AiAgent MakeAgent(string name)
        {
            return new AiAgent
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Provider = "anthropic",
                Model = "claude-sonnet-4-6",
                ApiKey = ApiKeyProtector.Protect("sk-test"),
                Enabled = true,
                CreatedUtc = DateTime.UtcNow.ToString("O"),
            };
        }

        private static AppSettings SettingsWith(params AiAgent[] agents)
        {
            var settings = new AppSettings();
            foreach (var agent in agents) settings.Ai.Agents.Add(agent);
            settings.Ai.ActiveAgentId = agents.Length > 0 ? agents[0].Id : string.Empty;
            return settings;
        }

        private static (SettingsWindow Dialog, AiAssistanceControls Controls) BuildPage(AppSettings settings)
        {
            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();
            var f = typeof(SettingsWindow).GetField("_pageControlsByKey",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(f);
            var pages = (Dictionary<string, IPageControls>)f!.GetValue(dialog)!;
            Assert.True(pages.TryGetValue("AI Assistance", out var controls), "AI Assistance controls not found.");
            return (dialog, (AiAssistanceControls)controls!);
        }

        /// <summary>OnApplyClick is private and wired to no button in the test-built window;
        /// invoke the handler directly (its sender/args are unused).</summary>
        private static void InvokeApply(SettingsWindow dialog)
        {
            var m = typeof(SettingsWindow).GetMethod("OnApplyClick",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(m);
            m!.Invoke(dialog, new object?[] { null, null });
        }

        private static TextBox NameBox(AiAssistanceControls controls) => GetField<TextBox>(controls, "_name");

        private static T GetField<T>(object instance, string name) where T : class
        {
            var f = instance.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(f != null, $"field {name} not found on {instance.GetType().Name}");
            var value = f!.GetValue(instance) as T;
            Assert.NotNull(value);
            return value!;
        }
    }
}
