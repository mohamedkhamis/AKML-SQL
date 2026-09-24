#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Formatting;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// The SSMS / Visual Studio Format Styles window on SQL Prompt's option model: the engine
    /// serves SQL Prompt's pages and options (<c>"model":"sqlPrompt"</c>), a style loads as its SQL
    /// Prompt document, previews and saves carry that document, and choices show SQL Prompt's
    /// labels while storing Redgate's values.
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class FormatStylesSqlPromptModelTests : AppDataIsolatedTest
    {
        public FormatStylesSqlPromptModelTests() : base("akmlsql-sqlpromptvm-test-") { }

        private const string Schema = "{\"model\":\"sqlPrompt\",\"schemaVersion\":2001," +
            "\"groups\":[" +
            "{\"id\":\"whitespace\",\"displayName\":\"Whitespace\",\"parentId\":\"global\",\"sample\":\"SELECT 1;\"}," +
            "{\"id\":\"lists\",\"displayName\":\"Lists\",\"parentId\":\"global\",\"sample\":\"SELECT a, b FROM t;\"}," +
            "{\"id\":\"dml\",\"displayName\":\"Data (DML)\",\"parentId\":\"statements\",\"sample\":\"UPDATE t SET a = 1;\"}]," +
            "\"settings\":[" +
            "{\"id\":\"sqlPrompt.whitespace.spacesOrTabs\",\"groupId\":\"whitespace\",\"displayName\":\"Indent with\",\"type\":\"Enum\",\"status\":\"Implemented\"," +
            "\"default\":\"spaces\",\"allowedEnumValues\":[\"spaces\",\"tabs\",\"tabsIfPossible\"],\"enumLabels\":[\"Use spaces\",\"Use tabs\",\"Use tabs where possible\"],\"subgroup\":\"Indentation\"}," +
            "{\"id\":\"sqlPrompt.lists.placeCommasBeforeItems\",\"groupId\":\"lists\",\"displayName\":\"Place commas before items\",\"type\":\"Bool\",\"status\":\"Implemented\",\"default\":false}," +
            "{\"id\":\"sqlPrompt.dml.collapseShortStatements\",\"groupId\":\"dml\",\"displayName\":\"Collapse short statements\",\"type\":\"Bool\",\"status\":\"Implemented\",\"default\":false}," +
            "{\"id\":\"sqlPrompt.dml.collapseStatementsShorterThan\",\"groupId\":\"dml\",\"displayName\":\"Collapse statements shorter than\",\"type\":\"Int\",\"status\":\"Implemented\",\"default\":80," +
            "\"min\":1,\"max\":1000,\"note\":\"A note.\",\"enabledWhen\":{\"id\":\"sqlPrompt.dml.collapseShortStatements\",\"value\":true}}" +
            "]}";

        /// <summary>A classic (AKML-model) style as stored…</summary>
        private const string ClassicJson = "{\"metadata\":{\"name\":\"Team\",\"id\":\"id-1\"},\"casing\":{\"reservedKeywords\":\"UPPERCASE\"},\"futureRoot\":1}";

        /// <summary>…and the engine's explicit SQL Prompt reading of it (every option written out).</summary>
        private const string ClassicAsSqlPrompt = "{\"metadata\":{\"id\":\"id-1\",\"name\":\"Team\"},\"whitespace\":{\"spacesOrTabs\":\"tabs\"}," +
            "\"lists\":{\"placeCommasBeforeItems\":false},\"dml\":{\"collapseShortStatements\":true,\"collapseStatementsShorterThan\":160}}";

        private static FormatStylesEditorViewModel Vm(FakeRpcClientAccessor fake) =>
            new FormatStylesEditorViewModel(fake) { MainThreadSwitchOverride = () => Task.CompletedTask };

        private static async Task<(FormatStylesEditorViewModel Vm, FakeRpcClientAccessor Fake)> LoadedAsync(bool isSqlPromptStyle = false)
        {
            var settings = ConfigManager.Load();
            settings.Formatter.ActiveProfile = "Team";
            ConfigManager.Save(settings);

            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.ProfileList, new ProfileListResponse { Profiles = new[] { new ProfileInfo { Name = "Team" } } });
            fake.Respond(MessageTypes.RequestStyleEditorSchema, new StyleEditorSchemaResponse { SchemaVersion = 2001, SchemaJson = Schema });
            fake.Respond(MessageTypes.FormatPreview, new FormatPreviewResponse { FormattedText = "SELECT 1" });
            fake.Respond(MessageTypes.ProfileSave, new ProfileSaveResponse { Success = true });
            fake.Respond(MessageTypes.ProfileGet, new ProfileGetResponse
            {
                Success = true, Name = "Team", ProfileJson = ClassicJson,
                SqlPromptJson = ClassicAsSqlPrompt, IsSqlPromptStyle = isSqlPromptStyle,
            });
            FormatStylesEditorViewModel.SetCachedSchemaForTests(null, null);
            var vm = Vm(fake);
            await vm.LoadAsync();
            if (vm.LoadedProfileName == null) await vm.SelectProfileAsync("Team");
            return (vm, fake);
        }

        [Fact]
        public async Task The_editor_asks_for_sql_prompts_model_and_recognises_it()
        {
            try
            {
                var (vm, fake) = await LoadedAsync();
                var request = fake.Requests.Where(r => r.MessageType == MessageTypes.RequestStyleEditorSchema)
                    .Select(r => r.Payload).OfType<StyleEditorSchemaRequest>().Single();
                Assert.True(request.SqlPromptModel);
                Assert.True(vm.IsSqlPromptModel);
            }
            finally { FormatStylesEditorViewModel.SetCachedSchemaForTests(null, null); }
        }

        [Fact]
        public async Task A_style_loads_as_its_sql_prompt_document()
        {
            try
            {
                var (vm, _) = await LoadedAsync();
                Assert.Equal("tabs", vm.GetWorkingValue("sqlPrompt.whitespace.spacesOrTabs"));
                Assert.Equal(true, vm.GetWorkingValue("sqlPrompt.dml.collapseShortStatements"));
                Assert.Equal(160, vm.GetWorkingValue("sqlPrompt.dml.collapseStatementsShorterThan"));
                // The AKML option groups beside the document are not editor-owned.
                Assert.Null(vm.GetWorkingValue("casing.reservedKeywords"));
                Assert.True(vm.IsSelectedClassic);
                Assert.False(vm.IsDirty);
            }
            finally { FormatStylesEditorViewModel.SetCachedSchemaForTests(null, null); }
        }

        [Fact]
        public async Task Previews_carry_the_edited_sql_prompt_document()
        {
            try
            {
                var (vm, _) = await LoadedAsync();
                vm.SetWorkingValue("sqlPrompt.lists.placeCommasBeforeItems", true);

                var json = JsonDocument.Parse(vm.BuildProfileJson()).RootElement;
                var document = json.GetProperty("sqlPrompt");
                Assert.True(document.GetProperty("lists").GetProperty("placeCommasBeforeItems").GetBoolean());
                Assert.Equal("tabs", document.GetProperty("whitespace").GetProperty("spacesOrTabs").GetString());
            }
            finally { FormatStylesEditorViewModel.SetCachedSchemaForTests(null, null); }
        }

        [Fact]
        public async Task Saving_writes_the_document_and_keeps_everything_else_in_the_file()
        {
            try
            {
                var (vm, fake) = await LoadedAsync();
                vm.SetWorkingValue("sqlPrompt.lists.placeCommasBeforeItems", true);
                // Switching a collapse off must be written out: its threshold alone would read as on.
                vm.SetWorkingValue("sqlPrompt.dml.collapseShortStatements", false);

                Assert.True(await vm.SaveAsync());

                var saved = fake.Requests.Where(r => r.MessageType == MessageTypes.ProfileSave)
                    .Select(r => r.Payload).OfType<ProfileSaveRequest>().Single();
                var root = JsonDocument.Parse(saved.ProfileJson).RootElement;
                var document = root.GetProperty("sqlPrompt");
                Assert.True(document.GetProperty("lists").GetProperty("placeCommasBeforeItems").GetBoolean());
                Assert.False(document.GetProperty("dml").GetProperty("collapseShortStatements").GetBoolean());
                Assert.Equal(160, document.GetProperty("dml").GetProperty("collapseStatementsShorterThan").GetInt32());
                Assert.Equal("Team", root.GetProperty("metadata").GetProperty("name").GetString());
                Assert.Equal("UPPERCASE", root.GetProperty("casing").GetProperty("reservedKeywords").GetString());
                Assert.Equal(1, root.GetProperty("futureRoot").GetInt32());
                Assert.False(vm.IsSelectedClassic);   // saved: it is a SQL Prompt style now
                Assert.False(vm.IsDirty);
            }
            finally { FormatStylesEditorViewModel.SetCachedSchemaForTests(null, null); }
        }

        [Fact]
        public async Task A_sql_prompt_style_is_not_flagged_classic()
        {
            try
            {
                var (vm, _) = await LoadedAsync(isSqlPromptStyle: true);
                Assert.False(vm.IsSelectedClassic);
            }
            finally { FormatStylesEditorViewModel.SetCachedSchemaForTests(null, null); }
        }

        [Fact]
        public async Task Each_page_previews_its_own_sample()
        {
            try
            {
                var (vm, fake) = await LoadedAsync();
                vm.PreviewSourceMode = FormatPreviewSource.PageSample;
                vm.PageSample = "UPDATE t SET a = 1;";

                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < deadline)
                {
                    var last = fake.Requests.ToArray().Where(r => r.MessageType == MessageTypes.FormatPreview)
                        .Select(r => r.Payload).OfType<FormatPreviewRequest>().LastOrDefault();
                    if (last?.SampleText == "UPDATE t SET a = 1;") return;
                    await Task.Delay(20);
                }
                Assert.Fail("The preview was not asked for the page's sample.");
            }
            finally { FormatStylesEditorViewModel.SetCachedSchemaForTests(null, null); }
        }

        [Fact]
        public void The_schema_model_reads_labels_notes_headings_gates_and_samples()
        {
            var model = FormatStylesSchemaModel.Parse(Schema);
            Assert.True(model.IsSqlPrompt);
            Assert.Equal(new[] { "Global", "Statements" }, model.Categories.Select(c => c.DisplayName));

            var whitespace = model.FlatGroups.Single(g => g.Id == "whitespace");
            Assert.Equal("SELECT 1;", whitespace.Sample);
            var tabs = whitespace.Settings.Single();
            Assert.Equal("Indentation", tabs.Subgroup);
            Assert.Equal("Use tabs where possible", tabs.LabelFor("tabsIfPossible"));
            Assert.Equal("tabsIfPossible", tabs.ValueFor("Use tabs where possible"));
            Assert.Equal("unknownValue", tabs.LabelFor("unknownValue"));

            var threshold = model.FlatGroups.Single(g => g.Id == "dml").Settings.Single(s => s.Type == "Int");
            Assert.Equal("A note.", threshold.Note);
            Assert.Equal("sqlPrompt.dml.collapseShortStatements", threshold.EnabledWhenId);
            Assert.Equal(true, threshold.EnabledWhenValue);
        }

        [Fact]
        public void The_akml_schema_is_still_read_as_before()
        {
            var model = FormatStylesSchemaModel.Parse("{\"groups\":[{\"id\":\"casing\",\"displayName\":\"Casing\",\"parentId\":\"global\"}]," +
                "\"settings\":[{\"id\":\"casing.reservedKeywords\",\"groupId\":\"casing\",\"type\":\"Enum\",\"default\":\"UPPERCASE\",\"allowedEnumValues\":[\"UPPERCASE\",\"lowercase\"]}]}");
            Assert.False(model.IsSqlPrompt);
            var setting = model.FlatGroups.Single().Settings.Single();
            Assert.Equal("lowercase", setting.LabelFor("lowercase"));   // no labels: the value is the label
            Assert.Null(setting.EnabledWhenId);
        }
    }
}
