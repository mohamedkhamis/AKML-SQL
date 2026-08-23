#nullable enable
using System;
using System.IO;
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
    /// Spec 033 (T030) — style lifecycle flows via the fake IPC seam under an isolated
    /// AKML_APP_DATA_ROOT: renaming the ACTIVE style must follow the shell-owned
    /// <c>Formatter.ActiveProfile</c> pointer; deleting the active or a built-in style is
    /// refused shell-side before any IPC; New-based-on forwards the chosen base; the ✔
    /// (IsActive) flag is recomputed from config at list load.
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class FormatStylesLifecycleTests : AppDataIsolatedTest
    {
        public FormatStylesLifecycleTests() : base("akmlsql-stylelifecycle-test-") { }

        /// <summary>VM with the headless main-thread-switch no-op (no VS JoinableTaskContext here).</summary>
        private static FormatStylesEditorViewModel Vm(FakeRpcClientAccessor fake) =>
            new FormatStylesEditorViewModel(fake) { MainThreadSwitchOverride = () => Task.CompletedTask };

        private static void SetActiveProfileInConfig(string name)
        {
            var settings = ConfigManager.Load();
            settings.Formatter.ActiveProfile = name;
            ConfigManager.Save(settings);
        }

        private static ProfileListResponse ListOf(params (string Name, bool BuiltIn)[] profiles) =>
            new ProfileListResponse
            {
                Profiles = profiles
                    .Select(p => new ProfileInfo { Name = p.Name, IsBuiltIn = p.BuiltIn })
                    .ToArray(),
            };

        [Fact]
        public async Task Renaming_the_active_style_updates_the_config_pointer()
        {
            SetActiveProfileInConfig("Team Standard");

            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.ProfileRename,
                new ProfileRenameResponse { Success = true, NewName = "Team Standard v2" });
            fake.Respond(MessageTypes.ProfileList, ListOf(("Team Standard v2", false)));

            var vm = Vm(fake);
            vm.SelectedProfileName = "Team Standard";

            var final = await vm.RenameSelectedAsync("Team Standard v2");

            Assert.Equal("Team Standard v2", final);
            Assert.Equal("Team Standard v2", ConfigManager.Load().Formatter.ActiveProfile);
            Assert.Equal("Team Standard v2", vm.SelectedProfileName);
        }

        [Fact]
        public async Task Renaming_a_non_active_style_leaves_the_config_pointer_alone()
        {
            SetActiveProfileInConfig("Khamis Style");

            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.ProfileRename, new ProfileRenameResponse { Success = true, NewName = "Other v2" });
            fake.Respond(MessageTypes.ProfileList, ListOf(("Other v2", false), ("Khamis Style", false)));

            var vm = Vm(fake);
            vm.SelectedProfileName = "Other";
            await vm.RenameSelectedAsync("Other v2");

            Assert.Equal("Khamis Style", ConfigManager.Load().Formatter.ActiveProfile);
        }

        [Fact]
        public async Task Deleting_the_active_style_is_refused_before_any_ipc()
        {
            SetActiveProfileInConfig("Team Standard");

            var fake = new FakeRpcClientAccessor();
            var vm = Vm(fake);
            vm.SelectedProfileName = "Team Standard";

            var ok = await vm.DeleteSelectedAsync();

            Assert.False(ok);
            Assert.Contains("active", vm.LastError, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(fake.Requests, r => r.MessageType == MessageTypes.ProfileDelete);
        }

        [Fact]
        public async Task Deleting_a_builtin_is_refused_before_any_ipc()
        {
            SetActiveProfileInConfig("Khamis Style");

            var fake = new FakeRpcClientAccessor();
            var vm = Vm(fake);
            vm.SelectedProfileName = "Default";
            vm.Profiles.Add(new StyleListItem { Name = "Default", IsReadOnly = true });

            var ok = await vm.DeleteSelectedAsync();

            Assert.False(ok);
            Assert.Contains("Built-in", vm.LastError);
            Assert.Empty(fake.Requests);
        }

        [Fact]
        public async Task Delete_succeeds_for_inactive_custom_style()
        {
            SetActiveProfileInConfig("Khamis Style");

            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.ProfileDelete, new ProfileDeleteResponse { Success = true });
            fake.Respond(MessageTypes.ProfileList, ListOf(("Khamis Style", false)));

            var vm = Vm(fake);
            vm.SelectedProfileName = "Old Style";

            var ok = await vm.DeleteSelectedAsync();

            Assert.True(ok);
            var deleteRequest = fake.Requests
                .Where(r => r.MessageType == MessageTypes.ProfileDelete)
                .Select(r => r.Payload).OfType<ProfileDeleteRequest>().Single();
            Assert.Equal("Old Style", deleteRequest.Name);
        }

        [Fact]
        public async Task CreateStyle_forwards_the_chosen_base()
        {
            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.DuplicateProfile,
                new DuplicateProfileResponse { Success = true, NewName = "Mine" });
            fake.Respond(MessageTypes.ProfileList, ListOf(("Mine", false)));

            var vm = Vm(fake);

            var created = await vm.CreateStyleAsync("Mine", "Khamis Style");

            Assert.Equal("Mine", created);
            var dupRequest = fake.Requests
                .Where(r => r.MessageType == MessageTypes.DuplicateProfile)
                .Select(r => r.Payload).OfType<DuplicateProfileRequest>().Single();
            Assert.Equal("Khamis Style", dupRequest.SourceName);
            Assert.Equal("Mine", dupRequest.NewName);
        }

        [Fact]
        public async Task IsActive_flag_follows_config_at_list_load()
        {
            SetActiveProfileInConfig("Beta");

            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.ProfileList, ListOf(("Alpha", false), ("Beta", false), ("Default", true)));

            var vm = Vm(fake);
            await vm.RefreshProfilesAsync();

            Assert.False(vm.Profiles.Single(p => p.Name == "Alpha").IsActive);
            Assert.True(vm.Profiles.Single(p => p.Name == "Beta").IsActive);
            Assert.Equal("Your styles", vm.Profiles.Single(p => p.Name == "Beta").Section);
            Assert.Equal("Built-in styles", vm.Profiles.Single(p => p.Name == "Default").Section);
        }
    }
}
