#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Formatting;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T076, STY-02, FR-021) — the live preview expands tabs at the selected style's tab
    /// width: SQL Prompt's "Number of spaces in tabs", or the AKML model's tab size.
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class FormatStylesPreviewTabTests : AppDataIsolatedTest
    {
        public FormatStylesPreviewTabTests() : base("akmlsql-previewtab-test-") { }

        private const string SqlPromptSchema = "{\"model\":\"sqlPrompt\",\"schemaVersion\":2001," +
            "\"groups\":[{\"id\":\"whitespace\",\"displayName\":\"Whitespace\",\"parentId\":\"global\",\"sample\":\"SELECT 1;\"}]," +
            "\"settings\":[{\"id\":\"sqlPrompt.whitespace.numberOfSpacesInTabs\",\"groupId\":\"whitespace\",\"displayName\":\"Number of spaces in tabs\",\"type\":\"Int\",\"status\":\"Implemented\",\"default\":4,\"min\":1,\"max\":16}]}";

        private const string AkmlSchema = "{\"schemaVersion\":7," +
            "\"groups\":[{\"id\":\"whitespace\",\"displayName\":\"Whitespace\"}]," +
            "\"settings\":[{\"id\":\"whitespace.tabSize\",\"groupId\":\"whitespace\",\"displayName\":\"Tab size\",\"type\":\"Int\",\"status\":\"Implemented\",\"default\":4}]}";

        private static async Task<FormatStylesEditorViewModel> LoadAsync(string schema, string? sqlPromptDoc, string profileJson)
        {
            var settings = ConfigManager.Load();
            settings.Formatter.ActiveProfile = "Mine";
            ConfigManager.Save(settings);

            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.ProfileList, new ProfileListResponse { Profiles = new[] { new ProfileInfo { Name = "Mine" } } });
            fake.Respond(MessageTypes.RequestStyleEditorSchema, new StyleEditorSchemaResponse { SchemaVersion = 1, SchemaJson = schema });
            fake.Respond(MessageTypes.FormatPreview, new FormatPreviewResponse { FormattedText = "SELECT 1" });
            fake.Respond(MessageTypes.ProfileGet, new ProfileGetResponse
            {
                Success = true, Name = "Mine", ProfileJson = profileJson,
                SqlPromptJson = sqlPromptDoc, IsSqlPromptStyle = sqlPromptDoc != null,
            });
            FormatStylesEditorViewModel.SetCachedSchemaForTests(null, null);
            var vm = new FormatStylesEditorViewModel(fake) { MainThreadSwitchOverride = () => Task.CompletedTask };
            await vm.LoadAsync();
            if (vm.LoadedProfileName == null) await vm.SelectProfileAsync("Mine");
            return vm;
        }

        [Fact]
        public async Task SqlPrompt_styles_use_number_of_spaces_in_tabs()
        {
            var vm = await LoadAsync(SqlPromptSchema,
                "{\"metadata\":{\"id\":\"m\",\"name\":\"Mine\"},\"whitespace\":{\"numberOfSpacesInTabs\":2}}",
                "{\"metadata\":{\"name\":\"Mine\"}}");

            Assert.Equal(2, vm.PreviewTabSize);
        }

        [Fact]
        public async Task Akml_styles_use_the_tab_size()
        {
            var vm = await LoadAsync(AkmlSchema, null, "{\"metadata\":{\"name\":\"Mine\"},\"whitespace\":{\"tabSize\":8}}");

            Assert.Equal(8, vm.PreviewTabSize);
        }

        [Fact]
        public async Task Follows_edits_and_raises_a_change()
        {
            var vm = await LoadAsync(SqlPromptSchema,
                "{\"metadata\":{\"id\":\"m\",\"name\":\"Mine\"},\"whitespace\":{\"numberOfSpacesInTabs\":4}}",
                "{\"metadata\":{\"name\":\"Mine\"}}");
            var changed = new List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            vm.SetWorkingValue("sqlPrompt.whitespace.numberOfSpacesInTabs", 3);

            Assert.Equal(3, vm.PreviewTabSize);
            Assert.Contains(nameof(FormatStylesEditorViewModel.PreviewTabSize), changed);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(40, 16)]
        public async Task Out_of_range_values_are_clamped(int stored, int expected)
        {
            var vm = await LoadAsync(SqlPromptSchema,
                "{\"metadata\":{\"id\":\"m\",\"name\":\"Mine\"},\"whitespace\":{\"numberOfSpacesInTabs\":" + stored + "}}",
                "{\"metadata\":{\"name\":\"Mine\"}}");

            Assert.Equal(expected, vm.PreviewTabSize);
        }
    }
}
