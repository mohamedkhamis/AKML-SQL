#nullable enable
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using AkmlSql.Shell.Shared.Formatting;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T187, T190 — STY-10, STY-11) — the shell side of team styles and Format SQL
    /// actions: the style list's TEAM STYLES group and read-only team styles in the Format Styles
    /// view model, and the Team style folder / "When you run Format SQL, AKML SQL will:" rows on
    /// Options › Format › Styles.
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class TeamStylesAndFormatSqlActionsTests : AppDataIsolatedTest
    {
        public TeamStylesAndFormatSqlActionsTests() : base("akmlsql-teamstyles-test-") { }

        private const string Refusal = "'Shared' is a team style and can't be changed here \u2014 copy it to edit.";

        private static async Task<(FormatStylesEditorViewModel Vm, FakeRpcClientAccessor Fake)> LoadedAsync(bool teamUnavailable = false)
        {
            var settings = ConfigManager.Load();
            settings.Formatter.ActiveProfile = "Mine";
            settings.Formatter.TeamStyleFolder = @"\\server\share\styles";
            ConfigManager.Save(settings);

            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.ProfileList, new ProfileListResponse
            {
                Profiles = new[]
                {
                    new ProfileInfo { Name = "Default", IsBuiltIn = true, Source = "builtIn" },
                    new ProfileInfo { Name = "Mine", Source = "user" },
                    new ProfileInfo { Name = "Shared", Source = "team", IsReadOnly = true },
                    new ProfileInfo { Name = "Open Team", Source = "team", IsReadOnly = false },
                },
                TeamFolderUnavailable = teamUnavailable,
            });
            fake.Respond(MessageTypes.RequestStyleEditorSchema, new StyleEditorSchemaResponse { SchemaVersion = 0 });
            fake.Respond<ProfileGetRequest>(MessageTypes.ProfileGet, r => new ProfileGetResponse
            {
                Success = true,
                Name = r.Name,
                ProfileJson = "{\"metadata\":{\"name\":\"" + r.Name + "\"}}",
            });
            fake.Respond(MessageTypes.FormatPreview, new FormatPreviewResponse { FormattedText = "SELECT 1;" });
            fake.Respond(MessageTypes.ProfileSave, new ProfileSaveResponse { Success = true });

            FormatStylesEditorViewModel.SetCachedSchemaForTests(null, null);
            var vm = new FormatStylesEditorViewModel(fake) { MainThreadSwitchOverride = () => Task.CompletedTask };
            await vm.LoadAsync();
            return (vm, fake);
        }

        [Fact]
        public async Task Team_styles_are_their_own_group_between_your_styles_and_built_in_ones()
        {
            var (vm, _) = await LoadedAsync();

            var shared = vm.Profiles.Single(p => p.Name == "Shared");
            var open = vm.Profiles.Single(p => p.Name == "Open Team");
            var mine = vm.Profiles.Single(p => p.Name == "Mine");
            var builtIn = vm.Profiles.Single(p => p.Name == "Default");

            Assert.Equal("Team styles", shared.Section);
            Assert.True(shared.IsTeam);
            Assert.True(shared.IsReadOnly);
            Assert.False(shared.IsShipped);
            Assert.False(open.IsReadOnly);
            Assert.Equal("Your styles", mine.Section);
            Assert.Equal("Built-in styles", builtIn.Section);
            Assert.True(mine.SectionOrder < shared.SectionOrder && shared.SectionOrder < builtIn.SectionOrder);
            Assert.False(vm.TeamFolderUnavailable);
        }

        [Fact]
        public async Task An_unreachable_team_folder_is_reported_with_the_folder_name()
        {
            var (vm, _) = await LoadedAsync(teamUnavailable: true);

            Assert.True(vm.TeamFolderUnavailable);
            Assert.Equal(@"\\server\share\styles", vm.TeamStyleFolder);
            Assert.Equal("Team styles unavailable \u2014 " + @"\\server\share\styles" + " can't be reached",
                FormatStylesEditorViewModel.TeamFolderUnavailableText(vm.TeamStyleFolder));
        }

        [Fact]
        public async Task A_read_only_team_style_loads_but_save_rename_and_delete_are_refused()
        {
            var (vm, fake) = await LoadedAsync();

            Assert.True(await vm.SelectProfileAsync("Shared"));
            Assert.True(vm.IsSelectedReadOnly);

            Assert.False(await vm.SaveAsync());
            Assert.Equal(Refusal, vm.LastError);

            Assert.Null(await vm.RenameSelectedAsync("Renamed"));
            Assert.Equal(Refusal, vm.LastError);

            Assert.False(await vm.DeleteSelectedAsync());
            Assert.Equal(Refusal, vm.LastError);

            // None of them reached the engine.
            Assert.DoesNotContain(fake.Requests, r => r.MessageType == MessageTypes.ProfileSave
                                                      || r.MessageType == MessageTypes.ProfileRename
                                                      || r.MessageType == MessageTypes.ProfileDelete);
        }

        [Fact]
        public async Task A_writable_team_style_is_not_read_only()
        {
            var (vm, _) = await LoadedAsync();

            Assert.True(await vm.SelectProfileAsync("Open Team"));

            Assert.False(vm.IsSelectedReadOnly);
        }

        // ------------------------------------------------------------------ Options rows

        private static (SettingsWindow Dialog, FormatSqlRows Rows) BuildRows(AppSettings settings)
        {
            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();
            var controlsByKey = (IDictionary)typeof(SettingsWindow)
                .GetField("_pageControlsByKey", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(dialog)!;
            var rows = ((FormattingControls)controlsByKey["Formatting"]!).AttachedFormatSqlRows;
            Assert.NotNull(rows);
            return (dialog, rows!);
        }

        [StaFact]
        public void The_format_sql_rows_load_the_saved_choices()
        {
            var settings = new AppSettings();
            settings.Formatter.TeamStyleFolder = @"C:\Team\Styles";
            settings.Formatter.FormatSqlActions = new FormatSqlActions
            {
                ApplyLayout = false,
                ApplyCasing = true,
                Semicolons = "remove",
                SquareBrackets = "add",
                ExpandWildcards = true,
                QualifyObjectNames = false,
            };

            var (_, rows) = BuildRows(settings);

            Assert.Equal(@"C:\Team\Styles", rows.TeamFolderBox.Text);
            Assert.False(rows.ApplyLayout.IsChecked);
            Assert.True(rows.ApplyCasing.IsChecked);
            Assert.Equal("Remove", rows.Semicolons.SelectedItem);
            Assert.Equal("Add", rows.SquareBrackets.SelectedItem);
            Assert.True(rows.ExpandWildcards.IsChecked);
            Assert.False(rows.QualifyObjectNames.IsChecked);
        }

        [StaFact]
        public void The_format_sql_rows_save_what_is_chosen()
        {
            var (dialog, rows) = BuildRows(new AppSettings());

            rows.TeamFolderBox.Text = @"  \\server\share\team\..\styles  ";
            rows.ApplyCasing.IsChecked = false;
            rows.Semicolons.SelectedItem = "Insert";
            rows.SquareBrackets.SelectedItem = "Remove";
            rows.QualifyObjectNames.IsChecked = true;

            var saved = dialog.GetSettings();

            Assert.Equal(@"\\server\share\styles", saved.Formatter.TeamStyleFolder);
            Assert.True(saved.Formatter.FormatSqlActions.ApplyLayout);
            Assert.False(saved.Formatter.FormatSqlActions.ApplyCasing);
            Assert.Equal("insert", saved.Formatter.FormatSqlActions.Semicolons);
            Assert.Equal("remove", saved.Formatter.FormatSqlActions.SquareBrackets);
            Assert.False(saved.Formatter.FormatSqlActions.ExpandWildcards);
            Assert.True(saved.Formatter.FormatSqlActions.QualifyObjectNames);
        }

        [StaFact]
        public void The_defaults_leave_semicolons_and_brackets_to_the_style()
        {
            var (dialog, rows) = BuildRows(new AppSettings());

            Assert.Equal("As the style says", rows.Semicolons.SelectedItem);
            Assert.Equal("As the style says", rows.SquareBrackets.SelectedItem);
            Assert.True(rows.ApplyLayout.IsChecked);
            Assert.True(rows.ApplyCasing.IsChecked);

            var saved = dialog.GetSettings();
            Assert.Equal("style", saved.Formatter.FormatSqlActions.Semicolons);
            Assert.Equal("style", saved.Formatter.FormatSqlActions.SquareBrackets);
            Assert.Equal(string.Empty, saved.Formatter.TeamStyleFolder);
        }

        [StaFact]
        public void A_relative_team_folder_is_flagged_and_the_last_good_one_is_kept()
        {
            var settings = new AppSettings();
            settings.Formatter.TeamStyleFolder = @"C:\Team\Styles";
            var (dialog, rows) = BuildRows(settings);

            rows.TeamFolderBox.Text = @"team\styles";
            Assert.Null(rows.ValidateTeamFolder());
            Assert.Equal(Visibility.Visible, rows.TeamFolderError.Visibility);
            Assert.False(string.IsNullOrWhiteSpace(rows.TeamFolderError.Text));

            Assert.Equal(@"C:\Team\Styles", dialog.GetSettings().Formatter.TeamStyleFolder);

            rows.TeamFolderBox.Text = @"D:\Styles";
            Assert.Equal(@"D:\Styles", rows.ValidateTeamFolder());
            Assert.Equal(Visibility.Collapsed, rows.TeamFolderError.Visibility);
        }
    }
}
