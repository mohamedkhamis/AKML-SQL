using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.SqlPrompt;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M2 task T038. IndexedDB-backed persistence of
/// <see cref="FormattingProfile"/> records (per data-model.md E4). Built-in profiles ship
/// as embedded resources under <c>src/AkmlSql.Web/Profiles/</c> and are merged with user
/// profiles at read time.
/// <para>
/// Styles also live on a paired engine: when the engine advertises
/// <c>styles.sqlprompt.v1</c>, its styles — the same styles folder SSMS uses —
/// are listed alongside the browser's (ids <c>engine:&lt;name&gt;</c>) and new styles are saved
/// there. Without an engine, styles are kept in this browser.
/// </para>
/// </summary>
public interface IProfileStore
{
    /// <summary>
    /// Return every available profile. Built-ins precede user profiles; engine styles follow the
    /// browser's. Order within each section is alphabetical by name. Engine entries carry only
    /// metadata (<see cref="ProfileRecord.IsSummary"/>); <see cref="GetAsync"/> loads the style.
    /// </summary>
    Task<IReadOnlyList<ProfileRecord>> ListAsync();

    /// <summary>Read one profile by id. Returns null when the id is unknown or its engine is away.</summary>
    Task<ProfileRecord?> GetAsync(string id);

    /// <summary>Save a user profile. Built-in ids are rejected with <see cref="InvalidOperationException"/>.</summary>
    Task SaveAsync(string id, FormattingProfile profile);

    /// <summary>Remove a user profile. Built-in ids are rejected.</summary>
    Task DeleteAsync(string id);

    /// <summary>Convenience: the active profile id (last selected). Defaults to "builtin.khamis".</summary>
    Task<string> GetActiveIdAsync();
    Task SetActiveIdAsync(string id);

    // ── SQL Prompt styles ────────────────────────────────────────────────────

    /// <summary>True when a paired engine stores styles (new styles are saved there).</summary>
    bool EngineStylesAvailable { get; }

    /// <summary>
    /// Why the last listing could not ask the engine for its styles (a timeout, an engine error),
    /// or null when it could. The browser's styles are listed either way, with the engine's styles
    /// as last listed; a new style is not created on the engine while this is set, because its
    /// name cannot be checked.
    /// </summary>
    string? EngineStylesError { get; }

    /// <summary>Raised when styles were added, changed, renamed or deleted, or the engine came or went.</summary>
    event Action? StylesChanged;

    /// <summary>
    /// The style as a SQL Prompt document — what the style editor edits. A style written in
    /// AKML's own model comes back as its closest SQL Prompt reading.
    /// </summary>
    Task<SqlPromptStyleDocument?> GetDocumentAsync(string id);

    /// <summary>
    /// The style's SQL Prompt document together with its record, or null. For an engine style
    /// both come from ONE engine request — the styles page used to ask the engine twice per click.
    /// </summary>
    Task<(SqlPromptStyleDocument Document, ProfileRecord Record)?> OpenAsync(string id);

    /// <summary>
    /// Saves <paramref name="document"/> over the style <paramref name="id"/> (a built-in gets an
    /// edited copy that shadows it, like SSMS), or as a new style when
    /// <paramref name="id"/> is null — on the engine when one is paired, otherwise in this browser.
    /// </summary>
    Task<ProfileRecord> SaveDocumentAsync(string? id, SqlPromptStyleDocument document);

    /// <summary>Renames a user style (not a built-in).</summary>
    Task<ProfileRecord> RenameAsync(string id, string newName);

    /// <summary>Discards the edits to a built-in style; returns the shipped style.</summary>
    Task<ProfileRecord?> ResetAsync(string id);
}

/// <summary>A profile entry exposed to the UI. The <see cref="Origin"/> drives the user
/// interface (can user delete? edit? export?).</summary>
public sealed record ProfileRecord(string Id, string Name, ProfileOrigin Origin, FormattingProfile Profile)
{
    /// <summary>Where the style is kept: this browser, or the paired engine (shared with SSMS).</summary>
    public ProfileLocation Location { get; init; } = ProfileLocation.Browser;

    /// <summary>A built-in style the user has edited (Reset brings the shipped one back).</summary>
    public bool IsCustomizedBuiltIn { get; init; }

    /// <summary>True when the style is written in SQL Prompt's model.</summary>
    public bool IsSqlPromptStyle { get; init; }

    /// <summary>True for list entries whose <see cref="Profile"/> is not loaded (engine styles).</summary>
    public bool IsSummary { get; init; }
}

public enum ProfileOrigin { BuiltIn, User, SqlPromptImport }

public enum ProfileLocation { Browser, Engine }

internal sealed class ProfileStore : IProfileStore
{
    private const string ActiveIdKey = "_active";
    private const string OverridePrefix = "override:";
    internal const string EnginePrefix = "engine:";
    private const string CapabilityStyles = "styles.sqlprompt.v1";

    // Spec 032 J3 (FR-031): the web edition ships the SAME product built-ins as the
    // desktop (Khamis Style + Collapsed, from the embedded spec-031 .akmlstyle
    // definitions), with Khamis Style active by default. builtin.default/ansi stay for
    // persisted references.
    private static readonly string[] BuiltInIds = { "builtin.khamis", "builtin.collapsed", "builtin.default", "builtin.ansi" };
    private const string DefaultActiveId = "builtin.khamis";

    private readonly IIndexedDbAdapter _store;
    private readonly IEngineBridge? _bridge;
    private readonly Dictionary<string, ProfileRecord> _builtIns;

    public ProfileStore(IIndexedDbAdapter store) : this(store, null) { }

    public ProfileStore(IIndexedDbAdapter store, IEngineBridge? bridge)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _bridge = bridge;
        _builtIns = BuildBuiltInProfiles();
        if (_bridge != null) _bridge.StateChanged += _ => StylesChanged?.Invoke();
    }

    public event Action? StylesChanged;

    public bool EngineStylesAvailable =>
        _bridge != null &&
        _bridge.State == BridgeState.Open &&
        Array.IndexOf(_bridge.EngineCapabilities, CapabilityStyles) >= 0;

    public string? EngineStylesError { get; private set; }

    // The engine's styles as last listed. A transient listing failure keeps showing them: an
    // empty list made the active engine style vanish from the editor's picker, which then
    // switched Format to another style the user never chose.
    private IReadOnlyList<ProfileRecord> _lastEngineStyles = Array.Empty<ProfileRecord>();

    public async Task<IReadOnlyList<ProfileRecord>> ListAsync()
    {
        var entries = await _store.ListAsync(StoreNames.Profiles).ConfigureAwait(false);
        var overrides = new Dictionary<string, PersistedProfile>(StringComparer.Ordinal);
        var user = new List<ProfileRecord>();
        foreach (var kv in entries)
        {
            if (kv.Key == ActiveIdKey || BuiltInIds.Contains(kv.Key, StringComparer.Ordinal)) continue;
            var persisted = SafeDeserialize(kv.Value);
            if (persisted == null) continue;
            if (kv.Key.StartsWith(OverridePrefix, StringComparison.Ordinal))
            {
                overrides[kv.Key[OverridePrefix.Length..]] = persisted;
                continue;
            }
            user.Add(ToRecord(kv.Key, persisted));
        }

        var output = new List<ProfileRecord>();
        foreach (var builtIn in _builtIns.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            output.Add(overrides.TryGetValue(builtIn.Id, out var edited)
                ? ToRecord(builtIn.Id, edited) with { Origin = ProfileOrigin.BuiltIn, IsCustomizedBuiltIn = true }
                : builtIn);
        }
        output.AddRange(user.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase));
        output.AddRange(await ListEngineAsync().ConfigureAwait(false));
        return output;
    }

    public async Task<ProfileRecord?> GetAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (id.StartsWith(EnginePrefix, StringComparison.Ordinal)) return await GetEngineAsync(id).ConfigureAwait(false);
        if (_builtIns.TryGetValue(id, out var builtIn))
        {
            var edited = SafeDeserialize(await _store.GetAsync(StoreNames.Profiles, OverridePrefix + id).ConfigureAwait(false));
            return edited == null
                ? builtIn
                : ToRecord(id, edited) with { Origin = ProfileOrigin.BuiltIn, IsCustomizedBuiltIn = true };
        }
        var record = SafeDeserialize(await _store.GetAsync(StoreNames.Profiles, id).ConfigureAwait(false));
        return record == null ? null : ToRecord(id, record);
    }

    public Task SaveAsync(string id, FormattingProfile profile)
    {
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("id is required.", nameof(id));
        if (id.StartsWith("builtin.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Built-in profile ids are read-only.");
        }
        return PutBrowserAsync(id, profile, ProfileOrigin.User);
    }

    public async Task DeleteAsync(string id)
    {
        if (id.StartsWith("builtin.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Built-in profile ids cannot be deleted.");
        }
        if (id.StartsWith(EnginePrefix, StringComparison.Ordinal))
        {
            var response = await SendAsync<ProfileDeleteRequest, ProfileDeleteResponse>(
                MessageTypes.ProfileDelete, new ProfileDeleteRequest { Name = EngineName(id) }).ConfigureAwait(false);
            if (!response.Success) throw new InvalidOperationException(response.ErrorMessage ?? "The engine could not delete the style.");
        }
        else
        {
            await _store.DeleteAsync(StoreNames.Profiles, id).ConfigureAwait(false);
        }
        StylesChanged?.Invoke();
    }

    public async Task<string> GetActiveIdAsync()
    {
        var v = await _store.GetAsync(StoreNames.Profiles, ActiveIdKey).ConfigureAwait(false);
        if (string.IsNullOrEmpty(v)) return DefaultActiveId;

        // Spec 032 J3: a persisted active id that no longer resolves (deleted user
        // profile, renamed built-in, engine away) falls back to the product default. The
        // stored choice is kept, so an engine style is active again once the engine is back.
        if (_builtIns.ContainsKey(v!)) return v!;
        if (v!.StartsWith(EnginePrefix, StringComparison.Ordinal))
            return EngineStylesAvailable ? v : DefaultActiveId;
        var stored = await _store.GetAsync(StoreNames.Profiles, v!).ConfigureAwait(false);
        return string.IsNullOrEmpty(stored) ? DefaultActiveId : v!;
    }

    public Task SetActiveIdAsync(string id) =>
        _store.SetAsync(StoreNames.Profiles, ActiveIdKey, id ?? DefaultActiveId);

    // ── SQL Prompt styles ────────────────────────────────────────────────────

    public async Task<SqlPromptStyleDocument?> GetDocumentAsync(string id) =>
        (await OpenAsync(id).ConfigureAwait(false))?.Document;

    public async Task<(SqlPromptStyleDocument Document, ProfileRecord Record)?> OpenAsync(string id)
    {
        if (id.StartsWith(EnginePrefix, StringComparison.Ordinal))
        {
            if (!EngineStylesAvailable) return null;
            var response = await SendAsync<ProfileGetRequest, ProfileGetResponse>(
                MessageTypes.ProfileGet, new ProfileGetRequest { Name = EngineName(id) }).ConfigureAwait(false);
            var record = EngineRecord(response);
            if (record == null) return null;
            SqlPromptStyleDocument document;
            if (!string.IsNullOrEmpty(response.SqlPromptJson))
            {
                // The engine writes every option out (for editors that cannot load this library);
                // keep SQL Prompt's minimal form here.
                document = SqlPromptStyleDocument.Parse(response.SqlPromptJson!);
                document.Minimize();
            }
            else
            {
                document = SqlPromptStyles.ToDocument(record.Profile);
            }
            return (document, record);
        }
        var browser = await GetAsync(id).ConfigureAwait(false);
        return browser == null ? null : (SqlPromptStyles.ToDocument(browser.Profile), browser);
    }

    public async Task<ProfileRecord> SaveDocumentAsync(string? id, SqlPromptStyleDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(document.Name)) throw new ArgumentException("A style needs a name.", nameof(document));

        ProfileRecord saved;
        if (id == null)
        {
            // A new style: shared with SSMS when an engine is paired.
            document.Name = document.Name.Trim();
            var existing = await ListAsync().ConfigureAwait(false);
            if (EngineStylesAvailable && EngineStylesError != null)
                throw new InvalidOperationException(
                    $"The engine's styles could not be listed ({EngineStylesError.TrimEnd('.')}), so the new style's name cannot be checked. Try again in a moment.");
            if (existing.Any(r => string.Equals(r.Name, document.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"A style named '{document.Name}' already exists.");
            // Styles are keyed by name; the id is SQL Prompt's, kept so an exported copy is
            // recognised by SQL Prompt as the same style.
            if (string.IsNullOrWhiteSpace(document.Id)) document.Id = Guid.NewGuid().ToString();
            saved = EngineStylesAvailable
                ? await SaveEngineAsync(document, previous: null, createOnly: true).ConfigureAwait(false)
                : await SaveBrowserAsync("user." + Guid.NewGuid().ToString("N")[..12], document, previous: null, ProfileOrigin.User).ConfigureAwait(false);
        }
        else if (id.StartsWith(EnginePrefix, StringComparison.Ordinal))
        {
            var current = await GetEngineAsync(id).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The engine that keeps this style is not connected.");
            document.Name = current.Name;
            saved = await SaveEngineAsync(document, current.Profile).ConfigureAwait(false);
        }
        else if (_builtIns.TryGetValue(id, out var builtIn))
        {
            // An edited copy shadows the built-in, exactly as SSMS does it.
            document.Name = builtIn.Name;
            saved = await SaveBrowserAsync(OverridePrefix + id, document, builtIn.Profile, ProfileOrigin.BuiltIn).ConfigureAwait(false)
                with { Id = id, IsCustomizedBuiltIn = true };
        }
        else
        {
            var current = await GetAsync(id).ConfigureAwait(false);
            saved = await SaveBrowserAsync(id, document, current?.Profile, ProfileOrigin.User).ConfigureAwait(false);
        }
        StylesChanged?.Invoke();
        return saved;
    }

    public async Task<ProfileRecord> RenameAsync(string id, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("A style needs a name.", nameof(newName));
        newName = newName.Trim();
        if (id.StartsWith("builtin.", StringComparison.Ordinal))
            throw new InvalidOperationException("Built-in styles keep their names. Save a copy under a new name instead.");

        var all = await ListAsync().ConfigureAwait(false);
        if (all.Any(r => r.Id != id && string.Equals(r.Name, newName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A style named '{newName}' already exists.");

        ProfileRecord renamed;
        if (id.StartsWith(EnginePrefix, StringComparison.Ordinal))
        {
            var response = await SendAsync<ProfileRenameRequest, ProfileRenameResponse>(
                MessageTypes.ProfileRename, new ProfileRenameRequest { OldName = EngineName(id), NewName = newName }).ConfigureAwait(false);
            if (!response.Success) throw new InvalidOperationException(response.ErrorMessage ?? "The engine could not rename the style.");
            renamed = await GetEngineAsync(EnginePrefix + (response.NewName ?? newName)).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The renamed style could not be read back.");
            if (await _store.GetAsync(StoreNames.Profiles, ActiveIdKey).ConfigureAwait(false) == id)
                await SetActiveIdAsync(renamed.Id).ConfigureAwait(false);
        }
        else
        {
            var record = await GetAsync(id).ConfigureAwait(false) ?? throw new InvalidOperationException("The style no longer exists.");
            var profile = record.Profile;
            profile.Metadata.Name = newName;
            if (profile.SqlPrompt != null) profile.SqlPrompt["metadata"] = new System.Text.Json.Nodes.JsonObject
            {
                ["id"] = profile.Metadata.Id,
                ["name"] = newName,
            };
            await PutBrowserAsync(id, profile, record.Origin).ConfigureAwait(false);
            renamed = record with { Name = newName };
        }
        StylesChanged?.Invoke();
        return renamed;
    }

    public async Task<ProfileRecord?> ResetAsync(string id)
    {
        if (id.StartsWith(EnginePrefix, StringComparison.Ordinal))
        {
            var response = await SendAsync<ProfileResetRequest, ProfileResetResponse>(
                MessageTypes.ProfileReset, new ProfileResetRequest { Name = EngineName(id) }).ConfigureAwait(false);
            if (!response.Success) throw new InvalidOperationException(response.ErrorMessage ?? "The engine could not reset the style.");
        }
        else if (_builtIns.ContainsKey(id))
        {
            await _store.DeleteAsync(StoreNames.Profiles, OverridePrefix + id).ConfigureAwait(false);
        }
        else
        {
            throw new InvalidOperationException("Only built-in styles can be reset.");
        }
        StylesChanged?.Invoke();
        return await GetAsync(id).ConfigureAwait(false);
    }

    // ── browser storage ──────────────────────────────────────────────────────

    private async Task<ProfileRecord> SaveBrowserAsync(string key, SqlPromptStyleDocument document, FormattingProfile? previous, ProfileOrigin origin)
    {
        var profile = SqlPromptStyles.ToProfile(document, previous);
        await PutBrowserAsync(key, profile, origin).ConfigureAwait(false);
        return ToRecord(key, new PersistedProfile { Name = profile.Metadata.Name, Origin = origin, Profile = profile });
    }

    private Task PutBrowserAsync(string key, FormattingProfile profile, ProfileOrigin origin)
    {
        var persisted = new PersistedProfile
        {
            Name = string.IsNullOrEmpty(profile.Metadata?.Name) ? key : profile.Metadata!.Name,
            Origin = origin,
            Profile = profile,
        };
        return _store.SetAsync(StoreNames.Profiles, key, JsonSerializer.Serialize(persisted));
    }

    private static ProfileRecord ToRecord(string id, PersistedProfile persisted)
    {
        var profile = persisted.Profile ?? new FormattingProfile();
        return new ProfileRecord(id, persisted.Name, persisted.Origin, profile)
        {
            IsSqlPromptStyle = profile.SqlPrompt != null,
        };
    }

    private static PersistedProfile? SafeDeserialize(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<PersistedProfile>(json); }
        catch (JsonException) { return null; }
    }

    // ── engine storage (shared with SSMS) ────────────────────

    private static string EngineName(string id) => id[EnginePrefix.Length..];

    /// <summary>
    /// The engine's styles, or none when it cannot list them. It never throws: the built-in and
    /// browser styles do not need the engine, and a slow or failing engine (the 15 s timeout, an
    /// engine error, a response without a list) used to fail the whole listing, taking the
    /// styles page and the editor's style picker down with it.
    /// </summary>
    private async Task<IReadOnlyList<ProfileRecord>> ListEngineAsync()
    {
        EngineStylesError = null;
        if (!EngineStylesAvailable) return _lastEngineStyles = Array.Empty<ProfileRecord>();
        try
        {
            var response = await SendAsync<ProfileListRequest, ProfileListResponse>(MessageTypes.ProfileList, new ProfileListRequest()).ConfigureAwait(false);
            if (response?.Profiles == null)
            {
                EngineStylesError = "The engine did not return its styles.";
                return _lastEngineStyles;
            }
            return _lastEngineStyles = response.Profiles
                .OrderBy(p => p.IsBuiltIn || p.IsCustomizedBuiltIn ? 0 : 1)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(p =>
                {
                    var summary = new FormattingProfile();
                    summary.Metadata.Name = p.Name;
                    summary.Metadata.Description = p.Description ?? string.Empty;
                    return new ProfileRecord(EnginePrefix + p.Name, p.Name,
                        p.IsBuiltIn || p.IsCustomizedBuiltIn ? ProfileOrigin.BuiltIn : ProfileOrigin.User, summary)
                    {
                        Location = ProfileLocation.Engine,
                        IsCustomizedBuiltIn = p.IsCustomizedBuiltIn,
                        IsSqlPromptStyle = p.IsSqlPromptStyle,
                        IsSummary = true,
                    };
                })
                .ToList();
        }
        catch (Exception ex)
        {
            // A bridge that closed mid-request is not an error worth showing: the engine is gone.
            if (_bridge?.State != BridgeState.Open) return _lastEngineStyles = Array.Empty<ProfileRecord>();
            EngineStylesError = ex is OperationCanceledException
                ? "The engine did not answer in time."
                : ex.Message;
            return _lastEngineStyles;
        }
    }

    private async Task<ProfileRecord?> GetEngineAsync(string id)
    {
        if (!EngineStylesAvailable) return null;
        var response = await SendAsync<ProfileGetRequest, ProfileGetResponse>(
            MessageTypes.ProfileGet, new ProfileGetRequest { Name = EngineName(id) }).ConfigureAwait(false);
        return EngineRecord(response);
    }

    private static ProfileRecord? EngineRecord(ProfileGetResponse response)
    {
        if (!response.Success || response.ProfileJson == null) return null;
        var profile = ProfileSerializer.Deserialize(response.ProfileJson);
        var name = response.Name ?? profile.Metadata.Name;
        return new ProfileRecord(EnginePrefix + name, name,
            response.HasBuiltIn ? ProfileOrigin.BuiltIn : ProfileOrigin.User, profile)
        {
            Location = ProfileLocation.Engine,
            IsCustomizedBuiltIn = response.IsCustomizedBuiltIn,
            IsSqlPromptStyle = response.IsSqlPromptStyle,
        };
    }

    private async Task<ProfileRecord> SaveEngineAsync(SqlPromptStyleDocument document, FormattingProfile? previous, bool createOnly = false)
    {
        var profile = SqlPromptStyles.ToProfile(document, previous);
        var response = await SendAsync<ProfileSaveRequest, ProfileSaveResponse>(MessageTypes.ProfileSave, new ProfileSaveRequest
        {
            Name = profile.Metadata.Name,
            ProfileJson = ProfileSerializer.Serialize(profile),
            Description = profile.Metadata.Description,
            BasedOn = profile.Metadata.BasedOn,
            // A new style: the engine refuses a built-in's or a taken name itself.
            CreateOnly = createOnly,
        }).ConfigureAwait(false);
        if (!response.Success) throw new InvalidOperationException(response.ErrorMessage ?? "The engine could not save the style.");
        return await GetEngineAsync(EnginePrefix + profile.Metadata.Name).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The saved style could not be read back from the engine.");
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(int type, TRequest request)
        where TRequest : class where TResponse : class
    {
        if (_bridge == null || _bridge.State != BridgeState.Open)
            throw new InvalidOperationException("No engine is connected.");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        return await _bridge.SendAsync<TRequest, TResponse>(type, request, cts.Token).ConfigureAwait(false);
    }

    // ── Built-in profiles ────────────────────────────────────────────────────

    /// <summary>
    /// Single source of truth for built-in profile construction. Both the runtime
    /// (<see cref="BuildBuiltInProfiles"/>) and the spec-024 parity test harness
    /// (<c>ParityCorpusLoader.GetProfile</c>) call this so a tweak in either default
    /// profile cannot drift between production behaviour and the parity baseline.
    /// <paramref name="shortId"/> is the unqualified id (<c>"default"</c> / <c>"ansi"</c>),
    /// not the <c>"builtin.*"</c> form.
    /// </summary>
    internal static FormattingProfile CreateBuiltInProfile(string shortId)
    {
        switch (shortId)
        {
            case "default":
            {
                var p = new FormattingProfile();
                p.Metadata.Name = "AKML Default";
                return p;
            }
            case "ansi":
            {
                var p = new FormattingProfile();
                p.Metadata.Name = "ANSI-compact";
                p.Casing.ReservedKeywords = "uppercase";
                return p;
            }
            default:
                throw new ArgumentException(
                    $"Unknown built-in profile short id '{shortId}'. Expected 'default' or 'ansi'.",
                    nameof(shortId));
        }
    }

    private static Dictionary<string, ProfileRecord> BuildBuiltInProfiles()
    {
        var defaultProfile = CreateBuiltInProfile("default");
        var ansiProfile = CreateBuiltInProfile("ansi");

        var builtIns = new Dictionary<string, ProfileRecord>(StringComparer.Ordinal);

        // Spec 032 J3: the product defaults, read from the SAME embedded .akmlstyle
        // definitions the desktop ships (AkmlSql.Formatting/Profiles/BuiltIn) — the
        // campaign formatted with POCO defaults because the web had no Khamis Style.
        var khamis = LoadEmbeddedBuiltIn("khamis-style.akmlstyle");
        if (khamis != null)
            builtIns["builtin.khamis"] = new("builtin.khamis", khamis.Metadata.Name, ProfileOrigin.BuiltIn, khamis);
        var collapsed = LoadEmbeddedBuiltIn("collapsed.akmlstyle");
        if (collapsed != null)
            builtIns["builtin.collapsed"] = new("builtin.collapsed", collapsed.Metadata.Name, ProfileOrigin.BuiltIn, collapsed);

        builtIns["builtin.default"] = new("builtin.default", defaultProfile.Metadata.Name, ProfileOrigin.BuiltIn, defaultProfile);
        builtIns["builtin.ansi"] = new("builtin.ansi", ansiProfile.Metadata.Name, ProfileOrigin.BuiltIn, ansiProfile);
        return builtIns;
    }

    private static FormattingProfile? LoadEmbeddedBuiltIn(string fileName)
    {
        try
        {
            var assembly = typeof(FormattingProfile).Assembly;
            using var stream = assembly.GetManifestResourceStream($"AkmlSql.Formatting.Profiles.BuiltIn.{fileName}");
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            return ProfileSerializer.Deserialize(reader.ReadToEnd());
        }
        catch
        {
            // Missing/corrupt resource must never break web boot — the synthesized
            // built-ins below remain available.
            return null;
        }
    }

    private sealed class PersistedProfile
    {
        public string Name { get; set; } = string.Empty;
        public ProfileOrigin Origin { get; set; } = ProfileOrigin.User;
        public FormattingProfile? Profile { get; set; }
    }
}
