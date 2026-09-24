#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Serilog;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Spec 020 US3 (T058) — view-model driving the Format Styles editor.
    /// Loads the canonical setting schema and the list of available profiles via IPC,
    /// surfaces them to the WPF view, and tracks the user's current selection.
    ///
    /// <para>
    /// The schema is fetched once via <c>RequestStyleEditorSchema</c> (msg 28); subsequent
    /// opens of the editor short-circuit when <see cref="CachedSchemaVersion"/> matches
    /// what the engine reports. The profile list is fetched via <c>ProfileList</c> (msg 14).
    /// </para>
    ///
    /// <para>
    /// Spec 033 promoted this from a browser to a full editor: <see cref="SelectProfileAsync"/>
    /// loads a style's stored values (raw ProfileGet text as the merge base),
    /// <see cref="SaveAsync"/> persists via <see cref="ProfileJsonMerger"/> + ProfileSave, and
    /// the lifecycle operations (rename/delete/create) keep the shell-owned
    /// <c>Formatter.ActiveProfile</c> pointer consistent.
    /// </para>
    /// </summary>
    internal sealed class FormatStylesEditorViewModel : INotifyPropertyChanged
    {
        private static int? _cachedSchemaVersion;
        private static string? _cachedSchemaJson;

        /// <summary>
        /// Test seam: primes or clears the static schema cache. The cache deliberately outlives
        /// the view-model (second-and-later editor opens short-circuit the schema IPC), so tests
        /// need deterministic control over it.
        /// </summary>
        internal static void SetCachedSchemaForTests(int? version, string? json)
        {
            _cachedSchemaVersion = version;
            _cachedSchemaJson = json;
        }

        /// <summary>
        /// Spec 033 (T002) — all engine IPC goes through this seam so tests can inject a fake.
        /// The default resolves <c>EngineLifecycle.Manager?.Client</c> at call time, preserving
        /// the pre-seam late-binding semantics.
        /// </summary>
        private readonly IRpcClientAccessor _rpc;

        public FormatStylesEditorViewModel() : this(EngineRpcClientAccessor.Instance) { }

        internal FormatStylesEditorViewModel(IRpcClientAccessor rpc)
        {
            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
        }

        /// <summary>
        /// Test seam beside <see cref="IRpcClientAccessor"/>: replaces the ThreadHelper
        /// main-thread switch, which requires a live VS JoinableTaskContext. Null (production)
        /// uses ThreadHelper directly — and fails fast if the switch genuinely breaks.
        /// </summary>
        internal Func<Task>? MainThreadSwitchOverride { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<StyleListItem> Profiles { get; } = new();

        private string? _schemaJson;
        public string? SchemaJson
        {
            get => _schemaJson;
            private set { _schemaJson = value; OnPropertyChanged(); }
        }

        public int? CachedSchemaVersion => _cachedSchemaVersion;

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            private set { _isLoading = value; OnPropertyChanged(); }
        }

        private string? _lastError;
        public string? LastError
        {
            get => _lastError;
            private set { _lastError = value; OnPropertyChanged(); }
        }

        private string? _selectedProfileName;
        public string? SelectedProfileName
        {
            get => _selectedProfileName;
            set { _selectedProfileName = value; OnPropertyChanged(); }
        }

        // -----------------------------------------------------------------
        // Spec 033 (T014/T015): load-on-select + dirty tracking + merge-save
        // -----------------------------------------------------------------

        /// <summary>Raw ProfileGet text for the loaded style — the merge base for Save.</summary>
        private string? _loadedProfileJson;

        /// <summary>Which style the working values belong to (null until a load succeeds).</summary>
        private string? _loadedProfileName;

        /// <summary>Test seam: the current merge base.</summary>
        internal string? LoadedProfileJson => _loadedProfileJson;

        /// <summary>Test seam: the loaded style's name.</summary>
        internal string? LoadedProfileName => _loadedProfileName;

        private bool _isDirty;
        /// <summary>True when a loaded style has unsaved edits; gates the Save button and switch/close prompts.</summary>
        public bool IsDirty
        {
            get => _isDirty;
            private set { if (_isDirty != value) { _isDirty = value; OnPropertyChanged(); } }
        }

        private bool _isSelectedBuiltIn;
        /// <summary>
        /// True when a style shipped with the product under the loaded name — whether or not the
        /// user has since edited it.
        /// <para>
        /// This replaced IsSelectedReadOnly. Built-ins are no longer read-only: editing one writes
        /// a custom file that shadows the shipped one, which is never written to. The flag still
        /// matters, because a shipped name cannot be renamed or deleted (the name is what ties an
        /// override to the original) and it is the only kind of style Reset can act on.
        /// </para>
        /// </summary>
        public bool IsSelectedBuiltIn
        {
            get => _isSelectedBuiltIn;
            private set { if (_isSelectedBuiltIn != value) { _isSelectedBuiltIn = value; OnPropertyChanged(); } }
        }

        private bool _isSelectedCustomized;
        /// <summary>
        /// True when the loaded style is a shipped one the user has edited — the only state in
        /// which Reset has anything to discard. Gates the Reset control.
        /// </summary>
        public bool IsSelectedCustomized
        {
            get => _isSelectedCustomized;
            private set { if (_isSelectedCustomized != value) { _isSelectedCustomized = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Window-provided prompt shown when switching away from (or closing over) unsaved
        /// edits. Null (headless/tests without a handler) behaves as Discard.
        /// </summary>
        public Func<Task<StyleSwitchDecision>>? DirtyDecisionHandler { get; set; }

        private string _previewText = string.Empty;
        public string PreviewText
        {
            get => _previewText;
            private set { _previewText = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Spec 020 T070 — non-null when the engine's stage-6 SemanticValidator rejected the
        /// formatted output. The view renders an inline warning bar above the preview pane.
        /// Also the surface for preview PIPELINE failures (engine not connected, request
        /// timeout, request error) — the pane must never sit silent on its placeholder.
        /// The equality guard rate-limits a failure that repeats on every keystroke:
        /// deterministic messages re-raise no PropertyChanged.
        /// </summary>
        private string? _previewValidationError;
        public string? PreviewValidationError
        {
            get => _previewValidationError;
            private set { if (_previewValidationError != value) { _previewValidationError = value; OnPropertyChanged(); } }
        }

        // -----------------------------------------------------------------
        // Tier 2b: working profile values + debounced preview pipeline
        // -----------------------------------------------------------------

        /// <summary>
        /// User-edited values overlaying the schema defaults. Keys are setting IDs in
        /// <c>"groupId.settingName"</c> form (e.g. <c>"casing.reservedKeywords"</c>); values
        /// are the working setting value (<c>bool</c>, <c>int</c>, or <c>string</c>).
        /// <para>
        /// PR-235 review fix: <see cref="ConcurrentDictionary{TKey,TValue}"/> rather than plain
        /// <c>Dictionary</c> because writes come from the UI thread (control change handlers
        /// calling <see cref="SetWorkingValue"/>) while reads come from a background
        /// <see cref="Task"/> inside <see cref="QueuePreviewAsync"/>
        /// (<see cref="BuildProfileJson"/> enumerates the dict). Plain <c>Dictionary</c> is not
        /// thread-safe under concurrent read/write and would race under rapid editing.
        /// </para>
        /// </summary>
        private readonly ConcurrentDictionary<string, object?> _workingValues = new(StringComparer.Ordinal);

        /// <summary>
        /// Spec 033 — schema default per setting id, captured at seed time. The merge-save uses
        /// it to keep paths absent from the stored file implicit when an edit matches the default.
        /// </summary>
        private readonly ConcurrentDictionary<string, object?> _schemaDefaults = new(StringComparer.Ordinal);

        /// <summary>
        /// The sample SQL the preview pane formats. Spec 020 US5 (T069): persisted at
        /// <c>%AppData%/AKML SQL/editor/preview-sample.sql</c> so user-pasted custom samples
        /// survive editor close/reopen. Setting the property writes atomically (temp + rename).
        /// On view-model construction, the persisted file is loaded if present; otherwise
        /// <see cref="DefaultSampleSql"/> is used.
        /// </summary>
        public string PreviewSample
        {
            get => _previewSample;
            set
            {
                if (string.Equals(_previewSample, value, StringComparison.Ordinal)) return;
                _previewSample = value ?? string.Empty;
                OnPropertyChanged();
                TryPersistPreviewSample(_previewSample);
                QueuePreviewAsync();
            }
        }
        private string _previewSample = LoadPersistedSampleOrDefault();

        /// <summary>
        /// Spec 030 T019 / FR-008 — preview source: the persisted sample, or the SQL from the
        /// editor that was active when the styles editor opened (<see cref="CurrentQueryText"/>).
        /// </summary>
        public FormatPreviewSource PreviewSourceMode
        {
            get => _previewSourceMode;
            set
            {
                if (_previewSourceMode == value) return;
                _previewSourceMode = value;
                OnPropertyChanged();
                QueuePreviewAsync();
            }
        }
        private FormatPreviewSource _previewSourceMode = FormatPreviewSource.Sample;

        /// <summary>The active editor's text captured at editor-open (FR-008). Empty if none.</summary>
        public string CurrentQueryText
        {
            get => _currentQueryText;
            set
            {
                _currentQueryText = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCurrentQuery));
            }
        }
        private string _currentQueryText = string.Empty;

        /// <summary>True when a current-editor query was captured (gates the "Current query" option).</summary>
        public bool HasCurrentQuery => !string.IsNullOrWhiteSpace(_currentQueryText);

        /// <summary>The SQL the preview pane formats given the selected source.</summary>
        private string EffectivePreviewSample =>
            PreviewSourceMode == FormatPreviewSource.CurrentQuery && HasCurrentQuery ? _currentQueryText
            : PreviewSourceMode == FormatPreviewSource.PageSample && !string.IsNullOrWhiteSpace(_pageSample) ? _pageSample!
            : _previewSample;

        /// <summary>
        /// True when the engine serves SQL Prompt's option model: settings are keyed
        /// <c>sqlPrompt.&lt;path&gt;</c>, and preview / save carry the style's SQL Prompt document.
        /// False against an older engine, which serves AKML's own settings schema.
        /// </summary>
        public bool IsSqlPromptModel { get; private set; }

        /// <summary>
        /// True when the loaded style is still written in AKML's own model: the editor shows its SQL
        /// Prompt reading, and saving makes it a SQL Prompt style.
        /// </summary>
        public bool IsSelectedClassic
        {
            get => _isSelectedClassic;
            private set { if (_isSelectedClassic != value) { _isSelectedClassic = value; OnPropertyChanged(); } }
        }
        private bool _isSelectedClassic;

        /// <summary>SQL Prompt model: the current page's preview SQL (what its options act on).</summary>
        public string? PageSample
        {
            get => _pageSample;
            set
            {
                if (string.Equals(_pageSample, value, StringComparison.Ordinal)) return;
                _pageSample = value;
                OnPropertyChanged();
                if (PreviewSourceMode == FormatPreviewSource.PageSample) QueuePreviewAsync();
            }
        }
        private string? _pageSample;

        /// <summary>
        /// The merge base for a SQL Prompt-model edit: the stored file with its <c>sqlPrompt</c>
        /// document replaced by the engine's explicit reading of the style (every option written
        /// out). Editing against explicit values keeps the collapse-threshold rule and the
        /// defaults exact; a classic style gains its document on first save.
        /// </summary>
        internal static string ComposeSqlPromptBase(string profileJson, string sqlPromptJson)
        {
            if (JsonNode.Parse(profileJson) is not JsonObject root) return profileJson;
            root["sqlPrompt"] = JsonNode.Parse(sqlPromptJson);
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        private static string PreviewSamplePath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AKML SQL", "editor");
                return Path.Combine(dir, "preview-sample.sql");
            }
        }

        private static string LoadPersistedSampleOrDefault()
        {
            try
            {
                var path = PreviewSamplePath;
                if (File.Exists(path))
                {
                    var text = File.ReadAllText(path);
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
            }
            catch (Exception ex)
            {
                try { Log.Debug(ex, "FormatStylesEditor: failed to load persisted preview sample"); } catch { }
            }
            return DefaultSampleSql;
        }

        private static void TryPersistPreviewSample(string content)
        {
            try
            {
                var path = PreviewSamplePath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                // Atomic write: temp + rename. .NET Framework 4.7.2 has no overwrite-aware File.Move
                // on this code path, so delete the destination first if it exists.
                var tempPath = path + ".tmp";
                try { File.Delete(tempPath); } catch { }
                File.WriteAllText(tempPath, content);
                try { File.Delete(path); } catch { /* didn't exist */ }
                File.Move(tempPath, path);
            }
            catch (Exception ex)
            {
                try { Log.Debug(ex, "FormatStylesEditor: failed to persist preview sample"); } catch { }
            }
        }

        private CancellationTokenSource? _previewCts;
        private readonly object _previewCtsLock = new();
        private int _previewSequence;

        /// <summary>
        /// Returns the working value for the setting, or <c>null</c> if the user hasn't
        /// edited it (the schema default is the effective value).
        /// </summary>
        public object? GetWorkingValue(string settingId) =>
            _workingValues.TryGetValue(settingId, out var v) ? v : null;

        /// <summary>
        /// Records a user edit and triggers a debounced preview refresh. Marks the loaded
        /// style dirty (browsing edits with no style loaded stay preview-only, never dirty).
        /// </summary>
        public void SetWorkingValue(string settingId, object? value)
        {
            _workingValues[settingId] = value;
            if (_loadedProfileName != null) IsDirty = true;
            QueuePreviewAsync();
        }

        /// <summary>
        /// Initialises <see cref="_workingValues"/> from the schema defaults so a "no edits"
        /// preview matches the engine's default-profile output.
        /// </summary>
        private void SeedWorkingValuesFromSchema(string schemaJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(schemaJson);
                IsSqlPromptModel = doc.RootElement.TryGetProperty("model", out var model)
                                   && model.ValueKind == JsonValueKind.String
                                   && string.Equals(model.GetString(), "sqlPrompt", StringComparison.Ordinal);
                if (!doc.RootElement.TryGetProperty("settings", out var settings)) return;

                foreach (var s in settings.EnumerateArray())
                {
                    var id = s.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    if (string.IsNullOrEmpty(id)) continue;

                    if (s.TryGetProperty("default", out var defEl))
                    {
                        var value = ProfileJsonMerger.ReadScalar(defEl);
                        _workingValues[id!] = value;
                        _schemaDefaults[id!] = value; // spec 033 — merge-save's implicit-default oracle
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "FormatStylesEditor: SeedWorkingValuesFromSchema failed");
            }
        }

        /// <summary>
        /// Builds a <c>FormattingProfile</c>-shaped JSON document from <see cref="_workingValues"/>.
        /// Flat dotted keys become nested JSON paths — nesting by EVERY segment (spec 033 T024:
        /// v2's flattened multi-segment ids like <c>insertStatements.columns.parenthesisStyle</c>
        /// must produce <c>{"insertStatements":{"columns":{...}}}</c>, not a literal
        /// "columns.parenthesisStyle" property).
        /// </summary>
        internal string BuildProfileJson()
        {
            var root = new JsonObject();
            foreach (var kvp in _workingValues)
            {
                var segments = kvp.Key.Split('.');
                if (segments.Length < 2 || segments[0].Length == 0) continue;
                // Shared with the save path — preview and persisted JSON must nest identically.
                ProfileJsonMerger.SetValueAt(root, segments, ProfileJsonMerger.ToJsonValue(kvp.Value));
            }
            return root.ToJsonString();
        }

        /// <summary>Shown in the pane when the engine answered but returned no text to display.</summary>
        private const string PreviewEmptyResponseText = "-- preview unavailable (the engine returned no text).";

        private const string PreviewNotConnectedMessage = "Preview unavailable — the engine is not connected.";
        private const string PreviewTimeoutMessage = "Preview unavailable — the request timed out.";

        /// <summary>
        /// Makes a preview failure visible: logs at Warning and puts a one-line reason in the
        /// pane's warning bar, so the pane never sits silent on the placeholder. A failure from
        /// a superseded request (a newer preview was queued while this one was in flight) is
        /// dropped — the newer request owns the pane.
        /// </summary>
        private void ReportPreviewFailure(int sequence, string reason, string userMessage, Exception? ex = null)
        {
            if (sequence < _previewSequence) return; // superseded
            if (ex != null) Log.Warning(ex, "FormatStylesEditor: preview {Reason}", reason);
            else Log.Warning("FormatStylesEditor: preview {Reason}", reason);
            PreviewValidationError = userMessage;
        }

        /// <summary>
        /// Fire-and-forget request to refresh the preview. 100 ms debounce + supersession via
        /// monotonic sequence ID per <c>contracts/ipc-format-preview-debounce.md</c>.
        /// </summary>
        public void QueuePreviewAsync()
        {
            CancellationToken token;
            int sequence;

            // PR-235 re-review fix: QueuePreviewAsync is called from BOTH the UI thread
            // (SetWorkingValue / PreviewSample setter) and a background Task
            // (LoadSchemaAsync continuation fires QueuePreviewAsync after seeding). Without
            // this lock, two callers could simultaneously read _previewCts, both call Cancel
            // on the same instance, both assign new instances — leaking one CTS and leaving
            // _previewCts holding a token whose request the other caller has already started.
            // The lock guarantees atomic Cancel-then-Replace-then-read-Token + sequence-bump.
            lock (_previewCtsLock)
            {
                _previewCts?.Cancel();
                _previewCts?.Dispose();
                _previewCts = new CancellationTokenSource();
                token = _previewCts.Token;
                sequence = System.Threading.Interlocked.Increment(ref _previewSequence);
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(100, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested) return;
                    if (sequence < _previewSequence) return; // superseded

                    if (!_rpc.IsConnected)
                    {
                        ReportPreviewFailure(sequence, "engine not connected", PreviewNotConnectedMessage);
                        return;
                    }

                    var request = new FormatPreviewRequest
                    {
                        SessionId = "format-styles-editor",
                        SampleText = EffectivePreviewSample,
                        ProfileJson = BuildProfileJson(),
                    };

                    var response = await _rpc.SendRequestAsync<FormatPreviewResponse, FormatPreviewRequest>(
                        MessageTypes.FormatPreview,
                        request,
                        timeoutMs: 2000,
                        token).ConfigureAwait(false);

                    // Discard if a newer request has been queued while we waited
                    if (sequence < _previewSequence) return;

                    if (response != null)
                    {
                        // Always apply: a response can carry ONLY a validation error (empty
                        // FormattedText) — dropping it left the pane stuck on the placeholder.
                        PreviewText = string.IsNullOrEmpty(response.FormattedText)
                            ? PreviewEmptyResponseText
                            : response.FormattedText!;
                        PreviewValidationError = response.ValidationError;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // Our debounce token fired — genuinely superseded, stay silent.
                }
                catch (OperationCanceledException)
                {
                    // NOT our token: PipeRpcClient cancels its response TCS when the 2000 ms
                    // request timeout fires — surface it instead of dying silently.
                    ReportPreviewFailure(sequence, "request timed out", PreviewTimeoutMessage);
                }
                catch (Exception ex)
                {
                    ReportPreviewFailure(sequence, "request failed", $"Preview unavailable — the request failed: {ex.Message}", ex);
                }
            }, token);
        }

        // -----------------------------------------------------------------
        // Spec 033 (T014): load-on-select
        // -----------------------------------------------------------------

        /// <summary>
        /// Guarded selection transition: prompts on unsaved edits (Save / Discard / Cancel),
        /// fetches the style's RAW stored JSON via ProfileGet, and seeds the working values
        /// with schema defaults overlaid by the style's actual values.
        /// Returns false when the transition was cancelled or the load failed — the caller
        /// must then restore the previous visual selection.
        /// </summary>
        public async Task<bool> SelectProfileAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            if (string.Equals(name, _loadedProfileName, StringComparison.OrdinalIgnoreCase))
            {
                SelectedProfileName = name; // reselect of the loaded style — nothing to do
                return true;
            }

            if (IsDirty)
            {
                var decision = DirtyDecisionHandler != null
                    ? await DirtyDecisionHandler().ConfigureAwait(true)
                    : StyleSwitchDecision.Discard;
                if (decision == StyleSwitchDecision.Cancel) return false;
                if (decision == StyleSwitchDecision.Save && !await SaveAsync().ConfigureAwait(true))
                    return false; // save failed — stay on the dirty style, error already surfaced
            }

            if (!_rpc.IsConnected)
            {
                LastError = "Engine not connected.";
                return false;
            }

            try
            {
                var response = await _rpc.SendRequestAsync<ProfileGetResponse, ProfileGetRequest>(
                    MessageTypes.ProfileGet,
                    new ProfileGetRequest { Name = name },
                    timeoutMs: 5000).ConfigureAwait(true);

                if (response == null || !response.Success || string.IsNullOrEmpty(response.ProfileJson))
                {
                    // Never show schema defaults masquerading as the style (spec US1 scenario).
                    LastError = response?.ErrorMessage ?? $"Could not load style '{name}'.";
                    ClearLoadedProfile();
                    return false;
                }

                var baseJson = IsSqlPromptModel && !string.IsNullOrEmpty(response.SqlPromptJson)
                    ? ComposeSqlPromptBase(response.ProfileJson!, response.SqlPromptJson!)
                    : response.ProfileJson!;

                ReseedWorkingValues();
                OverlayProfileValuesFromJson(baseJson);

                _loadedProfileJson = baseJson;
                _loadedProfileName = name;
                IsSelectedClassic = IsSqlPromptModel && !response.IsSqlPromptStyle;
                SelectedProfileName = name;
                // HasBuiltIn, not IsBuiltIn: an edited built-in resolves from the custom directory,
                // so IsBuiltIn is false for it even though it is still a shipped style that cannot
                // be renamed or deleted and can still be reset.
                IsSelectedBuiltIn = response.HasBuiltIn;
                IsSelectedCustomized = response.IsCustomizedBuiltIn;
                IsDirty = false;
                LastError = null;
                QueuePreviewAsync();
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Warning(ex, "FormatStylesEditor: load profile {Name} failed", name);
                ClearLoadedProfile();
                return false;
            }
        }

        private void ClearLoadedProfile()
        {
            _loadedProfileJson = null;
            _loadedProfileName = null;
            SelectedProfileName = null;
            IsSelectedBuiltIn = false;
            IsSelectedCustomized = false;
            IsSelectedClassic = false;
            IsDirty = false;
        }

        /// <summary>
        /// Resets every working value to the schema default, ready for a style's own values to be
        /// overlaid on top. Shared by load and reset so the two cannot drift: a reset that reseeded
        /// differently from a load would leave settings the restored style does not mention showing
        /// whatever the previous style had put there.
        /// </summary>
        private void ReseedWorkingValues()
        {
            _workingValues.Clear();
            if (!_schemaDefaults.IsEmpty)
            {
                // Cheaper than re-parsing the ~180-setting schema JSON on every selection:
                // the defaults captured at seed time ARE the reseed source.
                foreach (var kvp in _schemaDefaults) _workingValues[kvp.Key] = kvp.Value;
            }
            else
            {
                var schema = SchemaJson ?? _cachedSchemaJson;
                if (!string.IsNullOrEmpty(schema)) SeedWorkingValuesFromSchema(schema!);
            }
        }

        /// <summary>
        /// Flattens the style's nested option values over the seeded defaults so the tree,
        /// controls, and preview reflect the style itself. Objects recurse (multi-segment ids
        /// like <c>insertStatements.columns.parenthesisStyle</c>); metadata and non-primitive
        /// leaves are skipped — the merge base keeps whatever the editor doesn't model.
        /// </summary>
        private void OverlayProfileValuesFromJson(string profileJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(profileJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

                foreach (var group in doc.RootElement.EnumerateObject())
                {
                    if (group.Value.ValueKind != JsonValueKind.Object) continue;
                    if (string.Equals(group.Name, "metadata", StringComparison.OrdinalIgnoreCase)) continue;
                    // SQL Prompt model: the style is its sqlPrompt document; the AKML option groups
                    // beside it are the engine's projection for older builds, not editor-owned.
                    if (IsSqlPromptModel && !string.Equals(group.Name, "sqlPrompt", StringComparison.Ordinal)) continue;
                    OverlayObject(group.Name, group.Value);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "FormatStylesEditor: overlay of loaded profile values failed");
            }
        }

        private void OverlayObject(string prefix, JsonElement obj)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                var path = prefix + "." + prop.Name;
                switch (prop.Value.ValueKind)
                {
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                    case JsonValueKind.Number:
                    case JsonValueKind.String:
                        _workingValues[path] = ProfileJsonMerger.ReadScalar(prop.Value);
                        break;
                    case JsonValueKind.Object:
                        OverlayObject(path, prop.Value);
                        break;
                    // Arrays/null: not editor-modeled — the merge base preserves them untouched.
                }
            }
        }

        // -----------------------------------------------------------------
        // Reset / Revert
        // -----------------------------------------------------------------

        /// <summary>
        /// Discards unsaved edits to the loaded style, putting every control back to the values in
        /// the stored file. Nothing is written and nothing is asked of the engine — the merge base
        /// the editor already holds IS the stored file, so this is purely local.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="ResetToBuiltInAsync"/>, which throws away edits that were
        /// already SAVED. Collapsing the two into one "reset" would make an unrecoverable action
        /// and a trivially recoverable one share a button.
        /// </remarks>
        public bool RevertChanges()
        {
            if (_loadedProfileJson == null || string.IsNullOrEmpty(_loadedProfileName))
            {
                LastError = "No style loaded.";
                return false;
            }

            ReseedWorkingValues();
            OverlayProfileValuesFromJson(_loadedProfileJson!);
            IsDirty = false;
            LastError = null;
            QueuePreviewAsync();
            return true;
        }

        /// <summary>
        /// Discards the user's SAVED edits to a shipped style: the engine deletes the custom file
        /// that shadows the built-in, and the shipped style resolves again.
        /// <para>
        /// The editor rebinds from the text the engine read back afterwards rather than from what
        /// the reset was expected to produce, so what is displayed is what is now stored.
        /// </para>
        /// </summary>
        public async Task<bool> ResetToBuiltInAsync()
        {
            if (string.IsNullOrEmpty(_loadedProfileName))
            {
                LastError = "No style loaded.";
                return false;
            }
            if (!IsSelectedBuiltIn)
            {
                // Guarded here as well as in the engine: a custom style has no shipped original to
                // return to, and the nearest thing -- wiping it to defaults -- would destroy work
                // under a word that does not imply it.
                LastError = $"'{_loadedProfileName}' is your own style, so there is no built-in version to reset to.";
                return false;
            }
            if (!_rpc.IsConnected)
            {
                LastError = "Engine not connected.";
                return false;
            }

            try
            {
                var response = await _rpc.SendRequestAsync<ProfileResetResponse, ProfileResetRequest>(
                    MessageTypes.ProfileReset,
                    new ProfileResetRequest { Name = _loadedProfileName! },
                    timeoutMs: 5000).ConfigureAwait(true);

                if (response == null || !response.Success || string.IsNullOrEmpty(response.ProfileJson))
                {
                    LastError = response?.ErrorMessage ?? "Reset failed.";
                    return false;
                }

                var restored = response.ProfileJson!;
                if (IsSqlPromptModel)
                {
                    var reread = await _rpc.SendRequestAsync<ProfileGetResponse, ProfileGetRequest>(
                        MessageTypes.ProfileGet,
                        new ProfileGetRequest { Name = _loadedProfileName! },
                        timeoutMs: 5000).ConfigureAwait(true);
                    if (reread is { Success: true, ProfileJson: not null } && !string.IsNullOrEmpty(reread.SqlPromptJson))
                    {
                        restored = ComposeSqlPromptBase(reread.ProfileJson, reread.SqlPromptJson!);
                        IsSelectedClassic = !reread.IsSqlPromptStyle;
                    }
                }

                ReseedWorkingValues();
                OverlayProfileValuesFromJson(restored);

                _loadedProfileJson = restored;
                IsSelectedCustomized = false;
                IsDirty = false;
                LastError = null;
                QueuePreviewAsync();
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Warning(ex, "FormatStylesEditor: reset {Name} failed", _loadedProfileName);
                return false;
            }
        }

        // -----------------------------------------------------------------
        // Spec 033 (T015): merge-save
        // -----------------------------------------------------------------

        /// <summary>
        /// Persists the loaded style by merging edited working values into its raw stored JSON
        /// (metadata + untouched keys intact) via the existing ProfileSave IPC. On success the
        /// merged text becomes the new merge base and the dirty flag clears.
        /// </summary>
        public async Task<bool> SaveAsync()
        {
            if (_loadedProfileJson == null || string.IsNullOrEmpty(_loadedProfileName))
            {
                LastError = "No style loaded.";
                return false;
            }
            if (!_rpc.IsConnected)
            {
                LastError = "Engine not connected.";
                return false;
            }

            try
            {
                var merged = ProfileJsonMerger.Merge(_loadedProfileJson, _workingValues, _schemaDefaults);

                var response = await _rpc.SendRequestAsync<ProfileSaveResponse, ProfileSaveRequest>(
                    MessageTypes.ProfileSave,
                    new ProfileSaveRequest { Name = _loadedProfileName!, ProfileJson = merged },
                    timeoutMs: 5000).ConfigureAwait(true);

                if (response == null || !response.Success)
                {
                    LastError = response?.ErrorMessage ?? "Save failed.";
                    return false;
                }

                _loadedProfileJson = merged;
                if (IsSqlPromptModel) IsSelectedClassic = false;
                // Saving over a shipped style just created the override that shadows it, so Reset
                // becomes available from this moment -- without a reload.
                if (IsSelectedBuiltIn) IsSelectedCustomized = true;
                IsDirty = false;
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Warning(ex, "FormatStylesEditor: save {Name} failed", _loadedProfileName);
                return false;
            }
        }

        // -----------------------------------------------------------------

        /// <summary>
        /// Asynchronously loads profiles + schema. Safe to call multiple times — only one
        /// IPC roundtrip per fetch.
        /// </summary>
        public async Task LoadAsync(CancellationToken cancellationToken = default)
        {
            IsLoading = true;
            LastError = null;
            try
            {
                await LoadProfilesAsync(cancellationToken).ConfigureAwait(false);
                await LoadSchemaAsync(cancellationToken).ConfigureAwait(false);
                await AutoSelectActiveProfileAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Warning(ex, "FormatStylesEditor: LoadAsync failed");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Spec 033 review — open the editor with the ACTIVE style already loaded: nothing else
        /// selects a style at open, so without this the preview sits on its placeholder until
        /// the user clicks. Goes through the normal <see cref="SelectProfileAsync"/> path (at
        /// open nothing is dirty, so the dirty prompt cannot fire) — working values seed and
        /// the initial preview queues. When no active style resolves (engine down, or a
        /// config pointer naming a style the engine no longer lists), nothing is selected,
        /// same as before.
        /// </summary>
        private async Task AutoSelectActiveProfileAsync()
        {
            if (_loadedProfileName != null || !_rpc.IsConnected) return;
            var active = Profiles.FirstOrDefault(p => p.IsActive)?.Name;
            if (active == null) return;
            await SelectProfileAsync(active).ConfigureAwait(false);
        }

        // -----------------------------------------------------------------
        // Spec 030 T020 — Format Styles editor New / Copy / Set Active / Export.
        // New/Copy duplicate a STORED profile server-side (DuplicateProfile IPC) — faithful to the
        // persisted values, independent of the editor's working-values preview state. Set Active
        // writes AppSettings.Formatter.ActiveProfile (a Core/ConfigManager concern; the engine has
        // no set-active IPC). Export reuses the existing ProfileExportSqlPrompt IPC.
        // -----------------------------------------------------------------

        /// <summary>New style: duplicate the built-in <c>Default</c> under a unique name.</summary>
        public Task<string?> NewProfileAsync()
            => DuplicateAsync("Default", UniqueName("Custom Style"));

        /// <summary>
        /// Spec 033 (T034) — New Style… with a chosen name and based-on style. The engine's
        /// DuplicateProfile clones the base's persisted values under the new name directly.
        /// </summary>
        public Task<string?> CreateStyleAsync(string name, string basedOn)
        {
            if (string.IsNullOrWhiteSpace(name)) { LastError = "Enter a style name."; return Task.FromResult<string?>(null); }
            if (string.IsNullOrWhiteSpace(basedOn)) { LastError = "Choose a style to base the new one on."; return Task.FromResult<string?>(null); }
            if (Profiles.Any(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                LastError = $"A style named '{name.Trim()}' already exists.";
                return Task.FromResult<string?>(null);
            }
            return DuplicateAsync(basedOn, name.Trim());
        }

        /// <summary>
        /// Spec 033 (T034) — renames the loaded/selected custom style via the atomic engine
        /// ProfileRename. When the renamed style is the ACTIVE one, the shell-owned
        /// <c>Formatter.ActiveProfile</c> pointer is updated in the same flow (the engine
        /// cannot touch config.json — without this, formatting silently falls back to defaults).
        /// Returns the final persisted name, or null on failure/refusal.
        /// </summary>
        public async Task<string?> RenameSelectedAsync(string newName)
        {
            var target = _loadedProfileName ?? SelectedProfileName;
            if (string.IsNullOrWhiteSpace(target)) { LastError = "Select a style to rename."; return null; }
            // Still refused, and not for the old reason. A built-in's name is what ties an
            // override to the style it overrides, so renaming one would orphan the original rather
            // than rename anything. Copy makes an independent style that can be named freely.
            if (IsSelectedBuiltIn) { LastError = "Built-in styles cannot be renamed — use Copy to make one you can name."; return null; }
            if (string.IsNullOrWhiteSpace(newName)) { LastError = "Enter a new name."; return null; }
            if (!_rpc.IsConnected) { LastError = "Engine not connected."; return null; }

            try
            {
                var response = await _rpc.SendRequestAsync<ProfileRenameResponse, ProfileRenameRequest>(
                    MessageTypes.ProfileRename,
                    new ProfileRenameRequest { OldName = target!, NewName = newName },
                    timeoutMs: 5000).ConfigureAwait(true);

                if (response == null || !response.Success)
                {
                    LastError = response?.ErrorMessage ?? "Rename failed.";
                    return null;
                }

                var finalName = response.NewName ?? newName.Trim();

                // Active-pointer follow-up (shell-owned config).
                try
                {
                    var settings = ConfigManager.Load();
                    if (string.Equals(settings.Formatter.ActiveProfile, target, StringComparison.OrdinalIgnoreCase))
                    {
                        settings.Formatter.ActiveProfile = finalName;
                        ConfigManager.Save(settings);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "FormatStylesEditor: active-profile update after rename failed");
                }

                if (string.Equals(_loadedProfileName, target, StringComparison.OrdinalIgnoreCase))
                    _loadedProfileName = finalName;
                SelectedProfileName = finalName;
                LastError = null;
                await RefreshProfilesAsync().ConfigureAwait(true);
                return finalName;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Warning(ex, "FormatStylesEditor: rename {Old} -> {New} failed", target, newName);
                return null;
            }
        }

        /// <summary>
        /// Spec 033 (T034) — deletes the selected custom style. Refused shell-side (before any
        /// IPC) for built-ins and for the ACTIVE style: deleting the active style would leave a
        /// dangling config pointer and the engine silently formats with defaults.
        /// </summary>
        public async Task<bool> DeleteSelectedAsync()
        {
            var target = SelectedProfileName ?? _loadedProfileName;
            if (string.IsNullOrWhiteSpace(target)) { LastError = "Select a style to delete."; return false; }

            var item = Profiles.FirstOrDefault(p => string.Equals(p.Name, target, StringComparison.OrdinalIgnoreCase));
            if (item?.IsShipped == true)
            {
                LastError = item.IsCustomized
                    ? "Built-in styles cannot be deleted — reset it to discard your changes."
                    : "Built-in styles cannot be deleted.";
                return false;
            }

            try
            {
                var active = ConfigManager.Load().Formatter.ActiveProfile;
                if (string.Equals(active, target, StringComparison.OrdinalIgnoreCase))
                {
                    LastError = $"'{target}' is the active style — make another style active first.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FormatStylesEditor: active-style check before delete failed");
            }

            if (!_rpc.IsConnected) { LastError = "Engine not connected."; return false; }

            try
            {
                var response = await _rpc.SendRequestAsync<ProfileDeleteResponse, ProfileDeleteRequest>(
                    MessageTypes.ProfileDelete,
                    new ProfileDeleteRequest { Name = target! },
                    timeoutMs: 5000).ConfigureAwait(true);

                if (response == null || !response.Success)
                {
                    LastError = response?.ErrorMessage ?? "Delete failed.";
                    return false;
                }

                if (string.Equals(_loadedProfileName, target, StringComparison.OrdinalIgnoreCase))
                    ClearLoadedProfile();
                LastError = null;
                await RefreshProfilesAsync().ConfigureAwait(true);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Warning(ex, "FormatStylesEditor: delete {Name} failed", target);
                return false;
            }
        }

        /// <summary>Copy: duplicate the given stored profile under a unique "{name} copy" name.</summary>
        public Task<string?> CopyProfileAsync(string sourceName)
            => string.IsNullOrWhiteSpace(sourceName)
                ? Task.FromResult<string?>(null)
                : DuplicateAsync(sourceName, UniqueName($"{sourceName} copy"));

        private async Task<string?> DuplicateAsync(string source, string newName)
        {
            if (!_rpc.IsConnected)
            {
                LastError = "Engine not connected.";
                return null;
            }
            try
            {
                var response = await _rpc.SendRequestAsync<DuplicateProfileResponse, DuplicateProfileRequest>(
                    MessageTypes.DuplicateProfile,
                    new DuplicateProfileRequest { SourceName = source, NewName = newName },
                    timeoutMs: 5000).ConfigureAwait(false);

                if (response == null || !response.Success)
                {
                    LastError = response?.ErrorMessage ?? "Duplicate failed.";
                    return null;
                }
                await RefreshProfilesAsync().ConfigureAwait(false);
                return response.NewName;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Warning(ex, "FormatStylesEditor: duplicate {Source} -> {New} failed", source, newName);
                return null;
            }
        }

        /// <summary>
        /// Sets the active formatting style (FR-006). Writes <c>AppSettings.Formatter.ActiveProfile</c>
        /// via ConfigManager; the next Format SQL dispatch reads it. Returns true on success.
        /// </summary>
        public bool SetActiveProfile(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                var settings = ConfigManager.Load();
                settings.Formatter.ActiveProfile = name;
                ConfigManager.Save(settings);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Warning(ex, "FormatStylesEditor: set-active {Name} failed", name);
                return false;
            }
        }

        /// <summary>Exports the given stored profile to a .sqlpromptstylev2 file at the given path.</summary>
        public async Task<bool> ExportProfileAsync(string profileName, string destinationPath)
        {
            if (!_rpc.IsConnected)
            {
                LastError = "Engine not connected.";
                return false;
            }
            try
            {
                var response = await _rpc.SendRequestAsync<ProfileExportSqlPromptResponse, ProfileExportSqlPromptRequest>(
                    MessageTypes.ProfileExportSqlPrompt,
                    new ProfileExportSqlPromptRequest { Name = profileName, DestinationPath = destinationPath },
                    timeoutMs: 5000).ConfigureAwait(false);

                if (response == null || !response.Success)
                {
                    LastError = response?.ErrorMessage ?? "Export failed.";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Warning(ex, "FormatStylesEditor: export {Name} failed", profileName);
                return false;
            }
        }

        /// <summary>
        /// Spec 031 FR-010/FR-011 — imports a SQL Prompt style file (JSON or legacy XML) via the
        /// ProfileImport IPC. Returns the full response (option reports included) or null on
        /// transport failure; LastError is set on any failure path.
        /// </summary>
        public async Task<ProfileImportResponse?> ImportProfileAsync(string filePath, string? targetName = null)
        {
            const long MaxImportBytes = 1024 * 1024; // FR-010 — 1 MB cap, mirrors snippet import

            if (!_rpc.IsConnected)
            {
                LastError = "Engine not connected.";
                return null;
            }
            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists) { LastError = "File not found."; return null; }
                if (info.Length > MaxImportBytes) { LastError = "Style file exceeds the 1 MB import limit."; return null; }

                var bytes = File.ReadAllBytes(filePath); // UTF-8 (BOM tolerated engine-side by Encoding.UTF8.GetString)
                var response = await _rpc.SendRequestAsync<ProfileImportResponse, ProfileImportRequest>(
                    MessageTypes.ProfileImport,
                    new ProfileImportRequest { SourceFormat = "sqlprompt", FileContent = bytes, TargetProfileName = targetName },
                    timeoutMs: 5000).ConfigureAwait(false);

                if (response == null || !response.Success)
                {
                    LastError = response?.ErrorMessage ?? "Import failed.";
                    return response;
                }
                await RefreshProfilesAsync().ConfigureAwait(false);
                return response;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Warning(ex, "FormatStylesEditor: import {Path} failed", filePath);
                return null;
            }
        }

        /// <summary>Re-fetches the profile list (after New/Copy create a profile).</summary>
        public Task RefreshProfilesAsync() => LoadProfilesAsync(CancellationToken.None);

        /// <summary>Returns <paramref name="baseName"/>, or "baseName 2", "baseName 3"… if taken.</summary>
        private string UniqueName(string baseName)
        {
            bool Taken(string n) => Profiles.Any(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase));
            if (!Taken(baseName)) return baseName;
            for (int i = 2; i < 1000; i++)
            {
                var candidate = $"{baseName} {i}";
                if (!Taken(candidate)) return candidate;
            }
            return $"{baseName} {Guid.NewGuid():N}";
        }

        private async Task LoadProfilesAsync(CancellationToken ct)
        {
            if (!_rpc.IsConnected)
            {
                Log.Debug("FormatStylesEditor: engine not connected, profile list skipped");
                return;
            }

            try
            {
                var response = await _rpc.SendRequestAsync<ProfileListResponse, ProfileListRequest>(
                    MessageTypes.ProfileList,
                    new ProfileListRequest(),
                    timeoutMs: 3000,
                    ct).ConfigureAwait(false);

                // Spec 033 (T034) — ✔ marker source. The active style is shell-owned config
                // (ProfileInfo.IsActive is never populated on the wire).
                string? activeProfile = null;
                try { activeProfile = ConfigManager.Load().Formatter.ActiveProfile; }
                catch (Exception ex) { Log.Debug(ex, "FormatStylesEditor: active-profile read failed"); }

                // Profiles is bound to the style ListBox (ItemsSource) — mutate it on the UI thread.
                // The IPC await above resumed on a thread-pool thread (ConfigureAwait(false)); without
                // this switch, Clear()/Add() throw off-dispatcher, which surfaced as New/Copy wrongly
                // reporting failure after a successful server-side duplicate (spec 030 T020 review).
                // Spec 033 simplify pass: the seam replaces the old catch-all guard — swallowing a
                // REAL switch failure would proceed to mutate the collection off-dispatcher, the
                // exact bug the switch exists to prevent. Headless tests inject a no-op instead.
                if (MainThreadSwitchOverride != null)
                    await MainThreadSwitchOverride().ConfigureAwait(false);
                else
                    await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                Profiles.Clear();
                if (response?.Profiles == null) return;

                foreach (var p in response.Profiles)
                {
                    Profiles.Add(new StyleListItem
                    {
                        Name = p.Name ?? string.Empty,
                        Description = p.Description ?? string.Empty,
                        // IsBuiltIn alone cannot describe an edited built-in: the file that
                        // resolves is the custom one, so IsBuiltIn is false while the style is
                        // still shipped. The two flags together say "shipped" and "changed".
                        IsShipped = p.IsBuiltIn || p.IsCustomizedBuiltIn,
                        IsCustomized = p.IsCustomizedBuiltIn,
                        BasedOn = p.BasedOn,
                        IsActive = activeProfile != null && string.Equals(p.Name, activeProfile, StringComparison.OrdinalIgnoreCase),
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FormatStylesEditor: profile list IPC failed");
                throw;
            }
        }

        private async Task LoadSchemaAsync(CancellationToken ct)
        {
            if (!_rpc.IsConnected)
            {
                Log.Debug("FormatStylesEditor: engine not connected, schema fetch skipped");
                return;
            }

            try
            {
                var response = await _rpc.SendRequestAsync<StyleEditorSchemaResponse, StyleEditorSchemaRequest>(
                    MessageTypes.RequestStyleEditorSchema,
                    new StyleEditorSchemaRequest
                    {
                        ClientSchemaVersion = _cachedSchemaVersion,
                        IncludeUnsupported = true,
                        // SQL Prompt's own option model; an older engine ignores this and sends
                        // the AKML settings schema, which the editor still renders.
                        SqlPromptModel = true,
                    },
                    timeoutMs: 3000,
                    ct).ConfigureAwait(false);

                if (response == null) return;

                if (response.Cached && _cachedSchemaJson != null)
                {
                    SchemaJson = _cachedSchemaJson;
                    // The cache is static but the VM is recreated per window open — seed + queue
                    // exactly like the cold path below, or every second-and-later open starts
                    // with empty working values and never renders an initial preview.
                    SeedWorkingValuesFromSchema(_cachedSchemaJson);
                    QueuePreviewAsync();
                    return;
                }

                if (!string.IsNullOrEmpty(response.SchemaJson))
                {
                    _cachedSchemaVersion = response.SchemaVersion;
                    _cachedSchemaJson = response.SchemaJson;
                    SchemaJson = response.SchemaJson;
                    SeedWorkingValuesFromSchema(response.SchemaJson!);
                    QueuePreviewAsync();
                }
                else if (!string.IsNullOrEmpty(response.ErrorMessage))
                {
                    LastError = response.ErrorMessage;
                }
                else if (response.Cached && _cachedSchemaJson != null)
                {
                    SeedWorkingValuesFromSchema(_cachedSchemaJson);
                    QueuePreviewAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FormatStylesEditor: schema IPC failed");
                throw;
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // -----------------------------------------------------------------
        // Default sample SQL (Tier 2b — small representative snippet exercising several
        // setting groups so toggling controls produces visible differences in the preview).
        // -----------------------------------------------------------------
        private const string DefaultSampleSql = @"-- Sample SQL for the Format Styles editor preview
SELECT TOP 10
    o.OrderID,
    c.CustomerName,
    SUM(d.UnitPrice * d.Quantity) AS Total
FROM Orders o
INNER JOIN Customers c ON c.CustomerID = o.CustomerID
INNER JOIN OrderDetails d ON d.OrderID = o.OrderID
WHERE o.OrderDate >= '2025-01-01'
    AND c.Country = 'USA'
GROUP BY o.OrderID, c.CustomerName
HAVING SUM(d.UnitPrice * d.Quantity) > 100
ORDER BY Total DESC;

INSERT INTO Audit (Action, Timestamp)
VALUES ('SampleQuery', GETDATE());";
    }

    /// <summary>Lightweight DTO bound to the style list (left panel).</summary>
    internal sealed class StyleListItem
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// True when a style shipped under this name. Shipped styles are editable -- an edit
        /// writes an override -- but they can never be renamed or deleted.
        /// </summary>
        public bool IsShipped { get; set; }

        /// <summary>True when this is a shipped style the user has edited.</summary>
        public bool IsCustomized { get; set; }

        /// <summary>
        /// Badge text. "Built-in - modified" is the one that earns its place: without it there is
        /// no way to tell, from the list, whether a built-in is the shipped style or your edited
        /// version of it -- and that is exactly what someone reaching for Reset needs to know.
        /// </summary>
        public string Kind => IsCustomized ? "Built-in \u00b7 modified" : IsShipped ? "Built-in" : "Native";

        /// <summary>If this profile was forked from another, that source name.</summary>
        public string? BasedOn { get; set; }

        /// <summary>Spec 033 — ✔ marker: this style is <c>Formatter.ActiveProfile</c> (shell config).</summary>
        public bool IsActive { get; set; }

        /// <summary>Spec 033 — list section header ("Your styles" / "Built-in styles").</summary>
        public string Section => IsShipped ? "Built-in styles" : "Your styles";

        public override string ToString() =>
            string.IsNullOrEmpty(Description) ? Name : $"{Name} — {Description}";
    }

    /// <summary>Spec 033 — outcome of the unsaved-edits prompt when switching styles or closing.</summary>
    internal enum StyleSwitchDecision
    {
        /// <summary>Persist the edits, then continue the transition.</summary>
        Save,
        /// <summary>Drop the edits and continue the transition.</summary>
        Discard,
        /// <summary>Abort the transition; the dirty style stays loaded and selected.</summary>
        Cancel,
    }

    /// <summary>Spec 030 T019 / FR-008 — which SQL the Format Styles preview formats.</summary>
    internal enum FormatPreviewSource
    {
        /// <summary>The persisted/default sample snippet.</summary>
        Sample,
        /// <summary>SQL Prompt model: the current page's own sample (what its options act on).</summary>
        PageSample,
        /// <summary>The text from the editor that was active when the styles editor opened.</summary>
        CurrentQuery,
    }
}
