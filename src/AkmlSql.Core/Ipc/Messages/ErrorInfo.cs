using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class ErrorInfo
    {
        [Key(0)]
        public int Code { get; set; }

        [Key(1)]
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Spec 042 (contracts/ipc.md): stable ids for the dialect-specific failures. The string ids travel in the
    /// <c>ErrorCode</c> fields of the connection messages; <see cref="ErrorInfo.Code"/> is an int, so the router's
    /// error envelope uses the matching numeric code and starts <see cref="ErrorInfo.Message"/> with the string id.
    /// </summary>
    public static class DialectErrors
    {
        /// <summary>The surface does not exist for the session's database kind (refactorings and Find invalid objects on PostgreSQL).</summary>
        public const string Unsupported = "dialect.unsupported";

        /// <summary>A PostgreSQL connection needs a password the engine does not hold; the client should prompt.</summary>
        public const string PasswordRequired = "dialect.password-required";

        /// <summary>A bridge client sent a PostgreSQL connection the engine refuses (no user name, integrated authentication, relative certificate path).</summary>
        public const string CredentialsRequired = "dialect.credentials-required";

        public const int UnsupportedCode = 4201;
        public const int PasswordRequiredCode = 4202;
        public const int CredentialsRequiredCode = 4203;

        /// <summary>The message of the <see cref="Unsupported"/> reply.</summary>
        public const string UnsupportedMessage = Unsupported + ": Not available for PostgreSQL";
    }
}
