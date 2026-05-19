using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 021 (web edition) — M5 task T109. Browser asks the engine for a Phase B
    /// snapshot of the cached schema metadata (columns + foreign keys) for the
    /// given session's database. Phase B is fetched in the background after Phase A
    /// so the browser's first-completion path is never blocked on column metadata.
    /// </summary>
    [MessagePackObject]
    public sealed class SchemaPhaseBRequest
    {
        [Key(0)] public string SessionId { get; set; } = string.Empty;
        [Key(1)] public string DatabaseName { get; set; } = string.Empty;
    }
}
