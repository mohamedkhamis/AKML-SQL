#nullable enable
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using AkmlSql.Shell.Shared.Commands;
using AkmlSql.Shell.Shared.Editor.Completion;
using AkmlSql.Shell.Shared.Productivity.CommandPalette;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (scenario 41) — SSMS sends Enter pressed in the Command Palette's search box down the
    /// editor's command chain, where it was swallowed to keep it out of the document, so Enter did
    /// nothing in the palette. The editor now hands it back to the focused element.
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class FocusedKeyDeliveryTests : AppDataIsolatedTest
    {
        public FocusedKeyDeliveryTests() : base("akml-focused-key-")
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

        [StaFact]
        public void Enter_reaches_the_focused_elements_handlers()
        {
            var box = new TextBox();
            Key? seen = null;
            box.PreviewKeyDown += (_, e) => { seen = e.Key; e.Handled = true; };
            using var source = new HwndSource(new HwndSourceParameters("akml-focused-key-test")) { RootVisual = box };

            Assert.True(FocusedKeyDelivery.Deliver(box, Key.Enter));
            Assert.Equal(Key.Enter, seen);
        }

        [StaFact]
        public void Unhandled_or_windowless_is_reported_so_the_key_is_still_kept_from_the_document()
        {
            var box = new TextBox();
            Assert.False(FocusedKeyDelivery.Deliver(box, Key.Enter));   // in no window

            using var source = new HwndSource(new HwndSourceParameters("akml-focused-key-test")) { RootVisual = box };
            Assert.False(FocusedKeyDelivery.Deliver(box, Key.Enter));   // a plain TextBox ignores Enter
        }

        [StaFact]
        public void Enter_handed_to_the_palettes_search_box_toggles_the_option()
        {
            var palette = new CommandPaletteWindow();
            var vm = new CommandPaletteViewModel();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(CommandPaletteWindow).GetField("_viewModel", flags)!.SetValue(palette, vm);
            var window = (Window)typeof(CommandPaletteWindow).GetMethod("CreateWindow", flags)!.Invoke(palette, null)!;
            var search = (TextBox)typeof(CommandPaletteWindow).GetField("_searchBox", flags)!.GetValue(palette)!;
            // Only the search box goes in a window (its key handler comes with it): laying out the
            // list would build rows from a style another test's thread may own.
            switch (search.Parent)
            {
                case Panel panel: panel.Children.Remove(search); break;
                case Decorator decorator: decorator.Child = null; break;
                case ContentControl control: control.Content = null; break;
            }
            using var source = new HwndSource(new HwndSourceParameters("akml-palette-key-test")) { RootVisual = search };
            try
            {
                for (var i = 1; i <= "nullab".Length; i++) vm.SearchText = "nullab".Substring(0, i);
                var option = (OptionPaletteEntry)vm.FilteredCommands[0];
                var before = option.IsOn;

                Assert.True(FocusedKeyDelivery.Deliver(search, Key.Enter));
                Assert.NotEqual(before, option.IsOn);
            }
            finally
            {
                window.Close();
            }
        }
    }
}
