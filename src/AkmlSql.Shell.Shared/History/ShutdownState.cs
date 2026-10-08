#nullable enable
using System;
using System.Threading;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Spec 040 (HIS-02) — whether SSMS is shutting down, for <see cref="ExecutionCapture"/>: tabs
    /// closed by shutdown stay open in History so the next start can offer them for restore.
    /// <para>
    /// Two signals. <see cref="Begin"/> (DTE <c>OnBeginShutdown</c>, package dispose) is final.
    /// <see cref="CloseQueried"/> (package <c>QueryClose</c>) only means SSMS asked: the user can
    /// still Cancel at a "save changes?" prompt, or another package can refuse. That one lapses as
    /// soon as SSMS shows it is still running (<see cref="StillRunning"/> — a query runs, a document
    /// opens) or after <see cref="QueryWindow"/>; before, it stayed set for the rest of the session,
    /// so every later close left the query "open" and the autosave stopped for good.
    /// </para>
    /// </summary>
    internal sealed class ShutdownState
    {
        /// <summary>How long a close request counts as shutting down without SSMS shutting down.</summary>
        internal static readonly TimeSpan QueryWindow = TimeSpan.FromMinutes(1);

        private volatile bool _begun;
        private long _queriedTicks;

        /// <summary>Shutdown has begun; final.</summary>
        internal void Begin() => _begun = true;

        /// <summary>SSMS asked to close at <paramref name="nowUtc"/>; it may yet be cancelled.</summary>
        internal void CloseQueried(DateTime nowUtc) => Interlocked.Exchange(ref _queriedTicks, nowUtc.Ticks);

        /// <summary>SSMS did something only a running SSMS does: a close request was cancelled.</summary>
        internal void StillRunning() => Interlocked.Exchange(ref _queriedTicks, 0);

        internal bool IsShuttingDown(DateTime nowUtc)
        {
            if (_begun) return true;
            var queried = Interlocked.Read(ref _queriedTicks);
            return queried != 0 && nowUtc.Ticks - queried < QueryWindow.Ticks;
        }
    }
}
