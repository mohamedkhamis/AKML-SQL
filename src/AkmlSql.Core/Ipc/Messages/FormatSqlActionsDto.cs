using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 040 (STY-11, FR-065) — the interactive Format SQL actions the shell sends with Format
    /// Document and Format Selection (<see cref="FormatRequest.Actions"/> /
    /// <see cref="FormatSelectionRequest.Actions"/>, key 5). Null on the request means "use the
    /// style's own format actions", which is what the CLI, bulk format and the web edition send.
    /// A new instance is today's behaviour: layout and casing on, everything else left alone.
    /// </summary>
    [MessagePackObject]
    public class FormatSqlActionsDto
    {
        /// <summary>Codes for <see cref="Semicolons"/> and <see cref="SquareBrackets"/>.</summary>
        public const int Leave = 0;

        /// <summary><see cref="Semicolons"/>: insert missing terminators.</summary>
        public const int Insert = 1;

        /// <summary><see cref="SquareBrackets"/>: bracket plain identifiers.</summary>
        public const int Add = 1;

        /// <summary><see cref="Semicolons"/> / <see cref="SquareBrackets"/>: remove them.</summary>
        public const int Remove = 2;

        /// <summary>
        /// <see cref="Semicolons"/> / <see cref="SquareBrackets"/>: as the style's own format actions
        /// say. An engine that predates the code reads it as leave.
        /// </summary>
        public const int UseStyle = 3;

        /// <summary>False keeps the original whitespace; only the actions below run.</summary>
        [Key(0)]
        public bool ApplyLayout { get; set; } = true;

        [Key(1)]
        public bool ApplyCasing { get; set; } = true;

        /// <summary>0 leave, 1 insert, 2 remove, 3 as the style says.</summary>
        [Key(2)]
        public int Semicolons { get; set; }

        /// <summary>0 leave, 1 add, 2 remove, 3 as the style says.</summary>
        [Key(3)]
        public int SquareBrackets { get; set; }

        /// <summary>Needs the schema cache and the editor's session.</summary>
        [Key(4)]
        public bool ExpandWildcards { get; set; }

        /// <summary>Needs the schema cache and the editor's session.</summary>
        [Key(5)]
        public bool QualifyObjectNames { get; set; }
    }
}
