using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 021 (web edition) -- M5 task T104. Browser asks the engine for the canonical
    /// identity of the SQL Server the engine is currently connected to via
    /// <paramref name="SessionId"/>. The returned identity is the cache key for the web
    /// edition's IndexedDB schema cache (clarification 3 in spec.md).
    /// </summary>
    [MessagePackObject]
    public class SchemaIdentifyRequest
    {
        /// <summary>The session whose connection we're identifying.</summary>
        [Key(0)] public string SessionId { get; set; } = string.Empty;
    }
}
