#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Formatting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 033 (T008) — headless view-model coverage for load-on-select, dirty tracking,
    /// merge-save, and the read-only guard, via <see cref="FakeRpcClientAccessor"/>.
    /// The load-on-select/save tests load no schema (the overlay path works without one);
    /// the live-preview-pipeline tests at the bottom cover the static schema cache's
    /// cache-hit path, failure surfacing, supersession, and active-style auto-select at
    /// open — priming/clearing the static cache via
    /// <see cref="FormatStylesEditorViewModel.SetCachedSchemaForTests"/> to stay hermetic.
    /// Runs under an isolated AKML_APP_DATA_ROOT: the LoadAsync path reads/writes the
    /// shell-owned <c>Formatter.ActiveProfile</c> config.
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class FormatStylesEditorViewModelTests : AppDataIsolatedTest
    {
        public FormatStylesEditorViewModelTests() : base("akmlsql-stylevm-test-") { }

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

        // -----------------------------------------------------------------
        // Live-preview pipeline (spec 033 review fixes): the pane must never sit
        // silent on its placeholder — the static-cache path seeds + queues, and
        // every failure surface (not connected / timeout / error) is visible.
        // -----------------------------------------------------------------

        /// <summary>Minimal schema: two settings whose defaults the seed path must surface.</summary>
        private const string TestSchemaJson = "{\n" +
            "  \"settings\": [\n" +
            "    { \"id\": \"casing.reservedKeywords\", \"default\": \"UPPERCASE\" },\n" +
            "    { \"id\": \"whitespace.tabSize\", \"default\": 4 }\n" +
            "  ]\n" +
            "}";

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

        /// <summary>
        /// Awaits the fire-and-forget debounced preview pipeline (100 ms debounce + IPC) by
        /// polling for its observable effect. The fake's Requests list is written from the
        /// pipeline's background task, so a torn mid-Add enumeration just retries.
        /// </summary>
        private static async Task WaitForAsync(Func<bool> condition, string what)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (true)
            {
                bool done;
                try { done = condition(); }
                catch { done = false; }
                if (done) return;
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Timed out waiting for {what}.");
                await Task.Delay(20);
            }
        }

        [Fact]
        public async Task Cache_hit_seeds_working_values_and_queues_an_initial_preview()
        {
            // Second-and-later open in a session: the engine short-circuits (Cached=true) and
            // the static schema cache — primed as a previous open would have left it — is the
            // source. The VM is fresh, so nothing else could seed its working values.
            FormatStylesEditorViewModel.SetCachedSchemaForTests(7, TestSchemaJson);
            try
            {
                var fake = new FakeRpcClientAccessor();
                fake.Respond(MessageTypes.ProfileList, ListOf());
                fake.Respond(MessageTypes.RequestStyleEditorSchema,
                    new StyleEditorSchemaResponse { Cached = true, SchemaVersion = 7 });
                fake.Respond(MessageTypes.FormatPreview, new FormatPreviewResponse { FormattedText = "SELECT 1" });
                var vm = Vm(fake);

                await vm.LoadAsync();

                Assert.Null(vm.LastError);
                Assert.Equal(TestSchemaJson, vm.SchemaJson);
                Assert.Equal("UPPERCASE", vm.GetWorkingValue("casing.reservedKeywords"));
                Assert.Equal(4, vm.GetWorkingValue("whitespace.tabSize"));

                // The cache was consulted: the shell advertised its version to the engine.
                var schemaRequest = fake.Requests
                    .Where(r => r.MessageType == MessageTypes.RequestStyleEditorSchema)
                    .Select(r => r.Payload).OfType<StyleEditorSchemaRequest>().Single();
                Assert.Equal((int?)7, schemaRequest.ClientSchemaVersion);

                await WaitForAsync(() => fake.Requests.Any(r => r.MessageType == MessageTypes.FormatPreview),
                    "the initial preview request");
                await WaitForAsync(() => vm.PreviewText == "SELECT 1", "the preview text");
                Assert.Null(vm.PreviewValidationError);
            }
            finally
            {
                FormatStylesEditorViewModel.SetCachedSchemaForTests(null, null);
            }
        }

        [Fact]
        public async Task Faulting_preview_request_surfaces_a_warning_and_an_inline_message()
        {
            var sink = new CollectingSink();
            var priorLogger = Log.Logger;
            Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
            try
            {
                var fake = new FakeRpcClientAccessor();
                fake.Throw(MessageTypes.FormatPreview,
                    new InvalidOperationException("Engine error 42: pipeline exploded"));
                var vm = Vm(fake);

                vm.QueuePreviewAsync();

                await WaitForAsync(() => vm.PreviewValidationError != null, "the preview failure to surface");
                Assert.Contains("failed", vm.PreviewValidationError, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("pipeline exploded", vm.PreviewValidationError);
            }
            finally
            {
                Log.Logger = priorLogger;
            }

            // Assert on a locked snapshot after the logger is restored (the AiAgentHealthTests
            // idiom): the failure must reach the log at Warning, not vanish at Debug.
            List<LogEvent> captured;
            lock (sink.Events) captured = sink.Events.ToList();
            Assert.Contains(captured, e =>
                e.Level == LogEventLevel.Warning &&
                e.Exception is InvalidOperationException &&
                e.Exception.Message.Contains("pipeline exploded"));
        }

        [Fact]
        public async Task Validation_error_with_empty_formatted_text_is_applied()
        {
            // Stage-6 rejection shape: the engine answers with a validation error and no
            // formatted text. Dropping it was the silent-placeholder bug.
            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.FormatPreview,
                new FormatPreviewResponse { FormattedText = "", ValidationError = "missing semicolon" });
            var vm = Vm(fake);

            vm.QueuePreviewAsync();

            await WaitForAsync(() => vm.PreviewValidationError != null, "the validation error");
            Assert.Equal("missing semicolon", vm.PreviewValidationError);
            Assert.Contains("preview unavailable", vm.PreviewText); // placeholder, not stale text
        }

        [Fact]
        public async Task Rpc_timeout_cancellation_surfaces_a_timed_out_message()
        {
            // PipeRpcClient cancels its response TCS when the request timeout fires — an OCE
            // that is NOT the VM's debounce token. The fake reproduces that shape at the
            // accessor seam (faking the real PipeRpcClient timeout needs a live pipe).
            var fake = new FakeRpcClientAccessor();
            fake.Throw(MessageTypes.FormatPreview, new OperationCanceledException());
            var vm = Vm(fake);

            vm.QueuePreviewAsync();

            await WaitForAsync(() => vm.PreviewValidationError != null, "the timeout to surface");
            Assert.Contains("timed out", vm.PreviewValidationError, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Superseded_preview_requests_stay_silent()
        {
            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.FormatPreview, new FormatPreviewResponse { FormattedText = "SELECT 1" });
            var vm = Vm(fake);

            vm.QueuePreviewAsync();
            vm.QueuePreviewAsync(); // supersedes the first inside its 100 ms debounce window

            await WaitForAsync(() => vm.PreviewText == "SELECT 1", "the surviving preview");
            await Task.Delay(200); // a stray failure from the superseded request would have landed
            Assert.Null(vm.PreviewValidationError);
            Assert.Single(fake.Requests, r => r.MessageType == MessageTypes.FormatPreview);
        }

        [Fact]
        public async Task LoadAsync_selects_the_active_style_and_queues_an_initial_preview()
        {
            SetActiveProfileInConfig("Team Standard");
            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.ProfileList, ListOf(("Khamis Style", true), ("Team Standard", false)));
            fake.Respond<ProfileGetRequest>(MessageTypes.ProfileGet, req =>
                new ProfileGetResponse { Success = true, Name = req.Name, ProfileJson = StoredJson, IsBuiltIn = false });
            fake.Respond(MessageTypes.RequestStyleEditorSchema,
                new StyleEditorSchemaResponse { SchemaVersion = 1, SchemaJson = TestSchemaJson });
            fake.Respond(MessageTypes.FormatPreview, new FormatPreviewResponse { FormattedText = "SELECT 1" });
            var vm = Vm(fake);
            try
            {
                await vm.LoadAsync();

                Assert.Null(vm.LastError);
                Assert.Equal("Team Standard", vm.SelectedProfileName);
                Assert.Equal("Team Standard", vm.LoadedProfileName);
                Assert.False(vm.IsDirty); // opening is not an edit
                // Schema defaults seeded, then the style's stored values overlaid them.
                Assert.Equal("lowercase", vm.GetWorkingValue("casing.reservedKeywords"));
                Assert.Equal(2, vm.GetWorkingValue("whitespace.tabSize"));

                await WaitForAsync(() => fake.Requests.Any(r => r.MessageType == MessageTypes.FormatPreview),
                    "the initial preview request");
                // The schema-load queue was superseded by the selection queue — one request.
                Assert.Single(fake.Requests, r => r.MessageType == MessageTypes.FormatPreview);
            }
            finally
            {
                FormatStylesEditorViewModel.SetCachedSchemaForTests(null, null); // the cold load populated the static cache
            }
        }

        private sealed class CollectingSink : ILogEventSink
        {
            public List<LogEvent> Events { get; } = new List<LogEvent>();
            public void Emit(LogEvent logEvent)
            {
                lock (Events) Events.Add(logEvent);
            }
        }
    }
}
