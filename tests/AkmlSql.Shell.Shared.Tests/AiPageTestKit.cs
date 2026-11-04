#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using AkmlSql.Shell.Shared.Ui.Theme;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Shared helpers for the AI assistance page tests (2026-11 master–detail layout): build the
    /// Options window without showing it, reach the page's controls, drive the agent dialog
    /// through <see cref="AiAssistanceControls.EditorHost"/> instead of a modal, and build an
    /// <see cref="AiAgentEditor"/> on its own for the editor's async test flow.
    /// </summary>
    internal static class AiPageTestKit
    {
        internal static AiAgent MakeAgent(string name, string provider = "anthropic",
            string model = "claude-sonnet-4-6", string apiKey = "sk-test", string endpoint = "")
        {
            return new AiAgent
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Provider = provider,
                Model = model,
                ApiKey = apiKey.Length == 0 ? string.Empty : ApiKeyProtector.Protect(apiKey),
                Endpoint = endpoint,
                Enabled = true,
                CreatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            };
        }

        internal static AppSettings SettingsWith(params AiAgent[] agents)
        {
            var settings = new AppSettings();
            foreach (var agent in agents) settings.Ai.Agents.Add(agent);
            settings.Ai.ActiveAgentId = agents.Length > 0 ? agents[0].Id : string.Empty;
            return settings;
        }

        internal static (SettingsWindow Dialog, AiAssistanceControls Controls) BuildPage(AppSettings settings, string? initialPageKey = null)
        {
            var dialog = new SettingsWindow(settings);
            _ = initialPageKey == null
                ? dialog.TestBuildWindowForRenderTest()
                : dialog.TestBuildWindowForRenderTest(initialPageKey);
            var f = typeof(SettingsWindow).GetField("_pageControlsByKey", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(f);
            var pages = (Dictionary<string, IPageControls>)f!.GetValue(dialog)!;
            Assert.True(pages.TryGetValue("AI Assistance", out var controls), "AI Assistance controls not found.");
            return (dialog, (AiAssistanceControls)controls!);
        }

        internal static void Select(AiAssistanceControls controls, int index)
        {
            var list = controls.ListView.List;
            Assert.True(index < list.Items.Count, $"agent list holds {list.Items.Count} rows; cannot select index {index}");
            list.SelectedIndex = index;
        }

        internal static void Click(Button button)
            => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        /// <summary>
        /// The next dialog the page opens is "filled" by <paramref name="fill"/> and then OK'd
        /// the way the real dialog does it — through <see cref="AiAgentEditor.TryAccept"/>, so a
        /// refused draft keeps the dialog open (the host returns false). The editor it saw is
        /// handed back through <paramref name="seen"/>.
        /// </summary>
        internal static void AcceptNextEditor(AiAssistanceControls controls, Action<AiAgentEditor> fill, Action<AiAgentEditor>? seen = null)
        {
            controls.EditorHost = editor =>
            {
                fill(editor);
                seen?.Invoke(editor);
                return editor.TryAccept() == null;
            };
        }

        /// <summary>The next dialog is filled by <paramref name="fill"/> and then cancelled.</summary>
        internal static void CancelNextEditor(AiAssistanceControls controls, Action<AiAgentEditor>? fill = null)
        {
            controls.EditorHost = editor =>
            {
                fill?.Invoke(editor);
                return false;
            };
        }

        /// <summary>Fills a fresh Add… draft with a usable agent.</summary>
        internal static void FillUsable(AiAgentEditor editor, string? name = null, int providerIndex = 1,
            string model = "claude-sonnet-4-6", string key = "sk-new")
        {
            if (name != null) editor.NameBox.Text = name;
            editor.ProviderCombo.SelectedIndex = providerIndex;   // 1 = Anthropic
            editor.ModelBox.Text = model;
            editor.KeyBox.Text = key;
        }

        /// <summary>An editor on its own, over a copy of <paramref name="agent"/> (for the async test flow).</summary>
        internal static AiAgentEditor EditorFor(AiAgent agent, params AiAgent[] siblings)
        {
            var draft = new AiAgent
            {
                Id = agent.Id,
                Name = agent.Name,
                Provider = agent.Provider,
                Model = agent.Model,
                ApiKey = agent.ApiKey,
                Endpoint = agent.Endpoint,
                Enabled = agent.Enabled,
                CreatedUtc = agent.CreatedUtc,
                KeyDecryptFailed = agent.KeyDecryptFailed,
                Health = agent.Health == null ? null : new AgentHealth
                {
                    Status = agent.Health.Status,
                    CheckedUtc = agent.Health.CheckedUtc,
                    LatencyMs = agent.Health.LatencyMs,
                    Message = agent.Health.Message,
                },
            };
            return new AiAgentEditor(PageTheme.Light, draft, siblings, isNew: false, () => 30);
        }

        /// <summary>The visible text runs of one rendered agent row (marker, name, provider, model, badge).</summary>
        internal static List<string> RowTexts(ListBox list, int index)
        {
            var item = (ListBoxItem)list.Items[index];
            return LogicalTree.Descendants<TextBlock>((DependencyObject)item.Content).Select(b => b.Text).ToList();
        }

        internal static T GetField<T>(object instance, string name) where T : class
        {
            var f = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(f != null, $"field {name} not found on {instance.GetType().Name}");
            var value = f!.GetValue(instance) as T;
            Assert.True(value != null, $"field {name} is null or not a {typeof(T).Name}");
            return value!;
        }
    }
}
