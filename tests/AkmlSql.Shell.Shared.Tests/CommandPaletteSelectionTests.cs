#nullable enable
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using AkmlSql.Shell.Shared.Commands;
using AkmlSql.Shell.Shared.Productivity.CommandPalette;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (OPT-07) — typing in the Command Palette always leaves the first result selected,
    /// so Enter acts on it; an Options setting that is the only match is no exception. The list is
    /// bound to the view model the way <c>CommandPaletteWindow</c> binds it (two-way SelectedIndex).
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class CommandPaletteSelectionTests : AppDataIsolatedTest
    {
        public CommandPaletteSelectionTests() : base("akml-palette-selection-")
        {
            OptionsCommand.TestRpcAccessor = new FakeRpcClientAccessor();
            OptionsCommand.ThemePreferenceOverride = _ => { };
        }

        public override void Dispose()
        {
            OptionsCommand.TestRpcAccessor = null;
            OptionsCommand.ThemePreferenceOverride = null;
            base.Dispose();
        }

        private static (CommandPaletteViewModel Vm, ListBox List) Palette()
        {
            var vm = new CommandPaletteViewModel();
            var list = new ListBox();
            list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("FilteredCommands") { Source = vm });
            list.SetBinding(ListBox.SelectedIndexProperty, new Binding("SelectedIndex") { Source = vm, Mode = BindingMode.TwoWay });
            return (vm, list);
        }

        private static void Type(CommandPaletteViewModel vm, string text)
        {
            for (var i = 1; i <= text.Length; i++)
                vm.SearchText = text.Substring(0, i);
        }

        [StaFact]
        public void An_option_that_is_the_only_match_is_selected()
        {
            var (vm, list) = Palette();

            Type(vm, "nullab");

            var option = Assert.Single(vm.FilteredCommands.OfType<OptionPaletteEntry>(), o => o.Name.EndsWith("Show nullability info"));
            Assert.Equal(vm.FilteredCommands.IndexOf(option), vm.SelectedIndex);
            Assert.Equal(vm.SelectedIndex, list.SelectedIndex);
        }

        [StaFact]
        public void Enter_with_nothing_highlighted_acts_on_the_first_result()
        {
            var (vm, _) = Palette();
            Type(vm, "nullab");
            var option = (OptionPaletteEntry)vm.FilteredCommands[0];
            var before = option.IsOn;

            vm.SelectedIndex = -1;
            vm.ExecuteSelected();

            Assert.NotEqual(before, option.IsOn);
        }

        [StaFact]
        public void An_option_named_by_the_query_comes_before_letter_by_letter_command_matches()
        {
            // Scenario 41: "retention" + Enter ran "Create snippet from selection", whose letters
            // happen to spell it, instead of opening Options at History's Retention row.
            var (vm, list) = Palette();

            Type(vm, "retention");

            var first = Assert.IsType<OptionPaletteEntry>(vm.FilteredCommands[0]);
            Assert.EndsWith("Retention", first.Name);
            Assert.Equal(0, list.SelectedIndex);
        }

        [StaFact]
        public void Commands_that_hold_the_query_still_come_first()
        {
            var (vm, _) = Palette();

            Type(vm, "format");

            Assert.IsNotType<OptionPaletteEntry>(vm.FilteredCommands[0]);
            Assert.Contains("format", vm.FilteredCommands[0].Name, System.StringComparison.OrdinalIgnoreCase);
        }

        [StaFact]
        public void Typing_keeps_the_first_result_selected()
        {
            var (vm, list) = Palette();

            Type(vm, "format");

            Assert.NotEmpty(vm.FilteredCommands);
            Assert.Equal(0, vm.SelectedIndex);
            Assert.Equal(0, list.SelectedIndex);
        }

        [StaFact]
        public void Enter_on_a_clicked_row_acts_on_it()
        {
            // Clicking a row moves focus from the search box to the list; Enter there used to do nothing.
            var palette = new CommandPaletteWindow();
            var vm = new CommandPaletteViewModel();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(CommandPaletteWindow).GetField("_viewModel", flags)!.SetValue(palette, vm);
            var window = (Window)typeof(CommandPaletteWindow).GetMethod("CreateWindow", flags)!.Invoke(palette, null)!;
            var list = (ListBox)typeof(CommandPaletteWindow).GetField("_listBox", flags)!.GetValue(palette)!;
            Type(vm, "nullab");
            var option = (OptionPaletteEntry)vm.FilteredCommands[0];
            var before = option.IsOn;

            using var source = new HwndSource(new HwndSourceParameters("akml-palette-enter-test"));
            try
            {
                var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent };
                list.RaiseEvent(args);

                Assert.True(args.Handled);
                Assert.NotEqual(before, option.IsOn);
            }
            finally
            {
                window.Close();
            }
        }
    }
}
