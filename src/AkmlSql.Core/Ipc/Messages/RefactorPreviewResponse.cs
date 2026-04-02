using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class RefactorPreviewResponse
    {
        /// <summary>All proposed changes sorted by file path then offset descending.</summary>
        [Key(0)] public RefactorChangeInfo[] Changes          { get; set; } = [];

        /// <summary>Non-blocking advisory messages.</summary>
        [Key(1)] public string[]             Warnings         { get; set; } = [];

        /// <summary>Blocking issues that prevent apply (name collision, parse error).</summary>
        [Key(2)] public string[]             Errors           { get; set; } = [];

        /// <summary>False if any blocking errors exist.</summary>
        [Key(3)] public bool                 CanApply         { get; set; } = true;

        /// <summary>New SQL text blocks (proc body, CTE block, view def) for display in preview.</summary>
        [Key(4)] public string[]             GeneratedObjectTexts { get; set; } = [];
    }
}
