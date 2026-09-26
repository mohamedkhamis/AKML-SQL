#nullable enable
using System.Collections.Generic;
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
    /// The SSMS Format Styles window itself: a gate option re-enables the rows it controls in place
    /// (the page used to be rebuilt, dropping keyboard focus), and saving a built-in makes its list
    /// item "Built-in · modified" at once (Reset used to refuse until the window was reopened).
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class FormatStylesWindowFixTests : AppDataIsolatedTest
    {
        public FormatStylesWindowFixTests() : base("akmlsql-stylewindow-test-") { }

        private const string Schema = "{\"model\":\"sqlPrompt\",\"schemaVersion\":2001," +
            "\"groups\":[" +
            "{\"id\":\"lists\",\"displayName\":\"Lists\",\"parentId\":\"global\",\"sample\":\"SELECT a, b FROM t;\"}," +
            "{\"id\":\"dml\",\"displayName\":\"Data (DML)\",\"parentId\":\"statements\",\"sample\":\"UPDATE t SET a = 1;\"}]," +
            "\"settings\":[" +
            "{\"id\":\"sqlPrompt.lists.placeCommasBeforeItems\",\"groupId\":\"lists\",\"displayName\":\"Place commas before items\",\"type\":\"Bool\",\"status\":\"Implemented\",\"default\":false}," +
            "{\"id\":\"sqlPrompt.dml.collapseShortStatements\",\"groupId\":\"dml\",\"displayName\":\"Collapse short statements\",\"type\":\"Bool\",\"status\":\"Implemented\",\"default\":false}," +
            "{\"id\":\"sqlPrompt.dml.collapseStatementsShorterThan\",\"groupId\":\"dml\",\"displayName\":\"Collapse statements shorter than\",\"type\":\"Int\",\"status\":\"Implemented\",\"default\":80," +
            "\"min\":1,\"max\":1000,\"enabledWhen\":{\"id\":\"sqlPrompt.dml.collapseShortStatements\",\"value\":true}}" +
            "]}";

        private const string Document = "{\"metadata\":{\"id\":\"id-1\",\"name\":\"Compact\"}," +
            "\"lists\":{\"placeCommasBeforeItems\":false},\"dml\":{\"collapseShortStatements\":true,\"collapseStatementsShorterThan\":160}}";

        private static async Task<(FormatStylesEditorViewModel Vm, FakeRpcClientAccessor Fake)> LoadedBuiltInAsync()
        {
            var settings = ConfigManager.Load();
            settings.Formatter.ActiveProfile = "Compact";
            ConfigManager.Save(settings);

            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.ProfileList, new ProfileListResponse { Profiles = new[] { new ProfileInfo { Name = "Compact", IsBuiltIn = true } } });
            fake.Respond(MessageTypes.RequestStyleEditorSchema, new StyleEditorSchemaResponse { SchemaVersion = 2001, SchemaJson = Schema });
            fake.Respond(MessageTypes.FormatPreview, new FormatPreviewResponse { FormattedText = "SELECT 1" });
            fake.Respond(MessageTypes.ProfileSave, new ProfileSaveResponse { Success = true });
            fake.Respond(MessageTypes.ProfileGet, new ProfileGetResponse
            {
                Success = true, Name = "Compact", ProfileJson = "{\"metadata\":{\"name\":\"Compact\"}}",
                SqlPromptJson = Document, IsSqlPromptStyle = true, IsBuiltIn = true, HasBuiltIn = true,
            });
            FormatStylesEditorViewModel.SetCachedSchemaForTests(null, null);
            var vm = new FormatStylesEditorViewModel(fake) { MainThreadSwitchOverride = () => Task.CompletedTask };
            await vm.LoadAsync();
            if (vm.LoadedProfileName == null) await vm.SelectProfileAsync("Compact");
            return (vm, fake);
        }

        /// <summary>
        /// Outside VS/SSMS, DialogWindow's first construction fails once while it looks up
        /// IVsSettingsManager for its DPI helper (a XamlParseException out of BuildUi); the lookup is
        /// not repeated, so the next construction succeeds. Absorbing that one failure here keeps
        /// these tests independent of which one runs first.
        /// </summary>
        private static FormatStylesEditorWindow NewWindow(FormatStylesEditorViewModel vm)
        {
            try { return new FormatStylesEditorWindow(vm); }
            catch (System.Windows.Markup.XamlParseException) { return new FormatStylesEditorWindow(vm); }
        }

        /// <summary>
        /// Closes without the "save changes?" prompt: with unsaved edits OnClosing shows a modal
        /// MessageBox, which nobody answers in a test run (it hung the run).
        /// </summary>
        private static void CloseWithoutPrompt(FormatStylesEditorWindow window)
        {
            window.GetType().GetField("_closeConfirmed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            window.Close();
        }

        private static object? Call(object target, string method, params object?[] args) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

        private static T Field<T>(object target, string name) where T : class =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                if (child is T match) yield return match;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }

        [StaFact]
        public async Task A_gate_switch_enables_its_rows_in_place_and_keeps_the_page()
        {
            try
            {
                var (vm, _) = await LoadedBuiltInAsync();
                var window = NewWindow(vm);
                try
                {
                    var dml = FormatStylesSchemaModel.Parse(Schema).FlatGroups.Single(g => g.Id == "dml");
                    Call(window, "UpdateRightForGroup", dml, "Statements");
                    var host = Field<StackPanel>(window, "_settingControlsHost");
                    var gate = Descendants<CheckBox>(host).Single();
                    var threshold = Descendants<TextBox>(host).Single();
                    var rows = host.Children.Cast<UIElement>().ToList();
                    Assert.True(gate.IsChecked);
                    Assert.True(threshold.IsEnabled);

                    gate.IsChecked = false;

                    // The same controls, now disabled: nothing was rebuilt, so focus stays put.
                    Assert.Equal(rows, host.Children.Cast<UIElement>().ToList());
                    Assert.Same(gate, Descendants<CheckBox>(host).Single());
                    Assert.False(threshold.IsEnabled);
                    Assert.Equal(false, vm.GetWorkingValue("sqlPrompt.dml.collapseShortStatements"));

                    gate.IsChecked = true;
                    Assert.True(threshold.IsEnabled);
                }
                finally
                {
                    CloseWithoutPrompt(window);
                }
            }
            finally
            {
                FormatStylesEditorViewModel.SetCachedSchemaForTests(null, null);
            }
        }

        [StaFact]
        public async Task Saving_a_built_in_marks_it_modified_so_reset_is_available_at_once()
        {
            try
            {
                var (vm, fake) = await LoadedBuiltInAsync();
                var window = NewWindow(vm);
                try
                {
                    Assert.False(vm.Profiles.Single().IsCustomized);
                    vm.SetWorkingValue("sqlPrompt.lists.placeCommasBeforeItems", true);
                    // What the engine lists once the override exists.
                    fake.Respond(MessageTypes.ProfileList, new ProfileListResponse
                    {
                        Profiles = new[] { new ProfileInfo { Name = "Compact", IsBuiltIn = false, IsCustomizedBuiltIn = true } },
                    });

                    await (Task)Call(window, "SaveSelectedStyleAsync")!;

                    Assert.True(vm.IsSelectedCustomized);
                    var item = vm.Profiles.Single();
                    Assert.True(item.IsCustomized);
                    Assert.Equal("Built-in · modified", item.Kind);
                    Assert.Same(item, Field<ListBox>(window, "_styleList").SelectedItem);
                }
                finally
                {
                    CloseWithoutPrompt(window);
                }
            }
            finally
            {
                FormatStylesEditorViewModel.SetCachedSchemaForTests(null, null);
            }
        }
    }
}
