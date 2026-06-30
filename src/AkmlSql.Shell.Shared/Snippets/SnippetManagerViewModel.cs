#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Snippets
{
    /// <summary>
    /// ViewModel for the Snippet Manager dialog.
    /// Communicates with the engine via IPC to list, save, and delete snippets.
    /// </summary>
    internal sealed class SnippetManagerViewModel : INotifyPropertyChanged
    {
        private SnippetInfo? _selectedSnippet;
        private string _searchText = string.Empty;
        private string _editShortcode = string.Empty;
        private string _editName = string.Empty;
        private string _editDescription = string.Empty;
        private string _editBody = string.Empty;
        private string _editCategory = string.Empty;
        private string _editTags = string.Empty;
        private bool _isEditing;
        private bool _isBuiltIn;
        private string _statusMessage = string.Empty;

        public SnippetManagerViewModel()
        {
            Snippets = new ObservableCollection<SnippetInfo>();
            FilteredSnippets = new ObservableCollection<SnippetInfo>();
            Variables = new ObservableCollection<SnippetVariableRow>();
        }

        /// <summary>
        /// Spec 030 T046 / FR-036 — the editable custom-variable definitions for the snippet currently
        /// being edited. Populated from <see cref="SnippetInfo.Variables"/> on load (the dialog binds an
        /// editor grid to this collection); serialized back on Save/Export so variables are never wiped.
        /// </summary>
        public ObservableCollection<SnippetVariableRow> Variables { get; }

        /// <summary>The full list of snippets from the engine.</summary>
        public ObservableCollection<SnippetInfo> Snippets { get; }

        /// <summary>Snippets filtered by <see cref="SearchText"/>.</summary>
        public ObservableCollection<SnippetInfo> FilteredSnippets { get; }

        /// <summary>Currently selected snippet in the list.</summary>
        public SnippetInfo? SelectedSnippet
        {
            get => _selectedSnippet;
            set
            {
                if (_selectedSnippet == value) return;
                _selectedSnippet = value;
                OnPropertyChanged();
                OnSelectedSnippetChanged();
            }
        }

        /// <summary>Filter text; re-filters the list on change.</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        public string EditShortcode
        {
            get => _editShortcode;
            set { if (_editShortcode != value) { _editShortcode = value; OnPropertyChanged(); } }
        }

        public string EditName
        {
            get => _editName;
            set { if (_editName != value) { _editName = value; OnPropertyChanged(); } }
        }

        public string EditDescription
        {
            get => _editDescription;
            set { if (_editDescription != value) { _editDescription = value; OnPropertyChanged(); } }
        }

        public string EditBody
        {
            get => _editBody;
            set { if (_editBody != value) { _editBody = value; OnPropertyChanged(); } }
        }

        public string EditCategory
        {
            get => _editCategory;
            set { if (_editCategory != value) { _editCategory = value; OnPropertyChanged(); } }
        }

        public string EditTags
        {
            get => _editTags;
            set { if (_editTags != value) { _editTags = value; OnPropertyChanged(); } }
        }

        /// <summary>True when a snippet is selected for editing.</summary>
        public bool IsEditing
        {
            get => _isEditing;
            private set { if (_isEditing != value) { _isEditing = value; OnPropertyChanged(); } }
        }

        /// <summary>True when the selected snippet is built-in (Source == 3, read-only).</summary>
        public bool IsBuiltIn
        {
            get => _isBuiltIn;
            private set { if (_isBuiltIn != value) { _isBuiltIn = value; OnPropertyChanged(); } }
        }

        /// <summary>Status message displayed at the bottom of the dialog.</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Loads all snippets from the engine via IPC and populates the collections.
        /// </summary>
        public void LoadSnippets()
        {
            try
            {
                var manager = EngineLifecycle.Manager;
                if (manager?.Client == null)
                {
                    StatusMessage = "Engine not available.";
                    Log.Warning("SnippetManagerViewModel: engine not available for LoadSnippets");
                    return;
                }

                var request = new SnippetListRequest
                {
                    Query = string.Empty,
                    SourceFilter = 0 // All
                };

                var response = ThreadHelper.JoinableTaskFactory.Run(async () =>
                    await manager.Client.SendRequestAsync<SnippetListResponse, SnippetListRequest>(
                        MessageTypes.SnippetList, request, timeoutMs: 10_000));

                Snippets.Clear();
                if (response?.Snippets != null)
                {
                    foreach (var snippet in response.Snippets)
                    {
                        Snippets.Add(snippet);
                    }
                }

                ApplyFilter();
                StatusMessage = $"Loaded {Snippets.Count} snippet(s).";
            }
            catch (Exception ex)
            {
                StatusMessage = "Failed to load snippets.";
                Log.Error(ex, "SnippetManagerViewModel: failed to load snippets");
            }
        }

        /// <summary>
        /// Saves the current edit fields as a snippet via IPC.
        /// If the selected snippet is null (new snippet mode), creates a new one.
        /// </summary>
        public void SaveSnippet()
        {
            if (IsBuiltIn)
            {
                StatusMessage = "Built-in snippets cannot be modified.";
                return;
            }

            try
            {
                var manager = EngineLifecycle.Manager;
                if (manager?.Client == null)
                {
                    StatusMessage = "Engine not available.";
                    return;
                }

                var isNew = _selectedSnippet == null || string.IsNullOrEmpty(_selectedSnippet.Id);
                var snippetId = isNew ? Guid.NewGuid().ToString() : _selectedSnippet!.Id;

                var tags = ParseTags(_editTags);
                var bodyLines = _editBody.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                var snippetObj = new Dictionary<string, object>
                {
                    ["metadata"] = new Dictionary<string, object>
                    {
                        ["id"] = snippetId,
                        ["shortcode"] = _editShortcode,
                        ["name"] = _editName,
                        ["description"] = _editDescription,
                        ["category"] = _editCategory,
                        ["tags"] = tags
                    },
                    // Spec 030 T046 / FR-036 — serialize the current custom variables instead of wiping
                    // them. Keys (name/default/tooltip/schemaAware) match SnippetVariable's JsonPropertyName
                    // exactly because the engine's HandleSave deserializes with default (case-SENSITIVE)
                    // JsonSerializerOptions.
                    ["variables"] = SerializeVariables(),
                    ["body"] = bodyLines
                };

                var json = JsonSerializer.Serialize(snippetObj, JsonOptions.CamelCase);

                var request = new SnippetSaveRequest
                {
                    SnippetJson = json,
                    IsNew = isNew
                };

                var response = ThreadHelper.JoinableTaskFactory.Run(async () =>
                    await manager.Client.SendRequestAsync<SnippetSaveResponse, SnippetSaveRequest>(
                        MessageTypes.SnippetSave, request, timeoutMs: 10_000));

                if (response != null && response.Success)
                {
                    StatusMessage = isNew ? "Snippet created." : "Snippet saved.";
                    LoadSnippets();

                    // Re-select the saved snippet
                    var saved = Snippets.FirstOrDefault(s => s.Id == snippetId);
                    if (saved != null)
                    {
                        SelectedSnippet = saved;
                    }
                }
                else
                {
                    StatusMessage = response?.ErrorMessage ?? "Failed to save snippet.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Failed to save snippet.";
                Log.Error(ex, "SnippetManagerViewModel: failed to save snippet");
            }
        }

        /// <summary>
        /// Deletes the currently selected snippet via IPC.
        /// </summary>
        public void DeleteSnippet()
        {
            if (_selectedSnippet == null)
            {
                StatusMessage = "No snippet selected.";
                return;
            }

            if (IsBuiltIn)
            {
                StatusMessage = "Built-in snippets cannot be deleted.";
                return;
            }

            try
            {
                var manager = EngineLifecycle.Manager;
                if (manager?.Client == null)
                {
                    StatusMessage = "Engine not available.";
                    return;
                }

                var request = new SnippetDeleteRequest
                {
                    SnippetId = _selectedSnippet.Id
                };

                var response = ThreadHelper.JoinableTaskFactory.Run(async () =>
                    await manager.Client.SendRequestAsync<SnippetDeleteResponse, SnippetDeleteRequest>(
                        MessageTypes.SnippetDelete, request, timeoutMs: 10_000));

                if (response != null && response.Success)
                {
                    StatusMessage = "Snippet deleted.";
                    LoadSnippets();
                    SelectedSnippet = FilteredSnippets.FirstOrDefault();
                }
                else
                {
                    StatusMessage = response?.ErrorMessage ?? "Failed to delete snippet.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Failed to delete snippet.";
                Log.Error(ex, "SnippetManagerViewModel: failed to delete snippet");
            }
        }

        /// <summary>
        /// Clears the edit fields and deselects the current snippet to begin creating a new one.
        /// </summary>
        public void NewSnippet()
        {
            _selectedSnippet = null;
            OnPropertyChanged(nameof(SelectedSnippet));

            EditShortcode = string.Empty;
            EditName = string.Empty;
            EditDescription = string.Empty;
            EditBody = string.Empty;
            EditCategory = string.Empty;
            EditTags = string.Empty;
            Variables.Clear();
            OnPropertyChanged(nameof(Variables));
            IsEditing = true;
            IsBuiltIn = false;
            StatusMessage = "Enter details for the new snippet.";
        }

        /// <summary>
        /// Spec 030 T044 / FR-033 — seam for "Create Snippet from Selection". Begins a new-snippet edit
        /// pre-populated with the selected SQL as the body and an auto-derived shortcode (initials), then
        /// reuses the variable-preserving <see cref="SaveSnippet"/> path. Call AFTER the dialog is
        /// constructed so the dialog's PropertyChanged handlers push these values into the textboxes.
        /// </summary>
        public void NewSnippetFromSelection(string body, string shortcode)
        {
            _selectedSnippet = null;
            OnPropertyChanged(nameof(SelectedSnippet));

            EditShortcode = shortcode ?? string.Empty;
            EditName = string.Empty;
            EditDescription = string.Empty;
            EditBody = body ?? string.Empty;
            EditCategory = string.Empty;
            EditTags = string.Empty;
            Variables.Clear();
            OnPropertyChanged(nameof(Variables));
            IsEditing = true;
            IsBuiltIn = false;
            StatusMessage = "New snippet from selection — review the shortcode and save.";
        }

        /// <summary>
        /// Creates a copy of the currently selected snippet with a new ID and "(Copy)" suffix.
        /// </summary>
        public void DuplicateSnippet()
        {
            if (_selectedSnippet == null)
            {
                StatusMessage = "No snippet selected to duplicate.";
                return;
            }

            _selectedSnippet = null;
            OnPropertyChanged(nameof(SelectedSnippet));

            EditShortcode = _editShortcode + "_copy";
            EditName = _editName + " (Copy)";
            // Keep description, body, category, tags, and custom variables as-is. Spec 030 T046:
            // the Variables collection already holds the source snippet's variables (loaded in
            // OnSelectedSnippetChanged); leaving it untouched carries them into the duplicate.
            IsEditing = true;
            IsBuiltIn = false;
            StatusMessage = "Duplicated snippet. Modify and save.";
        }

        /// <summary>
        /// Filters the <see cref="Snippets"/> collection into <see cref="FilteredSnippets"/>
        /// based on the current <see cref="SearchText"/>.
        /// </summary>
        public void ApplyFilter()
        {
            FilteredSnippets.Clear();

            var query = _searchText.Trim();
            if (string.IsNullOrEmpty(query))
            {
                foreach (var snippet in Snippets)
                {
                    FilteredSnippets.Add(snippet);
                }
            }
            else
            {
                var lowerQuery = query.ToLowerInvariant();
                foreach (var snippet in Snippets)
                {
                    if (MatchesFilter(snippet, lowerQuery))
                    {
                        FilteredSnippets.Add(snippet);
                    }
                }
            }

            // If current selection is not in filtered list, select first or clear
            if (_selectedSnippet != null && !FilteredSnippets.Contains(_selectedSnippet))
            {
                SelectedSnippet = FilteredSnippets.FirstOrDefault();
            }
        }

        /// <summary>
        /// Populates the edit fields from the currently selected snippet.
        /// </summary>
        private void OnSelectedSnippetChanged()
        {
            if (_selectedSnippet == null)
            {
                IsEditing = false;
                IsBuiltIn = false;
                EditShortcode = string.Empty;
                EditName = string.Empty;
                EditDescription = string.Empty;
                EditBody = string.Empty;
                EditCategory = string.Empty;
                EditTags = string.Empty;
                Variables.Clear();
                OnPropertyChanged(nameof(Variables));
                return;
            }

            IsEditing = true;
            IsBuiltIn = _selectedSnippet.Source == 3;
            EditShortcode = _selectedSnippet.Shortcode ?? string.Empty;
            EditName = _selectedSnippet.Name ?? string.Empty;
            EditDescription = _selectedSnippet.Description ?? string.Empty;
            EditCategory = _selectedSnippet.Category ?? string.Empty;
            EditTags = _selectedSnippet.Tags != null ? string.Join(", ", _selectedSnippet.Tags) : string.Empty;

            // Spec 030 T046 / FR-036 — load the snippet's custom variables (the root-cause fix: the VM
            // never received them before, so any re-save wiped them). SnippetInfo.Variables now arrives
            // from the engine's HandleList. Replace the collection contents in place.
            Variables.Clear();
            if (_selectedSnippet.Variables != null)
            {
                foreach (var v in _selectedSnippet.Variables)
                {
                    Variables.Add(new SnippetVariableRow
                    {
                        Name = v.Name ?? string.Empty,
                        Default = v.Default ?? string.Empty,
                        Tooltip = v.Tooltip ?? string.Empty,
                        SchemaAware = v.SchemaAware
                    });
                }
            }
            OnPropertyChanged(nameof(Variables));

            // Load the snippet body from the engine via SnippetExpand (with FormatOnExpand=false).
            // SnippetExpandResponse.ExpandedText contains the raw body with placeholders intact.
            EditBody = LoadSnippetBody(_selectedSnippet.Shortcode);
            StatusMessage = IsBuiltIn ? "Built-in snippet (read-only)." : "Ready to edit.";
        }

        /// <summary>
        /// Loads the raw snippet body from the engine via SnippetExpand with FormatOnExpand=false.
        /// Returns an empty string if the engine is unavailable or the snippet is not found.
        /// </summary>
        private string LoadSnippetBody(string shortcode)
        {
            try
            {
                var manager = Ipc.EngineLifecycle.Manager;
                if (manager?.Client == null || !manager.Client.IsConnected)
                    return string.Empty;

                var request = new AkmlSql.Core.Ipc.Messages.SnippetExpandRequest
                {
                    SessionId = string.Empty,
                    Shortcode = shortcode,
                    FormatOnExpand = false
                };

                AkmlSql.Core.Ipc.Messages.SnippetExpandResponse? response = null;
                Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    response = await manager.Client.SendRequestAsync<
                        AkmlSql.Core.Ipc.Messages.SnippetExpandResponse,
                        AkmlSql.Core.Ipc.Messages.SnippetExpandRequest>(
                        AkmlSql.Core.Ipc.MessageTypes.SnippetExpand,
                        request,
                        timeoutMs: 5_000);
                });

                return response?.Success == true ? response.ExpandedText ?? string.Empty : string.Empty;
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug(ex, "SnippetManagerViewModel: failed to load body for {Shortcode}", shortcode);
                return string.Empty;
            }
        }

        private static bool MatchesFilter(SnippetInfo snippet, string lowerQuery)
        {
            if (snippet.Shortcode != null && snippet.Shortcode.ToLowerInvariant().Contains(lowerQuery))
                return true;
            if (snippet.Name != null && snippet.Name.ToLowerInvariant().Contains(lowerQuery))
                return true;
            if (snippet.Description != null && snippet.Description.ToLowerInvariant().Contains(lowerQuery))
                return true;
            if (snippet.Category != null && snippet.Category.ToLowerInvariant().Contains(lowerQuery))
                return true;
            if (snippet.Tags != null)
            {
                foreach (var tag in snippet.Tags)
                {
                    if (tag != null && tag.ToLowerInvariant().Contains(lowerQuery))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Spec 030 T046 / FR-036 — serialize the current <see cref="Variables"/> into the JSON shape the
        /// engine expects. Keys (name/default/tooltip/schemaAware) match the engine's SnippetVariable
        /// JsonPropertyName attributes exactly; HandleSave uses default (case-SENSITIVE) options, so the
        /// keys MUST be lowercase here. Empty-named rows are dropped. <c>schemaAware</c> is omitted when
        /// null so a non-schema-aware variable round-trips to <c>null</c>, not "".
        /// </summary>
        public List<Dictionary<string, object?>> SerializeVariables()
        {
            var result = new List<Dictionary<string, object?>>();
            foreach (var v in Variables)
            {
                if (string.IsNullOrWhiteSpace(v.Name)) continue;
                var dict = new Dictionary<string, object?>
                {
                    ["name"] = v.Name ?? string.Empty,
                    ["default"] = v.Default ?? string.Empty,
                    ["tooltip"] = v.Tooltip ?? string.Empty,
                };
                if (!string.IsNullOrEmpty(v.SchemaAware))
                    dict["schemaAware"] = v.SchemaAware;
                result.Add(dict);
            }
            return result;
        }

        private static string[] ParseTags(string tagsText)
        {
            if (string.IsNullOrWhiteSpace(tagsText))
                return Array.Empty<string>();

            var parts = tagsText.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<string>();
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    result.Add(trimmed);
                }
            }
            return result.ToArray();
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    /// <summary>
    /// Spec 030 T046 / FR-036 — an editable custom-variable row in the Snippet Manager's variables grid.
    /// Mirrors the engine's SnippetVariable (name/default/tooltip/schemaAware). <see cref="SchemaAware"/>
    /// is null for a plain text variable.
    /// </summary>
    internal sealed class SnippetVariableRow
    {
        public string Name { get; set; } = string.Empty;
        public string Default { get; set; } = string.Empty;
        public string Tooltip { get; set; } = string.Empty;
        public string? SchemaAware { get; set; }
    }
}
