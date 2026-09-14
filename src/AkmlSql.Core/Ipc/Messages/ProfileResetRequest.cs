using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Format Styles editor "Reset to built-in" — discard the user's edits to a shipped style.
    /// <para>
    /// The engine deletes the custom file that shadows the built-in, so the shipped style resolves
    /// again. Nothing ever writes to the built-in file, so what comes back is exactly what shipped
    /// rather than a reconstruction of it.
    /// </para>
    /// </summary>
    [MessagePackObject]
    public class ProfileResetRequest
    {
        /// <summary>Display name of the style to reset.</summary>
        [Key(0)]
        public string Name { get; set; } = string.Empty;
    }
}
