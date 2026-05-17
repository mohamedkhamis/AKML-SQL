using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M2 task T038. IndexedDB-backed persistence of
/// <see cref="FormattingProfile"/> records (per data-model.md E4). Built-in profiles ship
/// as embedded resources under <c>src/AkmlSql.Web/Profiles/</c> and are merged with user
/// profiles at read time. The user can only delete user profiles -- built-ins are
/// read-only.
/// </summary>
public interface IProfileStore
{
    /// <summary>
    /// Return every available profile. Built-ins precede user profiles. Order within each
    /// section is alphabetical by <see cref="FormattingProfile.Name"/>.
    /// </summary>
    Task<IReadOnlyList<ProfileRecord>> ListAsync();

    /// <summary>Read one profile by id. Returns null when the id is unknown.</summary>
    Task<ProfileRecord?> GetAsync(string id);

    /// <summary>Save a user profile. Built-in ids are rejected with <see cref="InvalidOperationException"/>.</summary>
    Task SaveAsync(string id, FormattingProfile profile);

    /// <summary>Remove a user profile. Built-in ids are rejected.</summary>
    Task DeleteAsync(string id);

    /// <summary>Convenience: the active profile id (last selected). Defaults to "builtin.default".</summary>
    Task<string> GetActiveIdAsync();
    Task SetActiveIdAsync(string id);
}

/// <summary>A profile entry exposed to the UI. The <see cref="Origin"/> drives the user
/// interface (can user delete? edit? export?).</summary>
public sealed record ProfileRecord(string Id, string Name, ProfileOrigin Origin, FormattingProfile Profile);

public enum ProfileOrigin { BuiltIn, User, SqlPromptImport }

internal sealed class ProfileStore : IProfileStore
{
    private const string ActiveIdKey = "_active";
    private static readonly string[] BuiltInIds = { "builtin.default", "builtin.ansi" };

    private readonly IIndexedDbAdapter _store;
    private readonly Dictionary<string, ProfileRecord> _builtIns;

    public ProfileStore(IIndexedDbAdapter store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _builtIns = BuildBuiltInProfiles();
    }

    public async Task<IReadOnlyList<ProfileRecord>> ListAsync()
    {
        var userEntries = await _store.ListAsync(StoreNames.Profiles).ConfigureAwait(false);

        var user = userEntries
            .Where(kv => kv.Key != ActiveIdKey && !BuiltInIds.Contains(kv.Key, StringComparer.Ordinal))
            .Select(kv =>
            {
                try
                {
                    var record = JsonSerializer.Deserialize<PersistedProfile>(kv.Value);
                    if (record == null) return null;
                    return new ProfileRecord(kv.Key, record.Name, record.Origin, record.Profile ?? new FormattingProfile());
                }
                catch (JsonException) { return null; }
            })
            .Where(r => r != null)
            .Cast<ProfileRecord>()
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var output = new List<ProfileRecord>(_builtIns.Count + user.Count);
        output.AddRange(_builtIns.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase));
        output.AddRange(user);
        return output;
    }

    public async Task<ProfileRecord?> GetAsync(string id)
    {
        if (_builtIns.TryGetValue(id, out var builtIn)) return builtIn;
        var raw = await _store.GetAsync(StoreNames.Profiles, id).ConfigureAwait(false);
        if (string.IsNullOrEmpty(raw)) return null;
        try
        {
            var record = JsonSerializer.Deserialize<PersistedProfile>(raw);
            if (record == null) return null;
            return new ProfileRecord(id, record.Name, record.Origin, record.Profile ?? new FormattingProfile());
        }
        catch (JsonException) { return null; }
    }

    public Task SaveAsync(string id, FormattingProfile profile)
    {
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("id is required.", nameof(id));
        if (id.StartsWith("builtin.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Built-in profile ids are read-only.");
        }
        var persisted = new PersistedProfile
        {
            Name = string.IsNullOrEmpty(profile.Metadata?.Name) ? id : profile.Metadata!.Name,
            Origin = ProfileOrigin.User,
            Profile = profile,
        };
        return _store.SetAsync(StoreNames.Profiles, id, JsonSerializer.Serialize(persisted));
    }

    public Task DeleteAsync(string id)
    {
        if (id.StartsWith("builtin.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Built-in profile ids cannot be deleted.");
        }
        return _store.DeleteAsync(StoreNames.Profiles, id);
    }

    public async Task<string> GetActiveIdAsync()
    {
        var v = await _store.GetAsync(StoreNames.Profiles, ActiveIdKey).ConfigureAwait(false);
        return string.IsNullOrEmpty(v) ? "builtin.default" : v!;
    }

    public Task SetActiveIdAsync(string id) =>
        _store.SetAsync(StoreNames.Profiles, ActiveIdKey, id ?? "builtin.default");

    // ── Built-in profiles ────────────────────────────────────────────────────

    private static Dictionary<string, ProfileRecord> BuildBuiltInProfiles()
    {
        // We synthesise the built-ins programmatically rather than shipping JSON resources
        // so the web-edition bundle does not need MSBuild EmbeddedResource gymnastics.
        // Future tasks can swap this for a resource-loader if the profile zoo grows.
        var defaultProfile = new FormattingProfile();
        defaultProfile.Metadata.Name = "AKML Default";
        var ansiProfile = new FormattingProfile();
        ansiProfile.Metadata.Name = "ANSI-compact";
        ansiProfile.Casing.ReservedKeywords = "uppercase";

        return new Dictionary<string, ProfileRecord>(StringComparer.Ordinal)
        {
            ["builtin.default"] = new("builtin.default", defaultProfile.Metadata.Name, ProfileOrigin.BuiltIn, defaultProfile),
            ["builtin.ansi"] = new("builtin.ansi", ansiProfile.Metadata.Name, ProfileOrigin.BuiltIn, ansiProfile),
        };
    }

    private sealed class PersistedProfile
    {
        public string Name { get; set; } = string.Empty;
        public ProfileOrigin Origin { get; set; } = ProfileOrigin.User;
        public FormattingProfile? Profile { get; set; }
    }
}
