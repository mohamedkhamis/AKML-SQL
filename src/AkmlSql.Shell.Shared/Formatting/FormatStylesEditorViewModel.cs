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
    /// Edit / Save / Export / Live-preview wiring is intentionally not implemented in this
    /// Tier-2 slice — the panels render data but don't yet mutate it. Adding those panels
    /// is a follow-up commit; the view-model exposes the hooks
    /// (<see cref="SelectedSettingId"/>, <see cref="SelectedProfileName"/>) ready for them.
    /// </para>
    /// </summary>
    internal sealed class FormatStylesEditorViewModel : INotifyPropertyChanged
    {
        private static int? _cachedSchemaVersion;
        private static string? _cachedSchemaJson;

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

        private string? _selectedSettingId;
        public string? SelectedSettingId
        {
            get => _selectedSettingId;
            set { _selectedSettingId = value; OnPropertyChanged(); }
        }

        private string _previewText = string.Empty;
        public string PreviewText
        {
            get => _previewText;
            private set { _previewText = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Spec 020 T070 — non-null when the engine's stage-6 SemanticValidator rejected the
        /// formatted output. The view renders an inline warning bar above the preview pane.
        /// </summary>
        private string? _previewValidationError;
        public string? PreviewValidationError
        {
            get => _previewValidationError;
            private set { _previewValidationError = value; OnPropertyChanged(); }
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
            PreviewSourceMode == FormatPreviewSource.CurrentQuery && HasCurrentQuery
                ? _currentQueryText
                : _previewSample;

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
        /// Records a user edit and triggers a debounced preview refresh.
        /// </summary>
        public void SetWorkingValue(string settingId, object? value)
        {
            _workingValues[settingId] = value;
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
                if (!doc.RootElement.TryGetProperty("settings", out var settings)) return;

                foreach (var s in settings.EnumerateArray())
                {
                    var id = s.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    if (string.IsNullOrEmpty(id)) continue;

                    if (s.TryGetProperty("default", out var defEl))
                    {
                        _workingValues[id!] = defEl.ValueKind switch
                        {
                            JsonValueKind.True or JsonValueKind.False => defEl.GetBoolean(),
                            JsonValueKind.Number => defEl.TryGetInt32(out var i) ? (object)i : defEl.GetDouble(),
                            JsonValueKind.String => defEl.GetString()!,
                            _ => null,
                        };
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
        /// Flat <c>"groupId.settingName"</c> keys become nested JSON paths.
        /// </summary>
        internal string BuildProfileJson()
        {
            var root = new JsonObject();
            foreach (var kvp in _workingValues)
            {
                var key = kvp.Key;
                var dotIdx = key.IndexOf('.');
                if (dotIdx <= 0) continue;
                var groupId = key.Substring(0, dotIdx);
                var settingName = key.Substring(dotIdx + 1);

                if (root[groupId] is not JsonObject groupNode)
                {
                    groupNode = new JsonObject();
                    root[groupId] = groupNode;
                }
                groupNode[settingName] = ToJsonValue(kvp.Value);
            }
            return root.ToJsonString();
        }

        private static JsonNode? ToJsonValue(object? value) => value switch
        {
            null => null,
            bool b => JsonValue.Create(b),
            int i => JsonValue.Create(i),
            long l => JsonValue.Create(l),
            double d => JsonValue.Create(d),
            string s => JsonValue.Create(s),
            _ => JsonValue.Create(value.ToString()),
        };

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

                    var client = EngineLifecycle.Manager?.Client;
                    if (client == null || !client.IsConnected) return;

                    var request = new FormatPreviewRequest
                    {
                        SessionId = "format-styles-editor",
                        SampleText = EffectivePreviewSample,
                        ProfileJson = BuildProfileJson(),
                    };

                    var response = await client.SendRequestAsync<FormatPreviewResponse, FormatPreviewRequest>(
                        MessageTypes.FormatPreview,
                        request,
                        timeoutMs: 2000,
                        token).ConfigureAwait(false);

                    // Discard if a newer request has been queued while we waited
                    if (sequence < _previewSequence) return;

                    if (response != null && !string.IsNullOrEmpty(response.FormattedText))
                    {
                        PreviewText = response.FormattedText;
                        PreviewValidationError = response.ValidationError;
                    }
                }
                catch (OperationCanceledException) { /* superseded — fine */ }
                catch (Exception ex)
                {
                    Log.Debug(ex, "FormatStylesEditor: preview request failed");
                }
            }, token);
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

        /// <summary>Copy: duplicate the given stored profile under a unique "{name} copy" name.</summary>
        public Task<string?> CopyProfileAsync(string sourceName)
            => string.IsNullOrWhiteSpace(sourceName)
                ? Task.FromResult<string?>(null)
                : DuplicateAsync(sourceName, UniqueName($"{sourceName} copy"));

        private async Task<string?> DuplicateAsync(string source, string newName)
        {
            var client = EngineLifecycle.Manager?.Client;
            if (client == null || !client.IsConnected)
            {
                LastError = "Engine not connected.";
                return null;
            }
            try
            {
                var response = await client.SendRequestAsync<DuplicateProfileResponse, DuplicateProfileRequest>(
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
            var client = EngineLifecycle.Manager?.Client;
            if (client == null || !client.IsConnected)
            {
                LastError = "Engine not connected.";
                return false;
            }
            try
            {
                var response = await client.SendRequestAsync<ProfileExportSqlPromptResponse, ProfileExportSqlPromptRequest>(
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

            var client = EngineLifecycle.Manager?.Client;
            if (client == null || !client.IsConnected)
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
                var response = await client.SendRequestAsync<ProfileImportResponse, ProfileImportRequest>(
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
            var client = EngineLifecycle.Manager?.Client;
            if (client == null || !client.IsConnected)
            {
                Log.Debug("FormatStylesEditor: engine not connected, profile list skipped");
                return;
            }

            try
            {
                var response = await client.SendRequestAsync<ProfileListResponse, ProfileListRequest>(
                    MessageTypes.ProfileList,
                    new ProfileListRequest(),
                    timeoutMs: 3000,
                    ct).ConfigureAwait(false);

                // Profiles is bound to the style ListBox (ItemsSource) — mutate it on the UI thread.
                // The IPC await above resumed on a thread-pool thread (ConfigureAwait(false)); without
                // this switch, Clear()/Add() throw off-dispatcher, which surfaced as New/Copy wrongly
                // reporting failure after a successful server-side duplicate (spec 030 T020 review).
                await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                Profiles.Clear();
                if (response?.Profiles == null) return;

                foreach (var p in response.Profiles)
                {
                    Profiles.Add(new StyleListItem
                    {
                        Name = p.Name ?? string.Empty,
                        Description = p.Description ?? string.Empty,
                        Kind = p.IsBuiltIn ? "Built-in" : "Native",
                        IsReadOnly = p.IsBuiltIn,
                        BasedOn = p.BasedOn,
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
            var client = EngineLifecycle.Manager?.Client;
            if (client == null || !client.IsConnected)
            {
                Log.Debug("FormatStylesEditor: engine not connected, schema fetch skipped");
                return;
            }

            try
            {
                var response = await client.SendRequestAsync<StyleEditorSchemaResponse, StyleEditorSchemaRequest>(
                    MessageTypes.RequestStyleEditorSchema,
                    new StyleEditorSchemaRequest
                    {
                        ClientSchemaVersion = _cachedSchemaVersion,
                        IncludeUnsupported = true,
                    },
                    timeoutMs: 3000,
                    ct).ConfigureAwait(false);

                if (response == null) return;

                if (response.Cached && _cachedSchemaJson != null)
                {
                    SchemaJson = _cachedSchemaJson;
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

        /// <summary>"Built-in" (read-only) or "Native" (user-editable).</summary>
        public string Kind { get; set; } = "Native";

        public bool IsReadOnly { get; set; }

        /// <summary>If this profile was forked from another, that source name.</summary>
        public string? BasedOn { get; set; }

        public override string ToString() =>
            string.IsNullOrEmpty(Description) ? Name : $"{Name} — {Description}";
    }

    /// <summary>Spec 030 T019 / FR-008 — which SQL the Format Styles preview formats.</summary>
    internal enum FormatPreviewSource
    {
        /// <summary>The persisted/default sample snippet.</summary>
        Sample,
        /// <summary>The text from the editor that was active when the styles editor opened.</summary>
        CurrentQuery,
    }
}
