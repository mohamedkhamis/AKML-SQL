using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class SnippetListResponse
    {
        [Key(0)]
        public SnippetInfo[] Snippets { get; set; } = [];
    }

    [MessagePackObject]
    public class SnippetInfo
    {
        [Key(0)]
        public string Id { get; set; } = string.Empty;

        [Key(1)]
        public string Shortcode { get; set; } = string.Empty;

        [Key(2)]
        public string Name { get; set; } = string.Empty;

        [Key(3)]
        public string Description { get; set; } = string.Empty;

        [Key(4)]
        public string Category { get; set; } = string.Empty;

        [Key(5)]
        public int Source { get; set; } // 1=Personal, 2=Team, 3=BuiltIn

        [Key(6)]
        public bool SurroundsWith { get; set; }

        [Key(7)]
        public int UsageCount { get; set; }

        [Key(8)]
        public string[] Tags { get; set; } = [];

        // Spec 030 T046 / FR-036 — the snippet's custom variable definitions, carried so the Snippet
        // Manager can load, edit, and re-save them without wiping (previously dropped: SnippetInfo had
        // no variables field, so the VM never received them on load). ADDITIVE Key(9): older payloads
        // that omit it deserialize to an empty array (never null), preserving MessagePack back-compat.
        [Key(9)]
        public SnippetVariableInfo[] Variables { get; set; } = [];
    }

    /// <summary>
    /// Spec 030 T046 / FR-036 — a single custom snippet variable definition transported shell↔engine.
    /// Mirrors the engine-side <c>SnippetVariable</c> (name/default/tooltip/schemaAware) so the Snippet
    /// Manager can round-trip variables faithfully. The engine model lives in the Engine project and is
    /// not visible to the shell, so this Core DTO is the shared shape.
    /// </summary>
    [MessagePackObject]
    public class SnippetVariableInfo
    {
        [Key(0)]
        public string Name { get; set; } = string.Empty;

        [Key(1)]
        public string Default { get; set; } = string.Empty;

        [Key(2)]
        public string Tooltip { get; set; } = string.Empty;

        [Key(3)]
        public string? SchemaAware { get; set; }
    }
}
