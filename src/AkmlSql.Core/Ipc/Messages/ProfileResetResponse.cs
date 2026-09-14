using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Result of <see cref="MessageTypes.ProfileReset"/>.
    /// </summary>
    [MessagePackObject]
    public class ProfileResetResponse
    {
        /// <summary>
        /// True when the style now resolves to the shipped built-in. This includes the case where
        /// it already did and nothing was deleted — see <see cref="ChangesDiscarded"/>. Failure
        /// means the style never shipped, so there was no original to reset to.
        /// </summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>Populated iff <see cref="Success"/> is false.</summary>
        [Key(1)]
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// True when an override was actually removed; false when the style was already the
        /// shipped one. Distinguished so the UI can say "reset" or "already the original" rather
        /// than claiming to have undone edits that did not exist.
        /// </summary>
        [Key(2)]
        public bool ChangesDiscarded { get; set; }

        /// <summary>The restored style's file text, verbatim — the shell's new merge base.</summary>
        [Key(3)]
        public string? ProfileJson { get; set; }
    }
}
