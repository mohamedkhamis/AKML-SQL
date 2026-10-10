using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 041 (research R35, contracts/ui.md §1) — the sizes the editor workspace keeps usable. The editor's
/// minimum width is 120 characters of the 13 px editor font plus the gutter and scrollbar (SC-003).
/// </summary>
public sealed record WorkspaceLayoutLimits
{
    public int EditorMinWidth { get; init; } = 920;
    public int EditorMinHeight { get; init; } = 320;
    public int SchemaMinWidth { get; init; } = 180;
    public int SchemaDefaultWidth { get; init; } = 260;
    public int AiMinWidth { get; init; } = 300;
    public int AiDefaultWidth { get; init; } = 380;
    public int ResultsMinHeight { get; init; } = 120;
    public double ResultsDefaultFraction { get; init; } = 0.38;

    /// <summary>The strip a hidden side panel leaves.</summary>
    public int EdgeStrip { get; init; } = 24;

    /// <summary>A splitter's track.</summary>
    public int SplitterTrack { get; init; } = 6;

    public static WorkspaceLayoutLimits Default { get; } = new();
}

/// <summary>A side panel of the workspace.</summary>
public enum WorkspacePanel
{
    None,
    Schema,
    Ai,
}

/// <summary>Which side panels the window is too narrow for right now (derived, never stored — FR-046).</summary>
public readonly record struct WorkspaceFold(bool SchemaFolded, bool AiFolded)
{
    public static WorkspaceFold None => default;
}

/// <summary>
/// Spec 041 (research R33–R36, data-model.md §1.2) — the editor workspace: which regions show and how big they
/// are. <see cref="Fit"/> corrects a remembered layout to the current window (sizes to the minimums, a layout
/// with every region hidden back to the default regions); <see cref="Fold"/> says which side panels a narrow
/// window cannot fit. Neither is ever written back.
/// </summary>
public sealed record WorkspaceLayout
{
    /// <summary>The layout version this build understands; a remembered layout with another is discarded.</summary>
    public const int CurrentVersion = 1;

    public const string AiTabActions = "actions";
    public const string AiTabChat = "chat";

    public bool NavVisible { get; init; } = true;
    public bool SchemaVisible { get; init; } = true;
    public int SchemaWidth { get; init; } = WorkspaceLayoutLimits.Default.SchemaDefaultWidth;
    public bool AiVisible { get; init; }
    public int AiWidth { get; init; } = WorkspaceLayoutLimits.Default.AiDefaultWidth;
    public string AiTab { get; init; } = AiTabActions;
    public bool ResultsVisible { get; init; } = true;

    /// <summary>The results pane's height; null until the user sizes it (then 38 % of the workspace).</summary>
    public int? ResultsHeight { get; init; }

    public bool ResultsMaximised { get; init; }
    public string ResultsTab { get; init; } = "results";

    public static WorkspaceLayout Default { get; } = new();

    /// <summary>
    /// A remembered layout as it is loaded: one with every region hidden gets the default regions back
    /// (FR-045). Applied once, on load — during a session the user may hide every region (the editor remains).
    /// </summary>
    public static WorkspaceLayout Corrected(WorkspaceLayout layout)
    {
        if (layout.NavVisible || layout.SchemaVisible || layout.AiVisible || layout.ResultsVisible) return layout;
        return layout with
        {
            NavVisible = Default.NavVisible,
            SchemaVisible = Default.SchemaVisible,
            AiVisible = Default.AiVisible,
            ResultsVisible = Default.ResultsVisible,
        };
    }

    /// <summary>
    /// The layout corrected for a <paramref name="width"/> × <paramref name="height"/> workspace: side panels
    /// between their minimum and what leaves the editor its minimum, and the results pane between its minimum
    /// and what leaves the editor its minimum height (FR-045). It never shows a region the user hid.
    /// </summary>
    public static WorkspaceLayout Fit(WorkspaceLayout layout, int width, int height, WorkspaceLayoutLimits limits)
    {
        var fitted = layout;
        if (!fitted.ResultsVisible && fitted.ResultsMaximised) fitted = fitted with { ResultsMaximised = false };

        int sideRoom = Math.Max(0, width - limits.EditorMinWidth);
        int results = fitted.ResultsHeight ?? (int)Math.Round(height * limits.ResultsDefaultFraction);
        return fitted with
        {
            SchemaWidth = Clamp(fitted.SchemaWidth, limits.SchemaMinWidth, Math.Max(limits.SchemaMinWidth, sideRoom)),
            AiWidth = Clamp(fitted.AiWidth, limits.AiMinWidth, Math.Max(limits.AiMinWidth, sideRoom)),
            ResultsHeight = Clamp(results, limits.ResultsMinHeight, Math.Max(limits.ResultsMinHeight, height - limits.EditorMinHeight)),
        };
    }

    /// <summary>
    /// The side panels a <paramref name="width"/>-wide workspace cannot fit next to the editor's minimum: the
    /// AI panel folds first, then the Schema panel — except that <paramref name="keep"/>, the panel the user
    /// last opened by hand, folds last. A folded panel shows as its edge strip; a wider window brings back
    /// any panel the user had not hidden (FR-046).
    /// </summary>
    public static WorkspaceFold Fold(WorkspaceLayout layout, int width, WorkspaceLayoutLimits limits, WorkspacePanel keep = WorkspacePanel.None)
    {
        bool schema = layout.SchemaVisible, ai = layout.AiVisible;
        int Need() =>
            (schema ? layout.SchemaWidth + limits.SplitterTrack : limits.EdgeStrip)
            + (ai ? layout.AiWidth + limits.SplitterTrack : limits.EdgeStrip)
            + limits.EditorMinWidth;

        bool aiFolded = false, schemaFolded = false;
        void FoldAi() { if (ai && width < Need()) { ai = false; aiFolded = true; } }
        void FoldSchema() { if (schema && width < Need()) { schema = false; schemaFolded = true; } }
        if (keep == WorkspacePanel.Ai) { FoldSchema(); FoldAi(); }
        else { FoldAi(); FoldSchema(); }
        return new WorkspaceFold(schemaFolded, aiFolded);
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
}

/// <summary>The field keys of the <c>workspaceLayout</c> store — one record each (data-model.md §1.2).</summary>
public static class WorkspaceLayoutFields
{
    public const string LayoutVersion = "layoutVersion";
    public const string NavVisible = "navVisible";
    public const string SchemaVisible = "schemaVisible";
    public const string SchemaWidth = "schemaWidth";
    public const string AiVisible = "aiVisible";
    public const string AiWidth = "aiWidth";
    public const string AiTab = "aiTab";
    public const string ResultsVisible = "resultsVisible";
    public const string ResultsHeight = "resultsHeight";
    public const string ResultsMaximised = "resultsMaximised";
    public const string ResultsTab = "resultsTab";
}

/// <summary>
/// Spec 041 (research R36, FR-045, FR-047) — the remembered workspace layout, one IndexedDB record per field so a
/// browser tab writes only what the user changed in it, never the whole layout. Loading never writes; the
/// version record is written with the first change.
/// </summary>
public interface IWorkspaceLayoutStore
{
    /// <summary>The layout as remembered (not fitted to the window).</summary>
    WorkspaceLayout Current { get; }

    /// <summary>Raised after a load, a change or a reset.</summary>
    event Action? Changed;

    Task<WorkspaceLayout> LoadAsync();

    /// <summary>Remember one field (a key of <see cref="WorkspaceLayoutFields"/>).</summary>
    Task SetAsync<T>(string field, T value);

    /// <summary>Forget everything: the default layout.</summary>
    Task ResetAsync();
}

internal sealed class WorkspaceLayoutStore : IWorkspaceLayoutStore
{
    private readonly IIndexedDbAdapter _store;
    private bool _versionWritten;

    public WorkspaceLayoutStore(IIndexedDbAdapter store) => _store = store ?? throw new ArgumentNullException(nameof(store));

    public WorkspaceLayout Current { get; private set; } = WorkspaceLayout.Default;

    public event Action? Changed;

    public async Task<WorkspaceLayout> LoadAsync()
    {
        IReadOnlyList<KeyValuePair<string, string>> records;
        try { records = await _store.ListAsync(StoreNames.WorkspaceLayout).ConfigureAwait(false); }
        catch { records = Array.Empty<KeyValuePair<string, string>>(); }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in records) values[key] = value;

        var layout = WorkspaceLayout.Default;
        _versionWritten = values.ContainsKey(WorkspaceLayoutFields.LayoutVersion);
        if (_versionWritten && Read<int?>(values, WorkspaceLayoutFields.LayoutVersion) != WorkspaceLayout.CurrentVersion)
        {
            // A layout this build does not understand is ignored silently (FR-047). It is not erased on load:
            // the first change overwrites the version, and Reset clears it.
            _versionWritten = false;
        }
        else
        {
            foreach (var key in values.Keys) layout = Apply(layout, key, values);
        }

        // Corrected in memory only: loading never writes (FR-045).
        Current = WorkspaceLayout.Corrected(layout);
        Changed?.Invoke();
        return Current;
    }

    public async Task SetAsync<T>(string field, T value)
    {
        if (!_versionWritten)
        {
            await _store.SetAsync(StoreNames.WorkspaceLayout, WorkspaceLayoutFields.LayoutVersion,
                JsonSerializer.Serialize(WorkspaceLayout.CurrentVersion)).ConfigureAwait(false);
            _versionWritten = true;
        }
        var json = JsonSerializer.Serialize(value);
        await _store.SetAsync(StoreNames.WorkspaceLayout, field, json).ConfigureAwait(false);
        Current = Apply(Current, field, new Dictionary<string, string> { [field] = json });
        Changed?.Invoke();
    }

    public async Task ResetAsync()
    {
        await _store.ClearAsync(StoreNames.WorkspaceLayout).ConfigureAwait(false);
        _versionWritten = false;
        Current = WorkspaceLayout.Default;
        Changed?.Invoke();
    }

    private static WorkspaceLayout Apply(WorkspaceLayout layout, string key, IReadOnlyDictionary<string, string> values) => key switch
    {
        WorkspaceLayoutFields.NavVisible => Read<bool?>(values, key) is { } b ? layout with { NavVisible = b } : layout,
        WorkspaceLayoutFields.SchemaVisible => Read<bool?>(values, key) is { } b ? layout with { SchemaVisible = b } : layout,
        WorkspaceLayoutFields.SchemaWidth => Read<int?>(values, key) is { } n ? layout with { SchemaWidth = n } : layout,
        WorkspaceLayoutFields.AiVisible => Read<bool?>(values, key) is { } b ? layout with { AiVisible = b } : layout,
        WorkspaceLayoutFields.AiWidth => Read<int?>(values, key) is { } n ? layout with { AiWidth = n } : layout,
        WorkspaceLayoutFields.AiTab => Read<string?>(values, key) is WorkspaceLayout.AiTabActions or WorkspaceLayout.AiTabChat
            ? layout with { AiTab = Read<string?>(values, key)! } : layout,
        WorkspaceLayoutFields.ResultsVisible => Read<bool?>(values, key) is { } b ? layout with { ResultsVisible = b } : layout,
        // null is a value here: the user forgot the height (double-click), so it is 38 % of the workspace again.
        WorkspaceLayoutFields.ResultsHeight => values[key] == "null" ? layout with { ResultsHeight = null }
            : Read<int?>(values, key) is { } n ? layout with { ResultsHeight = n } : layout,
        WorkspaceLayoutFields.ResultsMaximised => Read<bool?>(values, key) is { } b ? layout with { ResultsMaximised = b } : layout,
        WorkspaceLayoutFields.ResultsTab => Read<string?>(values, key) is "results" or "messages" or "problems"
            ? layout with { ResultsTab = Read<string?>(values, key)! } : layout,
        _ => layout,   // an unknown key (a newer build's field) is ignored
    };

    private static T? Read<T>(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var json)) return default;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch (JsonException) { return default; }
        catch (NotSupportedException) { return default; }
    }
}
