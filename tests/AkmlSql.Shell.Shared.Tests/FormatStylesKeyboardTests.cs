#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Formatting;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T091, STY-07) — the style editor's keyboard: Ctrl+S saves (only when there is
    /// something to save), Ctrl+F goes to the option search, and on the style list F2 renames,
    /// Delete deletes (refusing styles that cannot be deleted, with a reason) and Enter makes the
    /// style active.
    /// <para>No <c>await Task.Yield()</c> here: an [StaFact] has no synchronization context, so it
    /// would resume on a pool thread. The fake engine answers synchronously, so each key's action
    /// has finished when HandleKey returns.</para>
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class FormatStylesKeyboardTests : AppDataIsolatedTest
    {
        public FormatStylesKeyboardTests() : base("akmlsql-stylekeys-test-") { }

        private const string Schema = "{\"model\":\"sqlPrompt\",\"schemaVersion\":2001," +
            "\"groups\":[{\"id\":\"whitespace\",\"displayName\":\"Whitespace\",\"parentId\":\"global\",\"sample\":\"SELECT 1;\"}]," +
            "\"settings\":[{\"id\":\"sqlPrompt.whitespace.numberOfSpacesInTabs\",\"groupId\":\"whitespace\",\"displayName\":\"Number of spaces in tabs\",\"type\":\"Int\",\"status\":\"Implemented\",\"default\":4,\"min\":1,\"max\":16}]}";

        private static async Task<(FormatStylesEditorViewModel Vm, FakeRpcClientAccessor Fake)> LoadedAsync(string loaded, string active)
        {
            var settings = ConfigManager.Load();
            settings.Formatter.ActiveProfile = active;
            ConfigManager.Save(settings);

            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.ProfileList, new ProfileListResponse
            {
                Profiles = new[] { new ProfileInfo { Name = "Mine" }, new ProfileInfo { Name = "Default", IsBuiltIn = true } },
            });
            fake.Respond(MessageTypes.RequestStyleEditorSchema, new StyleEditorSchemaResponse { SchemaVersion = 2001, SchemaJson = Schema });
            fake.Respond(MessageTypes.FormatPreview, new FormatPreviewResponse { FormattedText = "SELECT 1" });
            fake.Respond<ProfileGetRequest>(MessageTypes.ProfileGet, r => new ProfileGetResponse
            {
                Success = true,
                Name = r.Name,
                ProfileJson = "{\"metadata\":{\"name\":\"" + r.Name + "\"}}",
                SqlPromptJson = "{\"metadata\":{\"id\":\"x\",\"name\":\"" + r.Name + "\"}}",
                IsSqlPromptStyle = true,
                IsBuiltIn = r.Name == "Default",
                HasBuiltIn = r.Name == "Default",
            });
            fake.Respond(MessageTypes.ProfileSave, new ProfileSaveResponse { Success = true });
            fake.Respond<ProfileRenameRequest>(MessageTypes.ProfileRename, r => new ProfileRenameResponse { Success = true, NewName = r.NewName });
            fake.Respond(MessageTypes.ProfileDelete, new ProfileDeleteResponse { Success = true });

            FormatStylesEditorViewModel.SetCachedSchemaForTests(null, null);
            var vm = new FormatStylesEditorViewModel(fake) { MainThreadSwitchOverride = () => Task.CompletedTask };
            await vm.LoadAsync();
            if (vm.LoadedProfileName != loaded) await vm.SelectProfileAsync(loaded);
            Assert.Equal(loaded, vm.LoadedProfileName);
            return (vm, fake);
        }

        private static T Field<T>(object target, string name) where T : class =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

        private static int CountOf(FakeRpcClientAccessor fake, int type) => fake.Requests.Count(r => r.MessageType == type);

        /// <summary>
        /// The window as it opens, on the loaded style. Built over an already-loaded view model, the
        /// list starts on its first style — and selecting it loads that style (in SSMS the window is
        /// built first and the styles load after). So the loaded style is selected again, the way a
        /// click would, which loads it back.
        /// </summary>
        private static FormatStylesEditorWindow Opened(FormatStylesEditorViewModel vm)
        {
            var loaded = vm.LoadedProfileName;
            var window = FormatStylesRowLayoutTests.NewWindow(vm);
            Field<ListBox>(window, "_styleList").SelectedItem = vm.Profiles.Single(p => p.Name == loaded);
            Assert.Equal(loaded, vm.LoadedProfileName);
            return window;
        }

        [StaFact]
        public async Task Ctrl_S_saves_only_when_there_is_something_to_save()
        {
            var (vm, fake) = await LoadedAsync("Mine", "Mine");
            var window = Opened(vm);
            try
            {
                Assert.True(window.HandleKey(Key.S, ModifierKeys.Control, listFocused: false));
                Assert.Equal(0, CountOf(fake, MessageTypes.ProfileSave));

                vm.SetWorkingValue("sqlPrompt.whitespace.numberOfSpacesInTabs", 2);
                window.HandleKey(Key.S, ModifierKeys.Control, listFocused: false);
                Assert.Equal(1, CountOf(fake, MessageTypes.ProfileSave));
                Assert.False(vm.IsDirty);
            }
            finally { FormatStylesRowLayoutTests.CloseWithoutPrompt(window); }
        }

        [StaFact]
        public async Task Ctrl_F_goes_to_the_option_search()
        {
            var (vm, _) = await LoadedAsync("Mine", "Mine");
            var window = Opened(vm);
            try
            {
                Assert.True(window.HandleKey(Key.F, ModifierKeys.Control, listFocused: false));
                Assert.Same(Field<TextBox>(window, "_searchBox"), FocusManager.GetFocusedElement(window));
            }
            finally { FormatStylesRowLayoutTests.CloseWithoutPrompt(window); }
        }

        [StaFact]
        public async Task F2_on_the_list_renames_through_the_name_prompt()
        {
            var (vm, fake) = await LoadedAsync("Mine", "Default");
            var window = Opened(vm);
            try
            {
                IReadOnlyCollection<string>? offeredNames = null;
                window.RenameNameOverride = (current, names) => { offeredNames = names; return "Northwind reports"; };

                Assert.True(window.HandleKey(Key.F2, ModifierKeys.None, listFocused: true));

                var request = fake.Requests.Where(r => r.MessageType == MessageTypes.ProfileRename)
                    .Select(r => r.Payload).OfType<ProfileRenameRequest>().Single();
                Assert.Equal("Northwind reports", request.NewName);
                Assert.Contains("Default", offeredNames!);
            }
            finally { FormatStylesRowLayoutTests.CloseWithoutPrompt(window); }
        }

        [StaFact]
        public async Task F2_outside_the_list_does_nothing()
        {
            var (vm, fake) = await LoadedAsync("Mine", "Default");
            var window = Opened(vm);
            try
            {
                var asked = false;
                window.RenameNameOverride = (_, _) => { asked = true; return "x"; };

                Assert.False(window.HandleKey(Key.F2, ModifierKeys.None, listFocused: false));
                Assert.False(asked);
            }
            finally { FormatStylesRowLayoutTests.CloseWithoutPrompt(window); }
        }

        [StaTheory]
        [InlineData("Default", "Mine", "Built-in")]   // a built-in style
        [InlineData("Mine", "Mine", "active")]        // the active style
        public async Task Delete_refuses_styles_that_cannot_be_deleted(string loaded, string active, string reason)
        {
            var (vm, fake) = await LoadedAsync(loaded, active);
            var window = Opened(vm);
            try
            {
                Assert.True(window.HandleKey(Key.Delete, ModifierKeys.None, listFocused: true));

                Assert.Equal(0, CountOf(fake, MessageTypes.ProfileDelete));
                Assert.Contains(reason, Field<TextBlock>(window, "_statusText").Text, StringComparison.OrdinalIgnoreCase);
            }
            finally { FormatStylesRowLayoutTests.CloseWithoutPrompt(window); }
        }

        [StaFact]
        public async Task Enter_on_the_list_makes_the_style_active()
        {
            var (vm, _) = await LoadedAsync("Mine", "Default");
            var window = Opened(vm);
            try
            {
                Assert.True(window.HandleKey(Key.Enter, ModifierKeys.None, listFocused: true));

                Assert.Equal("Mine", ConfigManager.Load().Formatter.ActiveProfile);
                Assert.Equal("Mine", vm.Profiles.Single(p => p.IsActive).Name);
            }
            finally { FormatStylesRowLayoutTests.CloseWithoutPrompt(window); }
        }

        [StaFact]
        public async Task The_row_actions_glyph_has_an_accessible_name()
        {
            var (vm, _) = await LoadedAsync("Mine", "Mine");
            var window = Opened(vm);
            try
            {
                var list = Field<ListBox>(window, "_styleList");
                list.ItemTemplate.Seal(); // WPF seals a template on first use; LoadContent needs it sealed
                var row = (DependencyObject)list.ItemTemplate.LoadContent();
                var glyph = FormatStylesRowLayoutTests.Descendants<TextBlock>(row).Single(t => t.Text == "⋮");
                Assert.Equal("Style actions", AutomationProperties.GetName(glyph));
            }
            finally { FormatStylesRowLayoutTests.CloseWithoutPrompt(window); }
        }
    }
}
