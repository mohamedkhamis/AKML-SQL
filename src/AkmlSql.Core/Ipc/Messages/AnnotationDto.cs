#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// A line-level annotation on AI-modified SQL, indicating whether a change is safe or needs review.
    /// </summary>
    [MessagePackObject]
    public class AnnotationDto
    {
        /// <summary>One-based start line of the annotated region.</summary>
        [Key(0)]
        public int StartLine { get; set; }

        /// <summary>One-based end line of the annotated region (inclusive).</summary>
        [Key(1)]
        public int EndLine { get; set; }

        /// <summary>Category of the annotation: "safe" or "review".</summary>
        [Key(2)]
        public string Category { get; set; } = string.Empty;

        /// <summary>Human-readable description of the change in this region.</summary>
        [Key(3)]
        public string Description { get; set; } = string.Empty;
    }
}
