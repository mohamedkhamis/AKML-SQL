using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 014, US19 / FR-098 — response from
    /// <see cref="EncryptedObjectDecryptionRequest"/>.
    /// Sent Engine → Shell as <see cref="MessageTypes.EncryptedObjectDecryptionResult"/> (192).
    /// </summary>
    [MessagePackObject]
    public class EncryptedObjectDecryptionResponse
    {
        /// <summary>
        /// Status code:
        /// 0 = Ok (decryption succeeded),
        /// 1 = DacUnavailable (no DAC permission or DAC is disabled on the server),
        /// 2 = NotEncrypted (the object exists but is not encrypted),
        /// 3 = NotFound,
        /// 4 = Error (transient or unexpected failure).
        /// </summary>
        [Key(0)]
        public int Status { get; set; }

        /// <summary>The decrypted CREATE statement when <see cref="Status"/> is Ok.</summary>
        [Key(1)]
        public string? DecryptedScript { get; set; }

        /// <summary>
        /// True when the engine actually performed the XOR/RC4 decryption (vs returning
        /// a non-encrypted object's plain definition). Drives the "decrypted" badge.
        /// </summary>
        [Key(2)]
        public bool WasDecrypted { get; set; }

        /// <summary>Error message when <see cref="Status"/> is non-zero.</summary>
        [Key(3)]
        public string? ErrorMessage { get; set; }
    }
}
