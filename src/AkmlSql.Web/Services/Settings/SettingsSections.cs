using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services.Settings;

// Spec 041 (research R46–R49, data-model.md §1.5) — the exportable sections. Each reads and writes only its own
// preference store, so an export can never carry a key, a password, a pairing token, a connection, the schema
// cache, snippets, history, chat, the document or the log (SC-013): those stores are never touched here.

internal sealed class GeneralSettingsSection : ISettingsSection
{
    private readonly IThemeService _theme;

    public GeneralSettingsSection(IThemeService theme) => _theme = theme ?? throw new ArgumentNullException(nameof(theme));

    public string Id => SettingsCatalog.General;
    public string Title => "General";
    public string ExportKey => "general";

    public Task<IReadOnlyList<string>> DescribeResetAsync() =>
        Task.FromResult<IReadOnlyList<string>>(new[] { "The theme goes back to System (it follows Windows)." });

    public Task ResetAsync() => _theme.ResetAsync();

    public async Task<object> ExportAsync()
    {
        await _theme.InitializeAsync().ConfigureAwait(false);
        return new GeneralDto { Theme = _theme.Current };
    }

    public async Task<int> ImportAsync(JsonElement section)
    {
        var dto = SettingsJson.Read<GeneralDto>(section);
        if (dto == null) return 1;
        await _theme.InitializeAsync().ConfigureAwait(false);
        if (dto.Theme is { } mode) await _theme.SetAsync(mode).ConfigureAwait(false);
        return SettingsJson.CountUnknown(dto);
    }

    public sealed class GeneralDto : SettingsDto
    {
        public ThemeMode? Theme { get; set; }
    }
}

internal sealed class EditorSettingsSection : ISettingsSection
{
    private readonly IEditorSettingsStore _store;

    public EditorSettingsSection(IEditorSettingsStore store) => _store = store ?? throw new ArgumentNullException(nameof(store));

    public string Id => SettingsCatalog.Editor;
    public string Title => "Editor";
    public string ExportKey => "editor";

    public Task<IReadOnlyList<string>> DescribeResetAsync() =>
        Task.FromResult<IReadOnlyList<string>>(new[] { "Editor font, font size, word wrap and tab size go back to the default font, 13 px, off and 4." });

    public Task ResetAsync() => _store.ResetAsync();

    public async Task<object> ExportAsync()
    {
        var s = await _store.GetAsync().ConfigureAwait(false);
        return new EditorDto { FontSize = s.FontSize, WordWrap = s.WordWrap, TabSize = s.TabSize, FontFamily = s.FontFamily };
    }

    public async Task<int> ImportAsync(JsonElement section)
    {
        var dto = SettingsJson.Read<EditorDto>(section);
        if (dto == null) return 1;
        var current = await _store.GetAsync().ConfigureAwait(false);
        await _store.SetAsync(new EditorSettings
        {
            FontSize = dto.FontSize ?? current.FontSize,
            WordWrap = dto.WordWrap ?? current.WordWrap,
            TabSize = dto.TabSize ?? current.TabSize,
            FontFamily = dto.FontFamily ?? current.FontFamily,
        }).ConfigureAwait(false);
        return SettingsJson.CountUnknown(dto);
    }

    public sealed class EditorDto : SettingsDto
    {
        public int? FontSize { get; set; }
        public bool? WordWrap { get; set; }
        public int? TabSize { get; set; }

        /// <summary>Spec 043: by name (<c>cascadiaMono</c>); absent in an older export, which keeps the current font.</summary>
        public EditorFontFamily? FontFamily { get; set; }
    }
}

internal sealed class FormatSettingsSection : ISettingsSection
{
    private readonly IProfileStore _profiles;

    public FormatSettingsSection(IProfileStore profiles) => _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));

    public string Id => SettingsCatalog.Format;
    public string Title => "Format";
    public string ExportKey => "format";

    public async Task<IReadOnlyList<string>> DescribeResetAsync()
    {
        var name = (await _profiles.GetAsync(ProfileStore.DefaultActiveId).ConfigureAwait(false))?.Name ?? "the default style";
        return new[] { $"The active style goes back to {name}." };
    }

    public Task ResetAsync() => _profiles.ResetActiveAsync();

    public async Task<object> ExportAsync() =>
        new FormatDto { ActiveStyleId = await _profiles.GetActiveIdAsync().ConfigureAwait(false) };

    public async Task<int> ImportAsync(JsonElement section)
    {
        var dto = SettingsJson.Read<FormatDto>(section);
        if (dto == null) return 1;
        int skipped = SettingsJson.CountUnknown(dto);
        if (!string.IsNullOrEmpty(dto.ActiveStyleId))
        {
            // A style this browser does not have (a user style from another browser) is skipped.
            if (await _profiles.GetAsync(dto.ActiveStyleId!).ConfigureAwait(false) != null)
                await _profiles.SetActiveIdAsync(dto.ActiveStyleId!).ConfigureAwait(false);
            else
                skipped++;
        }
        return skipped;
    }

    public sealed class FormatDto : SettingsDto
    {
        public string? ActiveStyleId { get; set; }
    }
}

internal sealed class QueriesSettingsSection : ISettingsSection
{
    private readonly IExecutionSettingsStore _store;

    public QueriesSettingsSection(IExecutionSettingsStore store) => _store = store ?? throw new ArgumentNullException(nameof(store));

    public string Id => SettingsCatalog.Queries;
    public string Title => "Queries";
    public string ExportKey => "queries";

    public Task<IReadOnlyList<string>> DescribeResetAsync() =>
        Task.FromResult<IReadOnlyList<string>>(new[]
        {
            "The default row limit and command timeout go back to 1,000 rows and 30 seconds.",
            "Show column types and Retain line breaks on copy or save are switched off.",
            "The toolbar's row limit and timeout for this session are dropped.",
        });

    public Task ResetAsync() => _store.ResetAsync();

    public async Task<object> ExportAsync()
    {
        var s = await _store.GetAsync().ConfigureAwait(false);
        return new QueriesDto
        {
            MaxRows = s.MaxRows,
            CommandTimeoutSeconds = s.CommandTimeoutSeconds,
            ShowColumnTypes = s.ShowColumnTypes,
            RetainLineBreaksOnCopy = s.RetainLineBreaksOnCopy,
        };
    }

    public async Task<int> ImportAsync(JsonElement section)
    {
        var dto = SettingsJson.Read<QueriesDto>(section);
        if (dto == null) return 1;
        var current = await _store.GetAsync().ConfigureAwait(false);
        await _store.SetAsync(new ExecutionSettings
        {
            MaxRows = Math.Clamp(dto.MaxRows ?? current.MaxRows, 1, 100_000),
            CommandTimeoutSeconds = Math.Clamp(dto.CommandTimeoutSeconds ?? current.CommandTimeoutSeconds, 1, 600),
            ShowColumnTypes = dto.ShowColumnTypes ?? current.ShowColumnTypes,
            RetainLineBreaksOnCopy = dto.RetainLineBreaksOnCopy ?? current.RetainLineBreaksOnCopy,
        }).ConfigureAwait(false);
        return SettingsJson.CountUnknown(dto);
    }

    public sealed class QueriesDto : SettingsDto
    {
        public int? MaxRows { get; set; }
        public int? CommandTimeoutSeconds { get; set; }
        public bool? ShowColumnTypes { get; set; }
        public bool? RetainLineBreaksOnCopy { get; set; }
    }
}

internal sealed class CodeAnalysisSettingsSection : ISettingsSection
{
    private readonly IAnalysisSettingsStore _store;

    public CodeAnalysisSettingsSection(IAnalysisSettingsStore store) => _store = store ?? throw new ArgumentNullException(nameof(store));

    public string Id => SettingsCatalog.CodeAnalysis;
    public string Title => "Code analysis";
    public string ExportKey => "codeAnalysis";

    public async Task<IReadOnlyList<string>> DescribeResetAsync()
    {
        var s = await _store.GetAsync().ConfigureAwait(false);
        var lines = new List<string> { "Code analysis is switched on, with Re-analyse after format and every Problems filter." };
        int suppressed = s.RuleOverrides.Count(kv => string.Equals(kv.Value, "off", StringComparison.OrdinalIgnoreCase));
        int remapped = s.RuleOverrides.Count - suppressed;
        if (suppressed > 0) lines.Add(suppressed == 1 ? "1 suppressed rule comes back." : $"{suppressed} suppressed rules come back.");
        if (remapped > 0) lines.Add(remapped == 1 ? "1 rule gets its own severity back." : $"{remapped} rules get their own severity back.");
        return lines;
    }

    public Task ResetAsync() => _store.ResetAsync();

    public async Task<object> ExportAsync()
    {
        var s = await _store.GetAsync().ConfigureAwait(false);
        return new CodeAnalysisDto
        {
            Enabled = s.Enabled,
            AutoAnalyseOnFormat = s.AutoAnalyseOnFormat,
            ProblemsFilter = new ProblemsFilterDto { ShowInfo = s.ProblemsFilter.ShowInfo, ShowWarning = s.ProblemsFilter.ShowWarning, ShowError = s.ProblemsFilter.ShowError },
            RuleOverrides = new Dictionary<string, string>(s.RuleOverrides, StringComparer.Ordinal),   // keys verbatim (R48)
        };
    }

    public async Task<int> ImportAsync(JsonElement section)
    {
        var dto = SettingsJson.Read<CodeAnalysisDto>(section);
        if (dto == null) return 1;
        var current = await _store.GetAsync().ConfigureAwait(false);
        var next = new WebAnalysisSettings
        {
            Enabled = dto.Enabled ?? current.Enabled,
            AutoAnalyseOnFormat = dto.AutoAnalyseOnFormat ?? current.AutoAnalyseOnFormat,
            ProblemsFilter = dto.ProblemsFilter == null
                ? current.ProblemsFilter
                : new ProblemsFilter
                {
                    ShowInfo = dto.ProblemsFilter.ShowInfo ?? current.ProblemsFilter.ShowInfo,
                    ShowWarning = dto.ProblemsFilter.ShowWarning ?? current.ProblemsFilter.ShowWarning,
                    ShowError = dto.ProblemsFilter.ShowError ?? current.ProblemsFilter.ShowError,
                },
            RuleOverrides = dto.RuleOverrides != null
                ? new Dictionary<string, string>(dto.RuleOverrides, StringComparer.Ordinal)
                : current.RuleOverrides,
        };
        await _store.SetAsync(next).ConfigureAwait(false);
        return SettingsJson.CountUnknown(dto);
    }

    public sealed class CodeAnalysisDto : SettingsDto
    {
        public bool? Enabled { get; set; }
        public bool? AutoAnalyseOnFormat { get; set; }
        public ProblemsFilterDto? ProblemsFilter { get; set; }
        public Dictionary<string, string>? RuleOverrides { get; set; }
    }

    public sealed class ProblemsFilterDto : SettingsDto
    {
        public bool? ShowInfo { get; set; }
        public bool? ShowWarning { get; set; }
        public bool? ShowError { get; set; }
    }
}

internal sealed class AiAssistanceSettingsSection : ISettingsSection
{
    private readonly IAiKeyVault _vault;
    private readonly IAiPreference _preference;
    private readonly IAiFeatureSettings _features;

    public AiAssistanceSettingsSection(IAiKeyVault vault, IAiPreference preference, IAiFeatureSettings features)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _preference = preference ?? throw new ArgumentNullException(nameof(preference));
        _features = features ?? throw new ArgumentNullException(nameof(features));
    }

    public string Id => SettingsCatalog.AiAssistance;
    public string Title => "AI assistance";
    public string ExportKey => "aiAssistance";

    public async Task<IReadOnlyList<string>> DescribeResetAsync()
    {
        var providers = await _vault.ListAsync().ConfigureAwait(false);
        var lines = new List<string>();
        lines.Add(providers.Count switch
        {
            0 => "No providers are configured.",
            1 => "This removes your provider and its key.",
            _ => $"This removes your {providers.Count.ToString(CultureInfo.InvariantCulture)} providers and their keys.",
        });
        lines.Add("Privacy modes and ghost text go back to their defaults.");
        lines.Add("Chat history is kept.");
        return lines;
    }

    public async Task ResetAsync()
    {
        // Through RemoveAsync, one provider at a time, so each key goes through the zeroise path.
        foreach (var p in await _vault.ListAsync().ConfigureAwait(false))
            await _vault.RemoveAsync(p.ProviderId).ConfigureAwait(false);
        await _preference.SetActiveAsync(string.Empty).ConfigureAwait(false);
        await _features.ResetAsync().ConfigureAwait(false);
    }

    public async Task<object> ExportAsync()
    {
        var providers = await _vault.ListAsync().ConfigureAwait(false);
        var f = await _features.GetAsync().ConfigureAwait(false);
        var active = await _preference.GetActiveAsync().ConfigureAwait(false);
        return new AiAssistanceDto
        {
            // Name, model and endpoint only: the config list never holds key material (R49).
            Providers = providers.Select(p => new ProviderDto { ProviderId = p.ProviderId, DisplayName = p.DisplayName, Model = p.Model, Endpoint = p.Endpoint }).ToList(),
            ActiveProviderId = string.IsNullOrEmpty(active) ? null : active,
            Privacy = new PrivacyDto { GlobalDefaultMode = f.GlobalDefaultMode, FeatureModeOverrides = new Dictionary<string, AiPrivacyMode>(f.FeatureModeOverrides, StringComparer.Ordinal) },
            GhostText = new GhostTextDto { Enabled = f.GhostTextEnabled, DelayMs = f.GhostTextDelayMs, MaxRequestsPer3s = f.GhostTextMaxRequestsPer3s },
        };
    }

    public async Task<int> ImportAsync(JsonElement section)
    {
        var dto = SettingsJson.Read<AiAssistanceDto>(section);
        if (dto == null) return 1;
        int skipped = SettingsJson.CountUnknown(dto);

        foreach (var p in dto.Providers ?? new List<ProviderDto>())
        {
            if (string.IsNullOrWhiteSpace(p.ProviderId) || string.IsNullOrWhiteSpace(p.Model)) { skipped++; continue; }
            // Never a key: an existing one is kept, a new provider "Needs key" (R49).
            await _vault.UpsertConfigAsync(new AiProviderConfig { ProviderId = p.ProviderId!, DisplayName = p.DisplayName ?? p.ProviderId!, Model = p.Model!, Endpoint = p.Endpoint }).ConfigureAwait(false);
        }

        var f = await _features.GetAsync().ConfigureAwait(false);
        var next = new AiFeatureSettings
        {
            GlobalDefaultMode = dto.Privacy?.GlobalDefaultMode ?? f.GlobalDefaultMode,
            FeatureModeOverrides = dto.Privacy?.FeatureModeOverrides != null
                ? new Dictionary<string, AiPrivacyMode>(dto.Privacy.FeatureModeOverrides, StringComparer.Ordinal)
                : f.FeatureModeOverrides,
            GhostTextEnabled = dto.GhostText?.Enabled ?? f.GhostTextEnabled,
            GhostTextDelayMs = Math.Max(100, dto.GhostText?.DelayMs ?? f.GhostTextDelayMs),
            GhostTextMaxRequestsPer3s = Math.Max(1, dto.GhostText?.MaxRequestsPer3s ?? f.GhostTextMaxRequestsPer3s),
        };
        await _features.SetAsync(next).ConfigureAwait(false);

        // The active provider only if it exists here after the import.
        if (!string.IsNullOrEmpty(dto.ActiveProviderId))
        {
            if (await _vault.GetConfigAsync(dto.ActiveProviderId!).ConfigureAwait(false) != null)
                await _preference.SetActiveAsync(dto.ActiveProviderId!).ConfigureAwait(false);
            else
                skipped++;
        }

        // Fully local never leaves a cloud provider active (spec 028 T012).
        var active = await _preference.GetActiveAsync().ConfigureAwait(false);
        if (next.GlobalDefaultMode == AiPrivacyMode.FullyLocal && !string.IsNullOrEmpty(active) && !AiProviders.IsLocal(active))
            await _preference.SetActiveAsync(string.Empty).ConfigureAwait(false);

        return skipped;
    }

    public sealed class AiAssistanceDto : SettingsDto
    {
        public List<ProviderDto>? Providers { get; set; }
        public string? ActiveProviderId { get; set; }
        public PrivacyDto? Privacy { get; set; }
        public GhostTextDto? GhostText { get; set; }
    }

    public sealed class ProviderDto : SettingsDto
    {
        public string? ProviderId { get; set; }
        public string? DisplayName { get; set; }
        public string? Model { get; set; }
        public string? Endpoint { get; set; }
    }

    public sealed class PrivacyDto : SettingsDto
    {
        public AiPrivacyMode? GlobalDefaultMode { get; set; }
        public Dictionary<string, AiPrivacyMode>? FeatureModeOverrides { get; set; }
    }

    public sealed class GhostTextDto : SettingsDto
    {
        public bool? Enabled { get; set; }
        public int? DelayMs { get; set; }
        public int? MaxRequestsPer3s { get; set; }
    }
}

internal sealed class LayoutSettingsSection : ISettingsSection
{
    private readonly IWorkspaceLayoutStore _layout;

    public LayoutSettingsSection(IWorkspaceLayoutStore layout) => _layout = layout ?? throw new ArgumentNullException(nameof(layout));

    public string Id => "layout";
    public string Title => "Layout";
    public string ExportKey => "layout";

    public Task<IReadOnlyList<string>> DescribeResetAsync() =>
        Task.FromResult<IReadOnlyList<string>>(new[] { "The editor workspace goes back to its default panels, sizes and tabs." });

    public Task ResetAsync() => _layout.ResetAsync();

    public async Task<object> ExportAsync()
    {
        var l = (await _layout.LoadAsync().ConfigureAwait(false));
        return new LayoutDto
        {
            NavVisible = l.NavVisible, SchemaVisible = l.SchemaVisible, SchemaWidth = l.SchemaWidth,
            AiVisible = l.AiVisible, AiWidth = l.AiWidth, AiTab = l.AiTab,
            ResultsVisible = l.ResultsVisible, ResultsHeight = l.ResultsHeight, ResultsMaximised = l.ResultsMaximised, ResultsTab = l.ResultsTab,
        };
    }

    public async Task<int> ImportAsync(JsonElement section)
    {
        var dto = SettingsJson.Read<LayoutDto>(section);
        if (dto == null) return 1;
        await _layout.LoadAsync().ConfigureAwait(false);
        if (dto.NavVisible is { } nav) await _layout.SetAsync(WorkspaceLayoutFields.NavVisible, nav).ConfigureAwait(false);
        if (dto.SchemaVisible is { } sv) await _layout.SetAsync(WorkspaceLayoutFields.SchemaVisible, sv).ConfigureAwait(false);
        if (dto.SchemaWidth is { } sw) await _layout.SetAsync(WorkspaceLayoutFields.SchemaWidth, sw).ConfigureAwait(false);
        if (dto.AiVisible is { } av) await _layout.SetAsync(WorkspaceLayoutFields.AiVisible, av).ConfigureAwait(false);
        if (dto.AiWidth is { } aw) await _layout.SetAsync(WorkspaceLayoutFields.AiWidth, aw).ConfigureAwait(false);
        if (dto.AiTab is WorkspaceLayout.AiTabActions or WorkspaceLayout.AiTabChat) await _layout.SetAsync(WorkspaceLayoutFields.AiTab, dto.AiTab).ConfigureAwait(false);
        if (dto.ResultsVisible is { } rv) await _layout.SetAsync(WorkspaceLayoutFields.ResultsVisible, rv).ConfigureAwait(false);
        if (dto.ResultsHeight is { } rh) await _layout.SetAsync(WorkspaceLayoutFields.ResultsHeight, rh).ConfigureAwait(false);
        if (dto.ResultsMaximised is { } rm) await _layout.SetAsync(WorkspaceLayoutFields.ResultsMaximised, rm).ConfigureAwait(false);
        if (dto.ResultsTab is "results" or "messages" or "problems") await _layout.SetAsync(WorkspaceLayoutFields.ResultsTab, dto.ResultsTab).ConfigureAwait(false);
        return SettingsJson.CountUnknown(dto);
    }

    public sealed class LayoutDto : SettingsDto
    {
        public bool? NavVisible { get; set; }
        public bool? SchemaVisible { get; set; }
        public int? SchemaWidth { get; set; }
        public bool? AiVisible { get; set; }
        public int? AiWidth { get; set; }
        public string? AiTab { get; set; }
        public bool? ResultsVisible { get; set; }
        public int? ResultsHeight { get; set; }
        public bool? ResultsMaximised { get; set; }
        public string? ResultsTab { get; set; }
    }
}
