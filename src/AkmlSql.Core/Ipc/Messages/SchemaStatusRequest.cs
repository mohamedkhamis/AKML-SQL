using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Shell → Engine: poll request asking for the current schema loading state
    /// for a given session. The shell sends this every ~500ms while the
    /// bottom-right progress indicator margin is visible.
    /// </summary>
    [MessagePackObject]
    public class SchemaStatusRequest
    {
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Engine → Shell: current schema loading state for a session.
    /// </summary>
    [MessagePackObject]
    public class SchemaStatusResponse
    {
        [Key(0)]
        public string DatabaseName { get; set; } = string.Empty;

        /// <summary>
        /// 0 = NotLoaded, 1 = PhaseA (objects), 2 = PhaseB (columns+FKs), 3 = Complete.
        /// Mirrors <c>AkmlSql.Engine.Schema.PopulationPhase</c>.
        /// </summary>
        [Key(1)]
        public int Phase { get; set; }

        /// <summary>Number of objects (tables/views/procs) currently in the cache.</summary>
        [Key(2)]
        public int ObjectCount { get; set; }

        /// <summary>Number of objects that have their column metadata loaded.</summary>
        [Key(3)]
        public int ColumnsLoadedCount { get; set; }

        /// <summary>True if the cache for this session+database exists. False if not started.</summary>
        [Key(4)]
        public bool Exists { get; set; }

        /// <summary>True if the engine's schema load for this session hit a login/permission failure
        /// (4060/18456/18452/916). The shell uses this to surface "credentials rejected" for SQL-auth
        /// sessions. Spec 029.</summary>
        [Key(5)]
        public bool AuthError { get; set; }

        /// <summary>The SQL error number behind <see cref="AuthError"/> (e.g. 18456 login failed,
        /// 18452 untrusted-domain login, 4060 cannot-open-database, 916 no-database-permission), or 0.
        /// Lets the shell distinguish a rejected password (18456/18452 → clear + re-prompt) from a
        /// valid-login-but-no-DB-access case (4060/916 → keep the password). Spec 029 follow-up.</summary>
        [Key(6)]
        public int AuthErrorNumber { get; set; }
    }
}
