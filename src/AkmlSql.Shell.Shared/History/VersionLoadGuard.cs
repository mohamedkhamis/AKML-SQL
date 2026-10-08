#nullable enable
using System.Threading;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Spec 040 (HIS-04) — drops stale version-list responses. Each load takes a token from
    /// <see cref="Begin"/>; when its response arrives, <see cref="IsCurrent"/> is false if a later
    /// load has begun since (the user selected another entry), so the older answer is not shown.
    /// </summary>
    internal sealed class VersionLoadGuard
    {
        private long _latest;

        /// <summary>Starts a load and returns its token.</summary>
        public long Begin() => Interlocked.Increment(ref _latest);

        /// <summary>True while no later load has begun.</summary>
        public bool IsCurrent(long token) => Interlocked.Read(ref _latest) == token;
    }
}
