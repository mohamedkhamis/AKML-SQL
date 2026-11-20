#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ai;
using Xunit;
using static AkmlSql.Shell.Shared.Tests.ChatPanelTestText;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 046 (US2, FR-023, contracts/chat-rendering.md Part 4): an empty conversation opens the
    /// way Copilot Chat does — the bound database, the agent that answers, and three starter
    /// questions that send on a click. The welcome card replaces spec 037's greeting bubble and
    /// leaves the conversation when the first message arrives.
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class AiChatWelcomeCardTests : IDisposable
    {
        private readonly Func<AppSettings>? _priorProvider;
        private readonly Func<ChatBinding?, ChatBinding>? _priorResolver;
        private ChatBinding _next = ChatBinding.ForBound("s1", @"C:\Temp\SQLQuery1.sql", "srv01", "Sales");

        public AiChatWelcomeCardTests()
        {
            _priorProvider = AiChatPanel.TestSettingsProvider;
            _priorResolver = AiChatPanel.TestBindingResolver;
            var settings = new AppSettings();
            settings.Ai.Agents.Add(new AiAgent
            {
                Id = "a1", Name = "Gemini Flash", Provider = "gemini", Model = "gemini-2.5-flash",
                ApiKey = "k", Enabled = true,
            });
            settings.Ai.ActiveAgentId = "a1";
            AiChatPanel.TestSettingsProvider = () => settings;
            AiChatPanel.TestBindingResolver = _ => _next;
        }

        public void Dispose()
        {
            AiChatPanel.TestSettingsProvider = _priorProvider;
            AiChatPanel.TestBindingResolver = _priorResolver;
        }

        [StaFact]
        public void The_card_names_the_bound_database_or_says_there_is_none()
        {
            var card = new AiChatWelcomeCard();
            Assert.Equal(AiChatWelcomeCard.NotBoundTitle, card.TitleText);

            card.SetBinding(ChatBinding.ForBound("s1", @"C:\Temp\q.sql", "srv01", "Sales"));
            Assert.Equal("Ask about srv01.Sales", card.TitleText);

            card.SetBinding(ChatBinding.ForNotConnected("s1", @"C:\Temp\q.sql"));
            Assert.Equal("Not connected to a database", card.TitleText);
        }

        [StaFact]
        public void The_card_names_the_answering_agent()
        {
            var card = new AiChatWelcomeCard();
            Assert.Equal(string.Empty, card.AgentText);

            card.SetAgent("Gemini Flash · Gemini · gemini-2.5-flash");

            Assert.Equal("Answers come from Gemini Flash · Gemini · gemini-2.5-flash.", card.AgentText);
        }

        [StaFact]
        public void Three_starters_with_their_tooltip_raise_their_question()
        {
            var card = new AiChatWelcomeCard();
            var asked = new List<string>();
            card.StarterRequested += (_, q) => asked.Add(q);

            var starters = FindAll<Button>(card);
            Assert.Equal(AiChatWelcomeCard.StarterQuestions, starters.Select(b => (string)b.Content));
            Assert.All(starters, b => Assert.Equal(AiChatWelcomeCard.StarterToolTip, b.ToolTip));

            Click(starters[1]);

            Assert.Equal(new[] { "Which tables have no primary key?" }, asked);
        }

        [StaFact]
        public void A_busy_card_disables_its_starters_and_says_why()
        {
            var card = new AiChatWelcomeCard { IsBusy = true };

            Assert.All(FindAll<Button>(card), b =>
            {
                Assert.False(b.IsEnabled);
                Assert.Equal("Wait for the answer to finish", b.ToolTip);
            });
        }

        [StaFact]
        public void An_empty_panel_shows_the_card_with_its_database_and_agent()
        {
            var panel = new AiChatPanel();

            var card = Assert.Single(FindAll<AiChatWelcomeCard>(panel));
            Assert.Equal("Ask about srv01.Sales", card.TitleText);
            Assert.Equal("Answers come from Gemini Flash · Gemini · gemini-2.5-flash.", card.AgentText);
        }

        [StaFact]
        public void The_card_follows_the_binding()
        {
            var panel = new AiChatPanel();

            _next = ChatBinding.ForBound("s2", @"C:\Temp\SQLQuery2.sql", "srv02", "Inventory");
            panel.RefreshBinding();

            Assert.Equal("Ask about srv02.Inventory", Assert.Single(FindAll<AiChatWelcomeCard>(panel)).TitleText);
        }

        [StaFact]
        public void A_starter_sends_its_question_and_the_card_leaves()
        {
            var panel = new AiChatPanel();
            var card = Assert.Single(FindAll<AiChatWelcomeCard>(panel));

            Click(FindAll<Button>(card)[0]);

            // Off-host the send stops at the engine-down error — after the user's message is shown.
            Assert.Contains("What are the largest tables in this database?", MessageTexts(panel));
            Assert.Empty(FindAll<AiChatWelcomeCard>(panel));
        }

        [StaFact]
        public void Any_message_replaces_the_card()
        {
            var panel = new AiChatPanel();

            InvokeAddAssistantMessage(panel, "an answer");

            Assert.Empty(FindAll<AiChatWelcomeCard>(panel));
        }
    }
}
