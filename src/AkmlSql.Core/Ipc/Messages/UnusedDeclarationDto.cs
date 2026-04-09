using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 014, US13 — a single unused declaration (variable or parameter)
    /// reported by <see cref="FindUnusedVariablesRequest"/>.
    /// </summary>
    [MessagePackObject]
    public class UnusedDeclarationDto
    {
        /// <summary>Declaration kind: 0 = Variable (<c>DECLARE @x</c>), 1 = Parameter.</summary>
        [Key(0)]
        public int Kind { get; set; }

        /// <summary>Declared name including the leading <c>@</c> sigil for variables and parameters.</summary>
        [Key(1)]
        public string Name { get; set; } = string.Empty;

        /// <summary>1-based line where the declaration appears.</summary>
        [Key(2)]
        public int DeclaredLine { get; set; }

        /// <summary>1-based column where the declaration appears.</summary>
        [Key(3)]
        public int DeclaredColumn { get; set; }

        /// <summary>
        /// Schema-qualified name of the enclosing procedure / function when the
        /// declaration is a parameter; <c>null</c> for top-level script variables.
        /// </summary>
        [Key(4)]
        public string? EnclosingObject { get; set; }
    }
}
