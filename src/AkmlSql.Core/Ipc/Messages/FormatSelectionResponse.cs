using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class FormatSelectionResponse
    {
        [Key(0)]
        public bool Success { get; set; }

        [Key(1)]
        public string FormattedText { get; set; } = string.Empty;

        [Key(2)]
        public int OriginalStart { get; set; }

        [Key(3)]
        public int OriginalEnd { get; set; }

        [Key(4)]
        public bool WasModified { get; set; }

        [Key(5)]
        public bool ValidationPassed { get; set; }

        [Key(6)]
        public long ElapsedMs { get; set; }

        /// <summary>
        /// Spec 040 (STY-09): set when the requested style could not be loaded and the built-in
        /// defaults were used — the same notice as <see cref="FormatResponse.ProfileFallbackWarning"/>.
        /// Null otherwise. Appended key: an older engine omits it and it deserializes null.
        /// </summary>
        [Key(7)]
        public string? ProfileFallbackWarning { get; set; }
    }
}
