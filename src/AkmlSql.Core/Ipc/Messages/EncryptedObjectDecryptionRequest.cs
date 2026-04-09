using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 014, US19 / FR-098 — request to decrypt an encrypted procedure or
    /// function via the SQL Server Dedicated Admin Connection (DAC). Returns
    /// the plaintext body when DAC permission is available; otherwise reports
    /// <c>DacUnavailable</c> so the shell can render the encrypted placeholder.
    /// Sent Shell → Engine as <see cref="MessageTypes.EncryptedObjectDecryption"/> (92).
    /// </summary>
    [MessagePackObject]
    public class EncryptedObjectDecryptionRequest
    {
        /// <summary>Session id used to look up the active connection.</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Database containing the encrypted object.</summary>
        [Key(1)]
        public string DatabaseName { get; set; } = string.Empty;

        /// <summary>Schema portion of the qualified name (e.g. <c>dbo</c>).</summary>
        [Key(2)]
        public string Schema { get; set; } = string.Empty;

        /// <summary>Object name.</summary>
        [Key(3)]
        public string Name { get; set; } = string.Empty;
    }
}
