using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class EngineStatusInfo
    {
        [Key(0)]
        public int MemoryUsageMB { get; set; }

        [Key(1)]
        public int CachedDatabases { get; set; }

        [Key(2)]
        public int ActiveSessions { get; set; }

        [Key(3)]
        public long UptimeSeconds { get; set; }
    }
}
