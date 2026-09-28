#nullable enable
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Formatting;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T086, STY-04, FR-030) — option search matches an option's label, description,
    /// note, subgroup, choice labels and id, case-insensitively, and reports per-page counts and
    /// the first match.
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class FormatStylesSearchTests : AppDataIsolatedTest
    {
        public FormatStylesSearchTests() : base("akmlsql-stylesearch-test-") { }

        // One "comma" hit per searchable field, spread over three pages; "casing" has none.
        private const string Schema = "{\"model\":\"sqlPrompt\",\"schemaVersion\":2001,\"groups\":[" +
            "{\"id\":\"lists\",\"displayName\":\"Lists\",\"parentId\":\"global\"}," +
            "{\"id\":\"dml\",\"displayName\":\"DML\",\"parentId\":\"statements\"}," +
            "{\"id\":\"casing\",\"displayName\":\"Casing\",\"parentId\":\"global\"}," +
            "{\"id\":\"ddl\",\"displayName\":\"DDL\",\"parentId\":\"statements\"}]," +
            "\"settings\":[" +
            "{\"id\":\"sqlPrompt.lists.placeCommasBefore\",\"groupId\":\"lists\",\"displayName\":\"Place Commas before items\",\"type\":\"Bool\",\"default\":false}," +
            "{\"id\":\"sqlPrompt.lists.alignItems\",\"groupId\":\"lists\",\"displayName\":\"Align items\",\"type\":\"Bool\",\"default\":true,\"description\":\"Lines up each item after the COMMA.\"}," +
            "{\"id\":\"sqlPrompt.lists.wrap\",\"groupId\":\"lists\",\"displayName\":\"Wrap long lists\",\"type\":\"Bool\",\"default\":true}," +
            "{\"id\":\"sqlPrompt.dml.collapse\",\"groupId\":\"dml\",\"displayName\":\"Collapse short statements\",\"type\":\"Bool\",\"default\":true,\"note\":\"Ignored when a comma list wraps.\"}," +
            "{\"id\":\"sqlPrompt.dml.spacing\",\"groupId\":\"dml\",\"displayName\":\"Spacing\",\"type\":\"Enum\",\"default\":\"one\",\"allowedEnumValues\":[\"one\",\"two\"],\"enumLabels\":[\"One space\",\"After each comma\"]}," +
            "{\"id\":\"sqlPrompt.casing.keywords\",\"groupId\":\"casing\",\"displayName\":\"Keywords\",\"type\":\"Enum\",\"default\":\"upper\",\"allowedEnumValues\":[\"upper\",\"lower\"]}," +
            "{\"id\":\"sqlPrompt.ddl.trailingCommaStyle\",\"groupId\":\"ddl\",\"displayName\":\"Column list ending\",\"type\":\"Bool\",\"default\":false}," +
            "{\"id\":\"sqlPrompt.ddl.parens\",\"groupId\":\"ddl\",\"displayName\":\"Parentheses\",\"type\":\"Bool\",\"default\":false,\"subgroup\":\"Commas and parentheses\"}" +
            "]}";

        private static async Task<FormatStylesEditorViewModel> LoadedAsync()
        {
            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.ProfileList, new ProfileListResponse { Profiles = new ProfileInfo[0] });
            fake.Respond(MessageTypes.RequestStyleEditorSchema, new StyleEditorSchemaResponse { SchemaVersion = 2001, SchemaJson = Schema });
            fake.Respond(MessageTypes.FormatPreview, new FormatPreviewResponse { FormattedText = "SELECT 1" });
            FormatStylesEditorViewModel.SetCachedSchemaForTests(null, null);
            var vm = new FormatStylesEditorViewModel(fake) { MainThreadSwitchOverride = () => Task.CompletedTask };
            await vm.LoadAsync();
            Assert.NotNull(vm.SchemaModel);
            return vm;
        }

        [Fact]
        public async Task Search_matches_every_searchable_field_and_counts_per_page()
        {
            var vm = await LoadedAsync();

            var result = vm.Search("comma");

            Assert.Equal(new[] { "lists", "dml", "ddl" }, result.GroupIds);
            Assert.Equal(2, result.Counts["lists"]);  // label, description
            Assert.Equal(2, result.Counts["dml"]);    // note, choice label
            Assert.Equal(2, result.Counts["ddl"]);    // option id, subgroup
            Assert.False(result.Counts.ContainsKey("casing"));
            Assert.Equal(
                new[] { "sqlPrompt.lists.placeCommasBefore", "sqlPrompt.lists.alignItems", "sqlPrompt.dml.collapse",
                        "sqlPrompt.dml.spacing", "sqlPrompt.ddl.trailingCommaStyle", "sqlPrompt.ddl.parens" }.OrderBy(x => x),
                result.OptionIds.OrderBy(x => x));
            Assert.Equal("lists", result.FirstGroupId);
            Assert.Equal("sqlPrompt.lists.placeCommasBefore", result.FirstOptionId);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public async Task An_empty_query_shows_every_page_without_counts(string? query)
        {
            var vm = await LoadedAsync();

            var result = vm.Search(query);

            Assert.Equal(new[] { "lists", "dml", "casing", "ddl" }, result.GroupIds);
            Assert.Empty(result.Counts);
            Assert.Empty(result.OptionIds);
            Assert.Null(result.FirstOptionId);
        }

        [Fact]
        public async Task A_query_with_no_match_shows_no_pages()
        {
            var vm = await LoadedAsync();

            var result = vm.Search("zzz-nothing");

            Assert.Empty(result.GroupIds);
            Assert.Null(result.FirstGroupId);
        }
    }
}
