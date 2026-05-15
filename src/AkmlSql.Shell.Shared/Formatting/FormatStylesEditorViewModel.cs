#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
                }
                else if (!string.IsNullOrEmpty(response.ErrorMessage))
                {
                    LastError = response.ErrorMessage;
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
