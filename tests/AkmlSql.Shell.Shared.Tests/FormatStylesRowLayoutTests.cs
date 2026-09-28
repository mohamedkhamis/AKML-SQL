#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Formatting;
using AkmlSql.Shell.Shared.Ui.Theme;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T075, STY-01, FR-020) — at the window's default size, no option label on any of
    /// SQL Prompt's pages is clipped mid-word: labels wrap instead, and clicking a Bool option's
    /// text toggles it. Uses a snapshot of the real editor schema (kept current by
    /// <c>AkmlSql.Formatting.Tests EditorSchemaFixtureTests</c>).
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class FormatStylesRowLayoutTests : AppDataIsolatedTest
    {
        public FormatStylesRowLayoutTests() : base("akmlsql-rowlayout-test-") { }

        internal static string SchemaJson()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir!.FullName, "tests", "AkmlSql.Shell.Shared.Tests", "Fixtures", "sqlprompt-editor-schema.json"));
        }

        internal static async Task<FormatStylesEditorViewModel> LoadedAsync(string schema)
        {
            var settings = ConfigManager.Load();
            settings.Formatter.ActiveProfile = "Default";
            ConfigManager.Save(settings);

            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.ProfileList, new ProfileListResponse { Profiles = new[] { new ProfileInfo { Name = "Default", IsBuiltIn = true } } });
            fake.Respond(MessageTypes.RequestStyleEditorSchema, new StyleEditorSchemaResponse { SchemaVersion = 2001, SchemaJson = schema });
            fake.Respond(MessageTypes.FormatPreview, new FormatPreviewResponse { FormattedText = "SELECT 1" });
            fake.Respond(MessageTypes.ProfileGet, new ProfileGetResponse
            {
                Success = true, Name = "Default", ProfileJson = "{\"metadata\":{\"name\":\"Default\"}}",
                SqlPromptJson = "{\"metadata\":{\"id\":\"d\",\"name\":\"Default\"}}", IsSqlPromptStyle = true, IsBuiltIn = true, HasBuiltIn = true,
            });
            FormatStylesEditorViewModel.SetCachedSchemaForTests(null, null);
            var vm = new FormatStylesEditorViewModel(fake) { MainThreadSwitchOverride = () => Task.CompletedTask };
            await vm.LoadAsync();
            if (vm.LoadedProfileName == null) await vm.SelectProfileAsync("Default");
            return vm;
        }

        internal static FormatStylesEditorWindow NewWindow(FormatStylesEditorViewModel vm)
        {
            try { return new FormatStylesEditorWindow(vm); }
            catch (System.Windows.Markup.XamlParseException) { return new FormatStylesEditorWindow(vm); }
        }

        internal static void CloseWithoutPrompt(FormatStylesEditorWindow window)
        {
            window.GetType().GetField("_closeConfirmed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            window.Close();
        }

        /// <summary>
        /// The window's client area at its default 1060×680 size (less the resize borders and the
        /// title bar).
        /// </summary>
        internal static readonly Size DefaultClientSize = new Size(1060 - 16, 680 - 39);

        /// <summary>
        /// Moves the window's content into a themed host that can be laid out. A window that is
        /// never shown stays Collapsed and skips layout, so measuring the window itself measures
        /// nothing; its only resources are the theme's, which the host carries instead.
        /// </summary>
        internal static FrameworkElement LayoutRoot(FormatStylesEditorWindow window)
        {
            var content = (UIElement)window.Content;
            window.Content = null;
            var host = new Border { Child = content };
            ThemeRegistry.Instance.AttachTo(host);
            return host;
        }

        internal static void ShowPage(FormatStylesEditorWindow window, FrameworkElement layoutRoot, FormatStylesSchemaModel.Group group)
        {
            window.GetType().GetMethod("UpdateRightForGroup", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, new object?[] { group, "Page" });
            layoutRoot.Measure(DefaultClientSize);
            layoutRoot.Arrange(new Rect(DefaultClientSize));
            layoutRoot.UpdateLayout();
        }

        internal static StackPanel Host(FormatStylesEditorWindow window) =>
            (StackPanel)window.GetType().GetField("_settingControlsHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

        internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                if (child is T match) yield return match;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }

        private static IEnumerable<TextBlock> Labels(StackPanel host) =>
            Descendants<TextBlock>(host).Where(t => Equals(t.Tag, FormatStylesEditorWindow.OptionLabelTag));

        private static double UnconstrainedWidth(TextBlock label)
        {
            var probe = new TextBlock
            {
                Text = label.Text,
                FontFamily = label.FontFamily,
                FontSize = label.FontSize,
                FontWeight = label.FontWeight,
            };
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return probe.DesiredSize.Width;
        }

        [StaFact]
        public async Task No_label_is_clipped_at_the_default_size_and_bool_text_toggles()
        {
            var schema = SchemaJson();
            var vm = await LoadedAsync(schema);
            var window = NewWindow(vm);
            try
            {
                var pages = FormatStylesSchemaModel.Parse(schema).FlatGroups.Where(g => g.Settings.Count > 0).ToList();
                Assert.Equal(14, pages.Count);
                var layoutRoot = LayoutRoot(window);

                foreach (var page in pages)
                {
                    ShowPage(window, layoutRoot, page);
                    var host = Host(window);
                    var labels = Labels(host).ToList();
                    Assert.NotEmpty(labels);

                    foreach (var label in labels)
                    {
                        for (DependencyObject? p = LogicalTreeHelper.GetParent(label); p != null && p != host; p = LogicalTreeHelper.GetParent(p))
                            Assert.False(p is StackPanel sp && sp.Orientation == Orientation.Horizontal,
                                $"'{label.Text}' on {page.DisplayName} sits in a horizontal StackPanel");
                        Assert.Equal(TextTrimming.None, label.TextTrimming);

                        var lineHeight = label.FontSize * label.FontFamily.LineSpacing;
                        var fits = UnconstrainedWidth(label) <= label.ActualWidth + 0.5;
                        var wraps = label.ActualHeight > lineHeight * 1.5;
                        Assert.True(fits || wraps,
                            $"'{label.Text}' on {page.DisplayName} is clipped: needs {UnconstrainedWidth(label):F0}px, has {label.ActualWidth:F0}px");
                    }

                    foreach (var setting in page.Settings.Where(s => FormatStylesSchemaModel.ControlKindFor(s) == FormatStylesSchemaModel.ControlKind.CheckBox))
                    {
                        var box = Descendants<CheckBox>(host).FirstOrDefault(c => c.Content is TextBlock t && t.Text == setting.DisplayName);
                        Assert.True(box != null, $"'{setting.DisplayName}' on {page.DisplayName}: its checkbox does not carry the label");
                    }
                }
            }
            finally
            {
                CloseWithoutPrompt(window);
            }
        }
    }
}
