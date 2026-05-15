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
using System.Threading;
using System.Threading.Tasks;
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
            _previewCts?.Cancel();
            _previewCts = new CancellationTokenSource();
            var token = _previewCts.Token;
            var sequence = System.Threading.Interlocked.Increment(ref _previewSequence);

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
                        SampleText = PreviewSample,
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
}
