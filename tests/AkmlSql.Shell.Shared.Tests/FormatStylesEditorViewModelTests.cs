#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Formatting;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 033 (T008) — headless view-model coverage for load-on-select, dirty tracking,
    /// merge-save, and the read-only guard, via <see cref="FakeRpcClientAccessor"/>.
    /// No schema is loaded in these tests (the static schema cache must stay untouched);
    /// the overlay path works without it.
    /// </summary>
    public class FormatStylesEditorViewModelTests
    {
        private const string StoredJson = "{\n" +
            "  \"metadata\": { \"name\": \"Team Standard\", \"id\": \"id-1\" },\n" +
            "  \"casing\": { \"reservedKeywords\": \"lowercase\" },\n" +
            "  \"whitespace\": { \"tabSize\": 2, \"futureKey\": \"kept\" }\n" +
            "}";

        private static FakeRpcClientAccessor FakeWithProfile(string name, string json, bool isBuiltIn = false)
        {
            var fake = new FakeRpcClientAccessor();
            fake.Respond<ProfileGetRequest>(MessageTypes.ProfileGet, req =>
                string.Equals(req.Name, name, StringComparison.OrdinalIgnoreCase)
                    ? new ProfileGetResponse { Success = true, Name = req.Name, ProfileJson = json, IsBuiltIn = isBuiltIn }
                    : new ProfileGetResponse { Success = false, ErrorMessage = $"Profile '{req.Name}' was not found." });
            return fake;
        }

        [Fact]
        public async Task SelectProfileAsync_loads_stored_values_not_defaults()
        {
            var fake = FakeWithProfile("Team Standard", StoredJson);
            var vm = new FormatStylesEditorViewModel(fake);

            var ok = await vm.SelectProfileAsync("Team Standard");

            Assert.True(ok);
            Assert.Equal("Team Standard", vm.SelectedProfileName);
            Assert.Equal("Team Standard", vm.LoadedProfileName);
            Assert.Equal(StoredJson, vm.LoadedProfileJson);
            Assert.Equal("lowercase", vm.GetWorkingValue("casing.reservedKeywords"));
            Assert.Equal(2, vm.GetWorkingValue("whitespace.tabSize"));
            Assert.False(vm.IsDirty);
            Assert.False(vm.IsSelectedReadOnly);
        }

        [Fact]
        public async Task SetWorkingValue_after_load_marks_dirty()
        {
            var vm = new FormatStylesEditorViewModel(FakeWithProfile("Team Standard", StoredJson));
            await vm.SelectProfileAsync("Team Standard");

            vm.SetWorkingValue("casing.reservedKeywords", "UPPERCASE");

            Assert.True(vm.IsDirty);
        }

        [Fact]
        public async Task SaveAsync_sends_merged_json_with_metadata_and_clears_dirty()
        {
            var fake = FakeWithProfile("Team Standard", StoredJson);
            fake.Respond(MessageTypes.ProfileSave, new ProfileSaveResponse { Success = true });
            var vm = new FormatStylesEditorViewModel(fake);
            await vm.SelectProfileAsync("Team Standard");
            vm.SetWorkingValue("casing.reservedKeywords", "UPPERCASE");

            var ok = await vm.SaveAsync();

            Assert.True(ok);
            Assert.False(vm.IsDirty);

            var saveRequest = fake.Requests
                .Where(r => r.MessageType == MessageTypes.ProfileSave)
                .Select(r => r.Payload)
                .OfType<ProfileSaveRequest>()
                .Single();
            Assert.Equal("Team Standard", saveRequest.Name);

            var root = JsonDocument.Parse(saveRequest.ProfileJson!).RootElement;
            Assert.Equal("Team Standard", root.GetProperty("metadata").GetProperty("name").GetString());
            Assert.Equal("UPPERCASE", root.GetProperty("casing").GetProperty("reservedKeywords").GetString());
            Assert.Equal("kept", root.GetProperty("whitespace").GetProperty("futureKey").GetString());

            // The merged text becomes the new merge base.
            Assert.Equal(saveRequest.ProfileJson, vm.LoadedProfileJson);
        }

        [Fact]
        public async Task Builtin_style_is_read_only_and_save_is_refused_without_ipc()
        {
            var fake = FakeWithProfile("Default", StoredJson, isBuiltIn: true);
            var vm = new FormatStylesEditorViewModel(fake);
            await vm.SelectProfileAsync("Default");

            Assert.True(vm.IsSelectedReadOnly);

            var ok = await vm.SaveAsync();

            Assert.False(ok);
            Assert.Contains("read-only", vm.LastError, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(fake.Requests, r => r.MessageType == MessageTypes.ProfileSave);
        }

        [Fact]
        public async Task Failed_load_clears_selection_and_never_masquerades()
        {
            var fake = FakeWithProfile("Exists", StoredJson);
            var vm = new FormatStylesEditorViewModel(fake);

            var ok = await vm.SelectProfileAsync("Deleted Elsewhere");

            Assert.False(ok);
            Assert.Null(vm.SelectedProfileName);
            Assert.Null(vm.LoadedProfileName);
            Assert.Null(vm.LoadedProfileJson);
            Assert.Contains("was not found", vm.LastError);
        }

        [Fact]
        public async Task Disconnected_engine_fails_softly_with_no_requests()
        {
            var fake = new FakeRpcClientAccessor { IsConnected = false };
            var vm = new FormatStylesEditorViewModel(fake);

            var ok = await vm.SelectProfileAsync("Anything");

            Assert.False(ok);
            Assert.Equal("Engine not connected.", vm.LastError);
            Assert.Empty(fake.Requests);
        }

        [Fact]
        public async Task Dirty_switch_with_cancel_keeps_current_style()
        {
            var fake = FakeWithProfile("A", StoredJson);
            var vm = new FormatStylesEditorViewModel(fake)
            {
                DirtyDecisionHandler = () => Task.FromResult(StyleSwitchDecision.Cancel),
            };
            await vm.SelectProfileAsync("A");
            vm.SetWorkingValue("whitespace.tabSize", 8);

            var ok = await vm.SelectProfileAsync("B");

            Assert.False(ok);
            Assert.Equal("A", vm.LoadedProfileName);
            Assert.True(vm.IsDirty); // edits kept
            Assert.Single(fake.Requests, r => r.MessageType == MessageTypes.ProfileGet); // no fetch for B
        }

        [Fact]
        public async Task Dirty_switch_with_save_persists_then_loads_next()
        {
            var fake = new FakeRpcClientAccessor();
            fake.Respond<ProfileGetRequest>(MessageTypes.ProfileGet, req =>
                new ProfileGetResponse { Success = true, Name = req.Name, ProfileJson = StoredJson, IsBuiltIn = false });
            fake.Respond(MessageTypes.ProfileSave, new ProfileSaveResponse { Success = true });

            var vm = new FormatStylesEditorViewModel(fake)
            {
                DirtyDecisionHandler = () => Task.FromResult(StyleSwitchDecision.Save),
            };
            await vm.SelectProfileAsync("A");
            vm.SetWorkingValue("whitespace.tabSize", 8);

            var ok = await vm.SelectProfileAsync("B");

            Assert.True(ok);
            Assert.Equal("B", vm.LoadedProfileName);
            Assert.False(vm.IsDirty);
            Assert.Contains(fake.Requests, r => r.MessageType == MessageTypes.ProfileSave);
            Assert.Equal(2, fake.Requests.Count(r => r.MessageType == MessageTypes.ProfileGet));
        }
    }
}
