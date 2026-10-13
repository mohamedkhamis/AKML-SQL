using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Web → Engine (spec 041, research R26, contracts/ipc.md): switch the session's current database, as SSMS's
    /// database selector does — on the same physical connection, so #temp tables and SET options survive. Sent
    /// only when the engine advertises <c>session.database.v1</c>.
    /// </summary>
    [MessagePackObject]
    public class ChangeDatabaseRequest
    {
        /// <summary>The canonical web session id.</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>The database to switch to; refused when it holds <c>;</c> <c>=</c> quotes or control characters.</summary>
        [Key(1)]
        public string DatabaseName { get; set; } = string.Empty;
    }

    /// <summary>Engine → Web (spec 041): the result of <see cref="ChangeDatabaseRequest"/>.</summary>
    [MessagePackObject]
    public class ChangeDatabaseResponse
    {
        [Key(0)]
        public bool Ok { get; set; }

        /// <summary>The database the server reports as current after the switch (<c>conn.Database</c>).</summary>
        [Key(1)]
        public string DatabaseName { get; set; } = string.Empty;

        /// <summary>The SQL error (916, 4060 …) or the refusal; null on success.</summary>
        [Key(2)]
        public string? ErrorMessage { get; set; }

        /// <summary>The persistent connection had to be reopened: #temp tables and SET options were lost.</summary>
        [Key(3)]
        public bool ConnectionWasReset { get; set; }
    }
}
