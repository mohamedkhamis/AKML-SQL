using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace AkmlSql.Engine.Pairing
{
    /// <summary>
    /// Spec 021 (web edition) -- M3 task T064. Persists bearer tokens at rest as SHA-256
    /// hashes (raw tokens never touch disk). See data-model.md E3 (PairingToken) and
    /// contracts/pairing-flow.md.
    ///
    /// File format: a JSON array of <see cref="StoredEntry"/> rows at
    /// <c>%AppData%/AKML SQL Web/tokens.json</c> by default. Atomic writes via temp + rename
    /// (the engine's <c>config.json</c> uses the same pattern).
    /// </summary>
    public sealed class BearerTokenStore
    {
        private readonly string _path;
        private readonly TimeSpan _ttl;
        private readonly Func<DateTimeOffset> _clock;
        private readonly object _lock = new();
        private readonly Dictionary<string, StoredEntry> _entries = new(StringComparer.Ordinal);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public BearerTokenStore(string path, TimeSpan? ttl = null, Func<DateTimeOffset>? clock = null)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _ttl = ttl ?? TimeSpan.FromDays(90);
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            Load();
        }

        /// <summary>
        /// Mint a fresh 256-bit bearer token, store its SHA-256 hash, and return the raw
        /// token to the caller. The caller is responsible for sending the raw token over
        /// the wire and then discarding it -- this class never holds the plaintext.
        /// </summary>
        public string Mint(string browserLabel)
        {
            var rawToken = NewToken();
            var hash = HashToken(rawToken);
            var now = _clock();
            var entry = new StoredEntry
            {
                TokenHash = hash,
                BrowserLabel = browserLabel ?? string.Empty,
                MintedAt = now,
                LastUsedAt = now,
                TtlExpiresAt = now + _ttl,
            };
            lock (_lock)
            {
                _entries[hash] = entry;
                Save();
            }
            Log.Information("BearerTokenStore: minted token for {Browser} (expires {ExpiresAt:O})",
                browserLabel, entry.TtlExpiresAt);
            return rawToken;
        }

        /// <summary>
        /// Validate an inbound bearer token. Returns <see langword="true"/> iff the token
        /// hashes to a stored entry AND the entry has not expired. Bumps <c>LastUsedAt</c>
        /// on success.
        /// </summary>
        public bool Validate(string rawToken)
        {
            if (string.IsNullOrEmpty(rawToken)) return false;
            var hash = HashToken(rawToken);
            lock (_lock)
            {
                if (!_entries.TryGetValue(hash, out var entry)) return false;
                if (_clock() > entry.TtlExpiresAt) return false;
                entry.LastUsedAt = _clock();
                Save();
                return true;
            }
        }

        /// <summary>Remove all stored tokens. The engine UI's "Revoke all" button calls this.</summary>
        public void RevokeAll()
        {
            lock (_lock)
            {
                var count = _entries.Count;
                _entries.Clear();
                Save();
                Log.Information("BearerTokenStore: revoked all ({Count})", count);
            }
        }

        /// <summary>
        /// Revoke a specific token by its hash. The hash is what the engine UI displays
        /// (raw tokens are not shown). Returns <see langword="true"/> iff something was removed.
        /// </summary>
        public bool RevokeByHash(string tokenHash)
        {
            if (string.IsNullOrEmpty(tokenHash)) return false;
            lock (_lock)
            {
                var removed = _entries.Remove(tokenHash);
                if (removed) Save();
                return removed;
            }
        }

        /// <summary>Snapshot of stored entries. The hash is the only identifier exposed.</summary>
        public IReadOnlyList<TokenMetadata> List()
        {
            lock (_lock)
            {
                return _entries.Values
                    .Select(e => new TokenMetadata
                    {
                        TokenHash = e.TokenHash,
                        BrowserLabel = e.BrowserLabel,
                        MintedAt = e.MintedAt,
                        LastUsedAt = e.LastUsedAt,
                        TtlExpiresAt = e.TtlExpiresAt,
                    })
                    .ToList();
            }
        }

        // ---------------- Persistence -----------------------------------

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                var json = File.ReadAllText(_path);
                var loaded = JsonSerializer.Deserialize<StoredEntry[]>(json, JsonOptions);
                if (loaded == null) return;
                lock (_lock)
                {
                    foreach (var entry in loaded)
                    {
                        if (!string.IsNullOrEmpty(entry.TokenHash))
                            _entries[entry.TokenHash] = entry;
                    }
                }
                Log.Information("BearerTokenStore: loaded {Count} tokens from {Path}",
                    _entries.Count, _path);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "BearerTokenStore: failed to load {Path} -- starting empty", _path);
            }
        }

        private void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var tmp = _path + ".tmp";
                var snapshot = _entries.Values.ToArray();
                File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, JsonOptions));
                if (File.Exists(_path)) File.Replace(tmp, _path, destinationBackupFileName: null);
                else File.Move(tmp, _path);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BearerTokenStore: failed to save {Path}", _path);
            }
        }

        // ---------------- Helpers ---------------------------------------

        private static string NewToken()
        {
            // 32 bytes = 256 bits of entropy, hex-encoded for the wire.
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        /// <summary>SHA-256 of the UTF-8 bytes of the raw token, hex-encoded.</summary>
        internal static string HashToken(string rawToken)
        {
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(Encoding.UTF8.GetBytes(rawToken), hash);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        // ---------------- DTOs ------------------------------------------

        /// <summary>Persisted row. <c>TokenHash</c> is the dictionary key.</summary>
        private sealed class StoredEntry
        {
            public string TokenHash { get; set; } = string.Empty;
            public string BrowserLabel { get; set; } = string.Empty;
            public DateTimeOffset MintedAt { get; set; }
            public DateTimeOffset LastUsedAt { get; set; }
            public DateTimeOffset TtlExpiresAt { get; set; }
        }

        /// <summary>Public view returned by <see cref="List"/>.</summary>
        public sealed class TokenMetadata
        {
            public string TokenHash { get; init; } = string.Empty;
            public string BrowserLabel { get; init; } = string.Empty;
            public DateTimeOffset MintedAt { get; init; }
            public DateTimeOffset LastUsedAt { get; init; }
            public DateTimeOffset TtlExpiresAt { get; init; }
        }
    }
}
