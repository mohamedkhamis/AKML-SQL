using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Web -> Engine (spec 042, FR-081): replace the literals of PostgreSQL text with placeholders before it is sent
    /// to a cloud AI provider, keeping the structure and identifiers. SQL Server text is redacted in the client with
    /// the T-SQL parser as before; only PostgreSQL goes through the engine.
    /// </summary>
    [MessagePackObject]
    public class RedactRequest
    {
        /// <summary>The dialect of <see cref="Text"/>: <c>postgresql</c> (see <see cref="DatabaseKindNames"/>).</summary>
        [Key(0)]
        public string Dialect { get; set; } = DatabaseKindNames.PostgreSql;

        [Key(1)]
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>Engine -> Web (spec 042): result of <see cref="RedactRequest"/>.</summary>
    [MessagePackObject]
    public class RedactResponse
    {
        /// <summary>False when the text could not be scanned; the client must then not send the original.</summary>
        [Key(0)]
        public bool Ok { get; set; }

        /// <summary>The redacted text; empty when <see cref="Ok"/> is false.</summary>
        [Key(1)]
        public string Text { get; set; } = string.Empty;

        [Key(2)]
        public string? Error { get; set; }
    }
}
