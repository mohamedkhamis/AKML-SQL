#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Formatting;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T077, STY-03, FR-022/FR-023) — Import and Export keep the style list, the ACTIVE
    /// marker, the header and "Set as active style" honest, never lose unsaved edits without
    /// asking, and never overwrite an existing style by name. Extends the
    /// <see cref="FormatStylesLifecycleTests"/> fake-IPC pattern to the window, with its dialog
    /// seams standing in for the file pickers and prompts.
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class FormatStylesImportExportTests : AppDataIsolatedTest
    {
        public FormatStylesImportExportTests() : base("akmlsql-importexport-test-") { }

        private const string Schema = "{\"model\":\"sqlPrompt\",\"schemaVersion\":2001," +
            "\"groups\":[{\"id\":\"whitespace\",\"displayName\":\"Whitespace\",\"parentId\":\"global\",\"sample\":\"SELECT 1;\"}]," +
            "\"settings\":[{\"id\":\"sqlPrompt.whitespace.numberOfSpacesInTabs\",\"groupId\":\"whitespace\",\"displayName\":\"Number of spaces in tabs\",\"type\":\"Int\",\"status\":\"Implemented\",\"default\":4,\"min\":1,\"max\":16}]}";

        /// <summary>The engine's style store as the fake sees it: name → built-in.</summary>
        private readonly Dictionary<string, bool> _stored = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["Mine"] = false,
            ["Default"] = true,
        };

        private FakeRpcClientAccessor NewFake()
        {
            var fake = new FakeRpcClientAccessor();
            fake.Respond<ProfileListRequest>(MessageTypes.ProfileList, _ => new ProfileListResponse
            {
                Profiles = _stored.Select(p => new ProfileInfo { Name = p.Key, IsBuiltIn = p.Value }).ToArray(),
            });
            fake.Respond(MessageTypes.RequestStyleEditorSchema, new StyleEditorSchemaResponse { SchemaVersion = 2001, SchemaJson = Schema });
            fake.Respond(MessageTypes.FormatPreview, new FormatPreviewResponse { FormattedText = "SELECT 1" });
            fake.Respond<ProfileGetRequest>(MessageTypes.ProfileGet, req => new ProfileGetResponse
            {
                Success = true,
                Name = req.Name,
                ProfileJson = "{\"metadata\":{\"name\":\"" + req.Name + "\"}}",
                SqlPromptJson = "{\"metadata\":{\"id\":\"x\",\"name\":\"" + req.Name + "\"},\"whitespace\":{\"numberOfSpacesInTabs\":4}}",
                IsSqlPromptStyle = true,
                IsBuiltIn = _stored.TryGetValue(req.Name, out var builtIn) && builtIn,
                HasBuiltIn = _stored.TryGetValue(req.Name, out var shipped) && shipped,
            });
            fake.Respond<ProfileImportRequest>(MessageTypes.ProfileImport, req =>
            {
                var name = req.TargetProfileName ?? "Northwind Style";
                _stored[name] = false;
                return new ProfileImportResponse { Success = true, ProfileName = name, OptionReports = Array.Empty<ProfileImportOptionReport>() };
            });
            fake.Respond(MessageTypes.ProfileSave, new ProfileSaveResponse { Success = true });
            fake.Respond(MessageTypes.ProfileExportSqlPrompt, new ProfileExportSqlPromptResponse { Success = true });
            return fake;
        }

        private async Task<(FormatStylesEditorViewModel Vm, FakeRpcClientAccessor Fake)> LoadedMineAsync()
        {
            var settings = ConfigManager.Load();
            settings.Formatter.ActiveProfile = "Mine";
            ConfigManager.Save(settings);

            var fake = NewFake();
            FormatStylesEditorViewModel.SetCachedSchemaForTests(null, null);
            var vm = new FormatStylesEditorViewModel(fake) { MainThreadSwitchOverride = () => Task.CompletedTask };
            await vm.LoadAsync();
            if (vm.LoadedProfileName == null) await vm.SelectProfileAsync("Mine");
            Assert.Equal("Mine", vm.LoadedProfileName);
            return (vm, fake);
        }

        /// <summary>A SQL Prompt .json style file named <paramref name="styleName"/> inside it.</summary>
        private string StyleFile(string styleName)
        {
            Directory.CreateDirectory(TempRoot);
            var path = Path.Combine(TempRoot, Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{\"metadata\":{\"id\":\"f\",\"name\":\"" + styleName + "\"},\"whitespace\":{\"numberOfSpacesInTabs\":2}}");
            return path;
        }

        private static FormatStylesEditorWindow NewWindow(FormatStylesEditorViewModel vm)
        {
            var window = FormatStylesRowLayoutTests.NewWindow(vm);
            window.ImportSummaryOverride = _ => { };
            return window;
        }

        private static T Field<T>(object target, string name) where T : class =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

        private static int CountOf(FakeRpcClientAccessor fake, int messageType) =>
            fake.Requests.Count(r => r.MessageType == messageType);

        [StaFact]
        public async Task Import_makes_the_style_active_in_the_list_header_and_button_at_once()
        {
            var (vm, fake) = await LoadedMineAsync();
            var window = NewWindow(vm);
            try
            {
                var file = StyleFile("Northwind Style");
                window.ImportFileOverride = () => file;

                await window.OnImportAsync();

                Assert.Equal("Northwind Style", ConfigManager.Load().Formatter.ActiveProfile);
                Assert.Equal("Northwind Style", Assert.Single(vm.Profiles, p => p.IsActive).Name);
                Assert.Equal("Active: Northwind Style", Field<TextBlock>(window, "_headerActiveChipText").Text);
                var setActive = Field<Button>(window, "_setActiveButton");
                Assert.False(setActive.IsEnabled);
                Assert.Equal("Northwind Style", ((StyleListItem)Field<ListBox>(window, "_styleList").SelectedItem).Name);
                Assert.Equal(1, CountOf(fake, MessageTypes.ProfileImport));
            }
            finally
            {
                FormatStylesRowLayoutTests.CloseWithoutPrompt(window);
            }
        }

        [StaFact]
        public async Task Import_with_unsaved_edits_asks_before_anything_is_imported()
        {
            var (vm, fake) = await LoadedMineAsync();
            var window = NewWindow(vm);
            try
            {
                vm.SetWorkingValue("sqlPrompt.whitespace.numberOfSpacesInTabs", 3);
                Assert.True(vm.IsDirty);

                var importsWhenAsked = -1;
                var pickerShown = false;
                vm.DirtyDecisionHandler = () =>
                {
                    importsWhenAsked = CountOf(fake, MessageTypes.ProfileImport);
                    return Task.FromResult(StyleSwitchDecision.Cancel);
                };
                var file = StyleFile("Northwind Style");
                window.ImportFileOverride = () => { pickerShown = true; return file; };

                await window.OnImportAsync();

                Assert.Equal(0, importsWhenAsked);
                Assert.False(pickerShown);
                Assert.Equal(0, CountOf(fake, MessageTypes.ProfileImport));
                Assert.True(vm.IsDirty); // Cancel keeps the edits
            }
            finally
            {
                FormatStylesRowLayoutTests.CloseWithoutPrompt(window);
            }
        }

        [StaFact]
        public async Task Import_after_discarding_edits_imports_and_leaves_nothing_unsaved()
        {
            var (vm, fake) = await LoadedMineAsync();
            var window = NewWindow(vm);
            try
            {
                vm.SetWorkingValue("sqlPrompt.whitespace.numberOfSpacesInTabs", 3);
                var asked = 0;
                vm.DirtyDecisionHandler = () => { asked++; return Task.FromResult(StyleSwitchDecision.Discard); };
                var file = StyleFile("Northwind Style");
                window.ImportFileOverride = () => file;

                await window.OnImportAsync();

                Assert.Equal(1, asked); // once, before the import — not again when the new style loads
                Assert.Equal(1, CountOf(fake, MessageTypes.ProfileImport));
                Assert.Equal(0, CountOf(fake, MessageTypes.ProfileSave));
                Assert.False(vm.IsDirty);
            }
            finally
            {
                FormatStylesRowLayoutTests.CloseWithoutPrompt(window);
            }
        }

        [StaTheory]
        [InlineData("Cancel")]
        [InlineData("Save")]
        [InlineData("Discard")]
        public async Task Export_with_unsaved_edits_asks_to_save_first(string answer)
        {
            var decision = (StyleSwitchDecision)Enum.Parse(typeof(StyleSwitchDecision), answer);
            var (vm, fake) = await LoadedMineAsync();
            var window = NewWindow(vm);
            try
            {
                vm.SetWorkingValue("sqlPrompt.whitespace.numberOfSpacesInTabs", 3);
                string? prompt = null;
                window.SaveDecisionOverride = message => { prompt = message; return decision; };
                window.ExportFileOverride = _ => Path.Combine(TempRoot, "Mine.json");
                fake.Requests.Clear();

                await window.OnExportAsync();

                Assert.Equal("Save changes to 'Mine' before exporting?", prompt);
                var sequence = fake.Requests
                    .Select(r => r.MessageType)
                    .Where(t => t == MessageTypes.ProfileSave || t == MessageTypes.ProfileExportSqlPrompt)
                    .ToList();
                switch (decision)
                {
                    case StyleSwitchDecision.Cancel:
                        Assert.Empty(sequence);
                        Assert.True(vm.IsDirty);
                        break;
                    case StyleSwitchDecision.Save:
                        Assert.Equal(new[] { MessageTypes.ProfileSave, MessageTypes.ProfileExportSqlPrompt }, sequence);
                        Assert.False(vm.IsDirty);
                        break;
                    default:
                        Assert.Equal(new[] { MessageTypes.ProfileExportSqlPrompt }, sequence);
                        Assert.True(vm.IsDirty); // the saved file was exported; the edits are still there
                        break;
                }
            }
            finally
            {
                FormatStylesRowLayoutTests.CloseWithoutPrompt(window);
            }
        }

        [StaFact]
        public async Task Export_of_a_clean_style_does_not_ask()
        {
            var (vm, fake) = await LoadedMineAsync();
            var window = NewWindow(vm);
            try
            {
                var asked = false;
                window.SaveDecisionOverride = _ => { asked = true; return StyleSwitchDecision.Cancel; };
                window.ExportFileOverride = _ => Path.Combine(TempRoot, "Mine.json");

                await window.OnExportAsync();

                Assert.False(asked);
                Assert.Equal(1, CountOf(fake, MessageTypes.ProfileExportSqlPrompt));
            }
            finally
            {
                FormatStylesRowLayoutTests.CloseWithoutPrompt(window);
            }
        }

        [StaTheory]
        [InlineData("Default")]
        [InlineData("Mine")]
        [InlineData("default")]
        public async Task Import_of_a_clashing_name_asks_for_a_new_one(string clashingName)
        {
            var (vm, fake) = await LoadedMineAsync();
            var window = NewWindow(vm);
            try
            {
                var file = StyleFile(clashingName);
                window.ImportFileOverride = () => file;
                string? suggested = null;
                IReadOnlyCollection<string>? existing = null;
                window.ImportNameOverride = (s, names) => { suggested = s; existing = names; return "Northwind Copy"; };

                await window.OnImportAsync();

                Assert.Equal(clashingName + " (imported)", suggested);
                Assert.Contains("Default", existing!);
                Assert.Contains("Mine", existing!);
                var request = fake.Requests.Where(r => r.MessageType == MessageTypes.ProfileImport)
                    .Select(r => r.Payload).OfType<ProfileImportRequest>().Single();
                Assert.Equal("Northwind Copy", request.TargetProfileName);
                Assert.Equal("Northwind Copy", ConfigManager.Load().Formatter.ActiveProfile);
                Assert.True(_stored["Default"]); // the built-in is untouched
                Assert.Equal(0, CountOf(fake, MessageTypes.ProfileSave));
            }
            finally
            {
                FormatStylesRowLayoutTests.CloseWithoutPrompt(window);
            }
        }

        [StaFact]
        public async Task Cancelling_the_new_name_imports_nothing()
        {
            var (vm, fake) = await LoadedMineAsync();
            var window = NewWindow(vm);
            try
            {
                var file = StyleFile("Default");
                window.ImportFileOverride = () => file;
                window.ImportNameOverride = (_, _) => null;

                await window.OnImportAsync();

                Assert.Equal(0, CountOf(fake, MessageTypes.ProfileImport));
                Assert.Equal("Mine", ConfigManager.Load().Formatter.ActiveProfile);
            }
            finally
            {
                FormatStylesRowLayoutTests.CloseWithoutPrompt(window);
            }
        }
    }
}
