using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class FormatPreviewResponse
    {
        [Key(0)]
        public string FormattedText { get; set; } = string.Empty;

        [Key(1)]
        public long ElapsedMs { get; set; }

        /// <summary>
        /// Spec 020 T070 — non-null when stage 6 (SemanticValidator) of the formatter pipeline
        /// rejected the formatted output. <see cref="FormattedText"/> equals the original input
        /// in that case; the editor should render a warning bar with this message.
        /// </summary>
        [Key(2)]
        public string? ValidationError { get; set; }
    }
}
