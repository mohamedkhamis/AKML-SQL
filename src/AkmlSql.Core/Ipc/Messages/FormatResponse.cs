using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class FormatResponse
    {
        [Key(0)]
        public bool Success { get; set; }

        [Key(1)]
        public string FormattedText { get; set; } = string.Empty;

        [Key(2)]
        public bool WasModified { get; set; }

        [Key(3)]
        public bool ValidationPassed { get; set; }

        [Key(4)]
        public long ElapsedMs { get; set; }

        [Key(5)]
        public FormatDiagnosticInfo[]? Diagnostics { get; set; }

        /// <summary>
        /// Set when the requested <c>ProfileName</c> could not be loaded and formatting silently
        /// used built-in defaults instead — the message names the style and the reason.
        /// <para>
        /// A dedicated field rather than another <see cref="Diagnostics"/> entry because the shell
        /// must distinguish THIS condition from unrelated warnings the pipeline also emits (e.g.
        /// the stage-7 "converged on a second pass" notice), and a successful-but-wrong-style
        /// format is otherwise indistinguishable from a correct one: the format "succeeds", so
        /// <c>FormatFailureNotifier.NotifyIfPreservedAsync</c> deliberately stays silent. That
        /// invisibility is exactly how the shipped default style ("Khamis Style", unloadable due
        /// to a filename/metadata-name mismatch) silently formatted with POCO defaults instead.
        /// </para>
        /// Null on success. Additive/back-compatible: older peers omit key 6 and deserialize null.
        /// </summary>
        [Key(6)]
        public string? ProfileFallbackWarning { get; set; }
    }
}
