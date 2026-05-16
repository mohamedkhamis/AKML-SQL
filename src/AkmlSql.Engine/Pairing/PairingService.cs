using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Serilog;

namespace AkmlSql.Engine.Pairing
{
    /// <summary>
    /// Spec 021 (web edition) -- M3 task T063. Generates a one-time 6-digit pairing PIN at
    /// engine startup (and on regenerate), enforces single-use + 24h TTL, applies a per-source
    /// rate limit of 5 attempts / minute, and validates with constant-time comparison. See
    /// contracts/pairing-flow.md.
    ///
    /// Threading: all state is protected by an internal lock. PIN attempts arrive from the
    /// transport accept loop (one thread) but tests exercise concurrent access; the lock keeps
    /// the rate-limit accounting honest under contention.
    /// </summary>
    public sealed class PairingService
    {
        /// <summary>PIN length in decimal digits.</summary>
        public const int PinLength = 6;

        /// <summary>How long a freshly minted PIN remains valid.</summary>
        public static readonly TimeSpan DefaultPinTtl = TimeSpan.FromHours(24);

        /// <summary>Maximum PIN attempts per source identifier within <see cref="RateLimitWindow"/>.</summary>
        public const int RateLimitMaxAttempts = 5;

        /// <summary>Rolling window for the rate limiter.</summary>
        public static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);

        private readonly object _lock = new();
        private readonly Func<DateTimeOffset> _clock;
        private readonly TimeSpan _pinTtl;

        private string _currentPin = string.Empty;
        private DateTimeOffset _currentPinExpiresAt;
        private bool _currentPinConsumed;

        private readonly ConcurrentDictionary<string, AttemptHistory> _attempts =
            new(StringComparer.Ordinal);

        /// <summary>Fires whenever a new PIN is minted (engine startup, regenerate, refresh after consume).</summary>
        public event EventHandler<string>? PinChanged;

        public PairingService() : this(null, null) { }

        public PairingService(Func<DateTimeOffset>? clock, TimeSpan? pinTtl)
        {
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _pinTtl = pinTtl ?? DefaultPinTtl;
            // Mint an initial PIN immediately so the engine has something to display
            // on its tray UI / installer success page from the moment it starts.
            RegeneratePin();
        }

        /// <summary>Generate a fresh 6-digit PIN; the previous one becomes invalid.</summary>
        public string RegeneratePin()
        {
            var pin = NewPin();
            lock (_lock)
            {
                _currentPin = pin;
                _currentPinExpiresAt = _clock() + _pinTtl;
                _currentPinConsumed = false;
            }
            Log.Information("PairingService: PIN regenerated (expires {ExpiresAt:O})", _currentPinExpiresAt);
            PinChanged?.Invoke(this, pin);
            return pin;
        }

        /// <summary>
        /// Current PIN (visible from the engine UI / installer summary). Returns the empty
        /// string when no PIN has been minted or after the current PIN has been consumed
        /// AND a successor has not yet been requested.
        /// </summary>
        public string CurrentPin
        {
            get
            {
                lock (_lock)
                {
                    return _currentPinConsumed ? string.Empty : _currentPin;
                }
            }
        }

        /// <summary>
        /// Validate an inbound PIN attempt. Applies the per-source rate limit FIRST so that
        /// flooding attempts never consult the PIN store. Returns one of <see cref="PinAttemptResult"/>.
        /// On a successful match the PIN is marked consumed (single-use) -- callers should
        /// immediately mint a bearer token and call <see cref="RegeneratePin"/> if they want
        /// a successor PIN visible from the UI.
        /// </summary>
        /// <param name="sourceId">
        /// Stable identifier for the requester -- the transport passes the remote IP. Used
        /// as the rate-limit bucket key.
        /// </param>
        public PinAttemptResult ValidatePin(string sourceId, string candidatePin)
        {
            if (string.IsNullOrEmpty(sourceId)) sourceId = "unknown";
            if (string.IsNullOrEmpty(candidatePin)) return PinAttemptResult.Invalid;

            // Rate-limit check (per-source rolling window).
            var now = _clock();
            var history = _attempts.GetOrAdd(sourceId, _ => new AttemptHistory());
            lock (history)
            {
                history.PruneOlderThan(now - RateLimitWindow);
                if (history.Count >= RateLimitMaxAttempts)
                {
                    Log.Warning("PairingService: rate-limited PIN attempt from {Source} ({Count} in window)",
                        sourceId, history.Count);
                    return PinAttemptResult.RateLimited;
                }
                history.Add(now);
            }

            // PIN compare (constant-time).
            lock (_lock)
            {
                if (_currentPinConsumed)
                {
                    return PinAttemptResult.Invalid;
                }
                if (now > _currentPinExpiresAt)
                {
                    return PinAttemptResult.Expired;
                }
                if (!ConstantTimeEqualsAscii(candidatePin, _currentPin))
                {
                    return PinAttemptResult.Invalid;
                }
                _currentPinConsumed = true;
                Log.Information("PairingService: PIN consumed (single-use) by {Source}", sourceId);
                return PinAttemptResult.Valid;
            }
        }

        // -------- Helpers ---------------------------------------------------

        private static string NewPin()
        {
            // 6 decimal digits. Uniform pick via rejection sampling on a 32-bit space.
            const int max = 1_000_000;
            var buffer = new byte[4];
            uint value;
            do
            {
                RandomNumberGenerator.Fill(buffer);
                value = BitConverter.ToUInt32(buffer, 0);
            }
            while (value >= (uint.MaxValue / max) * max);

            return (value % max).ToString("D6");
        }

        private static bool ConstantTimeEqualsAscii(string a, string b)
        {
            if (a.Length != b.Length) return false;
            var diff = 0;
            for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        /// <summary>Sliding-window attempt history for a single source identifier.</summary>
        private sealed class AttemptHistory
        {
            // Most workloads will hold at most RateLimitMaxAttempts items. Use a queue for
            // O(1) amortised prune + add.
            private readonly System.Collections.Generic.Queue<DateTimeOffset> _times = new();
            public int Count => _times.Count;
            public void Add(DateTimeOffset t) => _times.Enqueue(t);
            public void PruneOlderThan(DateTimeOffset threshold)
            {
                while (_times.Count > 0 && _times.Peek() < threshold) _times.Dequeue();
            }
        }
    }

    /// <summary>Outcome of <see cref="PairingService.ValidatePin"/>.</summary>
    public enum PinAttemptResult
    {
        /// <summary>PIN matched. Single-use: the PIN is now consumed.</summary>
        Valid,

        /// <summary>PIN did not match (or was empty).</summary>
        Invalid,

        /// <summary>The current PIN's TTL elapsed; the user needs to regenerate from the engine UI.</summary>
        Expired,

        /// <summary>The source IP exceeded the per-minute rate limit.</summary>
        RateLimited,
    }
}
