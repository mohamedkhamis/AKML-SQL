using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>What a <see cref="CredentialRequest"/> asks for.</summary>
    public static class CredentialAction
    {
        /// <summary>Is there a remembered password for this server and login?</summary>
        public const int Has = 0;

        /// <summary>Delete the remembered password for this server and login.</summary>
        public const int Forget = 1;
    }

    /// <summary>
    /// Web -> Engine (spec 042, FR-104 to FR-106): ask whether a remembered PostgreSQL password exists, or forget it.
    /// The message deliberately has no password field: a remembered password never leaves the engine's protected
    /// store and is never returned to a client. Served only when the engine advertises <c>dialect.postgresql.v1</c>.
    /// </summary>
    [MessagePackObject]
    public class CredentialRequest
    {
        /// <summary>See <see cref="CredentialAction"/>.</summary>
        [Key(0)]
        public int Action { get; set; }

        /// <summary>The <see cref="DatabaseKind"/> (only PostgreSQL is served today).</summary>
        [Key(1)]
        public int Kind { get; set; }

        /// <summary>The host as the user typed it; the engine lower-cases it for the store key.</summary>
        [Key(2)]
        public string Server { get; set; } = string.Empty;

        /// <summary>The port; 0 means the default (5432).</summary>
        [Key(3)]
        public int Port { get; set; }

        [Key(4)]
        public string Login { get; set; } = string.Empty;
    }

    /// <summary>Engine -> Web (spec 042): result of <see cref="CredentialRequest"/>.</summary>
    [MessagePackObject]
    public class CredentialResponse
    {
        [Key(0)]
        public bool Ok { get; set; }

        /// <summary>Has: a remembered password exists. Forget: one existed before it was removed.</summary>
        [Key(1)]
        public bool Has { get; set; }

        [Key(2)]
        public string? Error { get; set; }
    }
}
