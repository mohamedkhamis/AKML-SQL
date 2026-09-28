#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Formatting;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T087, STY-05/STY-06, FR-031/FR-032) — options that differ from SQL Prompt's
    /// default are marked, counted per page and resettable one by one; the preview marks the lines
    /// an option edit moved (never after a style or page switch); and a style is only "unsaved"
    /// while its values actually differ from what was saved.
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class FormatStylesChangeMarkersTests : AppDataIsolatedTest
    {
        public FormatStylesChangeMarkersTests() : base("akmlsql-changemarkers-test-") { }

        private const string Wrap = "sqlPrompt.lists.wrap";
        private const string Align = "sqlPrompt.lists.alignItems";
        private const string Keywords = "sqlPrompt.casing.keywords";

        private const string Schema = "{\"model\":\"sqlPrompt\",\"schemaVersion\":2001,\"groups\":[" +
            "{\"id\":\"lists\",\"displayName\":\"Lists\",\"parentId\":\"global\",\"sample\":\"SELECT a, b FROM t;\"}," +
            "{\"id\":\"casing\",\"displayName\":\"Casing\",\"parentId\":\"global\",\"sample\":\"select 1;\"}]," +
            "\"settings\":[" +
            "{\"id\":\"" + Wrap + "\",\"groupId\":\"lists\",\"displayName\":\"Wrap lists\",\"type\":\"Bool\",\"default\":true}," +
            "{\"id\":\"" + Align + "\",\"groupId\":\"lists\",\"displayName\":\"Align items\",\"type\":\"Bool\",\"default\":true}," +
            "{\"id\":\"" + Keywords + "\",\"groupId\":\"casing\",\"displayName\":\"Keywords\",\"type\":\"Enum\",\"default\":\"upper\",\"allowedEnumValues\":[\"upper\",\"lower\"]}]}";

        // Mine stores wrap = false (default true): one change on Lists before any edit.
        private const string MineDoc = "{\"metadata\":{\"id\":\"m\",\"name\":\"Mine\"},\"lists\":{\"wrap\":false}}";

        private static bool Wraps(FormatPreviewRequest r) => !r.ProfileJson.Replace(" ", "").Contains("\"wrap\":false");

        private static async Task<(FormatStylesEditorViewModel Vm, FakeRpcClientAccessor Fake)> LoadedAsync()
        {
            var settings = ConfigManager.Load();
            settings.Formatter.ActiveProfile = "Mine";
            ConfigManager.Save(settings);

            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.ProfileList, new ProfileListResponse
            {
                Profiles = new[] { new ProfileInfo { Name = "Mine" }, new ProfileInfo { Name = "Other" } },
            });
            fake.Respond(MessageTypes.RequestStyleEditorSchema, new StyleEditorSchemaResponse { SchemaVersion = 2001, SchemaJson = Schema });
            // Wrapped: the column list moves to line 1. Unwrapped: one line.
            fake.Respond<FormatPreviewRequest>(MessageTypes.FormatPreview, r => new FormatPreviewResponse
            {
                FormattedText = Wraps(r) ? "SELECT a,\n       b\nFROM t;" : "SELECT a, b\nFROM t;",
            });
            fake.Respond<ProfileGetRequest>(MessageTypes.ProfileGet, r => new ProfileGetResponse
            {
                Success = true,
                Name = r.Name,
                ProfileJson = "{\"metadata\":{\"name\":\"" + r.Name + "\"}}",
                SqlPromptJson = r.Name == "Mine" ? MineDoc : "{\"metadata\":{\"id\":\"o\",\"name\":\"Other\"}}",
                IsSqlPromptStyle = true,
            });
            fake.Respond(MessageTypes.ProfileSave, new ProfileSaveResponse { Success = true });

            FormatStylesEditorViewModel.SetCachedSchemaForTests(null, null);
            var vm = new FormatStylesEditorViewModel(fake) { MainThreadSwitchOverride = () => Task.CompletedTask };
            await vm.LoadAsync();
            if (vm.LoadedProfileName == null) await vm.SelectProfileAsync("Mine");
            Assert.Equal("Mine", vm.LoadedProfileName);
            await WaitForAsync(() => vm.PreviewText == "SELECT a, b\nFROM t;", "the loaded style's preview");
            return (vm, fake);
        }

        private static async Task WaitForAsync(Func<bool> condition, string what)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out waiting for {what}.");
                await Task.Delay(20);
            }
        }

        [Fact]
        public async Task Options_that_differ_from_the_default_are_marked_and_counted_per_page()
        {
            var (vm, _) = await LoadedAsync();

            Assert.True(vm.IsChanged(Wrap));
            Assert.False(vm.IsChanged(Align));
            Assert.Equal(1, vm.ChangedCount("lists"));
            Assert.Equal(0, vm.ChangedCount("casing"));

            vm.SetWorkingValue(Keywords, "lower");
            Assert.True(vm.IsChanged(Keywords));
            Assert.Equal(1, vm.ChangedCount("casing"));
        }

        [Fact]
        public async Task Reset_option_restores_the_default_and_lowers_the_count()
        {
            var (vm, _) = await LoadedAsync();

            vm.ResetOption(Wrap);

            Assert.Equal(true, vm.GetWorkingValue(Wrap));
            Assert.False(vm.IsChanged(Wrap));
            Assert.Equal(0, vm.ChangedCount("lists"));
            Assert.True(vm.IsDirty); // the saved style still has wrap = false
        }

        [Fact]
        public async Task Setting_an_option_back_to_its_saved_value_clears_unsaved()
        {
            var (vm, _) = await LoadedAsync();

            vm.SetWorkingValue(Align, false);
            Assert.True(vm.IsDirty);

            vm.SetWorkingValue(Align, true);
            Assert.False(vm.IsDirty);

            vm.SetWorkingValue(Wrap, true);
            Assert.True(vm.IsDirty);
            vm.SetWorkingValue(Wrap, false);
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public async Task After_a_save_the_saved_values_move_with_it()
        {
            var (vm, _) = await LoadedAsync();

            vm.SetWorkingValue(Align, false);
            Assert.True(await vm.SaveAsync());
            Assert.False(vm.IsDirty);

            vm.SetWorkingValue(Align, true);  // now a change from the saved value
            Assert.True(vm.IsDirty);
        }

        [Fact]
        public async Task An_option_edit_marks_the_lines_it_moved()
        {
            var (vm, _) = await LoadedAsync();
            Assert.Empty(vm.MovedLines); // loading a style marks nothing

            vm.SetWorkingValue(Wrap, true);
            await WaitForAsync(() => vm.PreviewText.StartsWith("SELECT a,\n"), "the wrapped preview");

            // "SELECT a, b" → "SELECT a,"; "FROM t;" → "       b"; new line 2 "FROM t;".
            Assert.Equal(new[] { 0, 1, 2 }, vm.MovedLines.OrderBy(i => i));

            vm.ResetOption(Wrap); // already the default: same text, nothing moved
            await Task.Delay(400);
            Assert.Empty(vm.MovedLines);
        }

        [Fact]
        public async Task A_page_or_style_switch_marks_nothing()
        {
            var (vm, _) = await LoadedAsync();
            vm.PreviewSourceMode = FormatPreviewSource.PageSample;

            vm.PageSample = "select 1;"; // page switch
            await Task.Delay(400);
            Assert.Empty(vm.MovedLines);

            Assert.True(await vm.SelectProfileAsync("Other")); // style switch: wrap back to the default
            await WaitForAsync(() => vm.PreviewText.StartsWith("SELECT a,\n"), "Other's preview");
            Assert.Empty(vm.MovedLines);
        }
    }
}
