using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class ConnectionInfo
    {
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        [Key(1)]
        public string ConnectionString { get; set; } = string.Empty;

        [Key(2)]
        public int ServerVersion { get; set; } // 13=2016, 14=2017, 15=2019, 16=2022, 17=2025

        [Key(3)]
        public int EngineEdition { get; set; } // 1-4=on-prem, 5=Azure SQL DB, 8=Azure MI

        [Key(4)]
        public string DatabaseName { get; set; } = string.Empty;

        /// <summary>Spec 042: the <see cref="DatabaseKind"/> of the server (0 SQL Server, 1 PostgreSQL). Absent from older peers = 0.</summary>
        [Key(5)]
        public int Kind { get; set; }

        /// <summary>
        /// Spec 042 (FR-104): save the supplied password in the engine's protected store after a successful open.
        /// The password itself travels only inside <see cref="ConnectionString"/>, never in this flag.
        /// </summary>
        [Key(6)]
        public bool RememberPassword { get; set; }
    }
}
