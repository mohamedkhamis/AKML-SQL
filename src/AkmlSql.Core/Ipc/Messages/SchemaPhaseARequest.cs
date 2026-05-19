using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 021 (web edition) — M5 task T109. Browser asks the engine for a Phase A
    /// snapshot of the cached schema metadata (schemas + object names + types) for
    /// the given session's database. Used to populate the IndexedDB cache so the
    /// browser can serve completion offline.
    /// </summary>
    [MessagePackObject]
    public sealed class SchemaPhaseARequest
    {
        /// <summary>The session whose DatabaseCache we want a Phase A snapshot of.</summary>
        [Key(0)] public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// The database whose cache the browser wants. Browser ships this rather
        /// than letting the engine pick from the session because a multi-database
        /// connection (USE statements) may have moved the session since.
        /// </summary>
        [Key(1)] public string DatabaseName { get; set; } = string.Empty;
    }
}
