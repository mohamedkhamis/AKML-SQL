using System;
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 014, US14 — a single broken-reference object found by
    /// <see cref="FindInvalidObjectsRequest"/>. Carried in
    /// <see cref="FindInvalidObjectsResponse.Records"/>.
    /// </summary>
    [MessagePackObject]
    public class InvalidObjectRecord
    {
        /// <summary>Schema portion of the object's qualified name (e.g. <c>dbo</c>).</summary>
        [Key(0)]
        public string Schema { get; set; } = string.Empty;

        /// <summary>Object name (e.g. <c>vw_OrderSummary</c>).</summary>
        [Key(1)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Object type code, mapped to a friendly label by the shell.
        /// 0 = Table, 1 = View, 2 = Procedure, 3 = Function, 4 = Trigger, 5 = Synonym.
        /// </summary>
        [Key(2)]
        public int Type { get; set; }

        /// <summary>The SQL Server-emitted error message describing the broken reference.</summary>
        [Key(3)]
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>1-based source line in the object's definition where the bad reference appears, when known.</summary>
        [Key(4)]
        public int? SourceLine { get; set; }

        /// <summary>The fully-qualified name of the missing dependency (table / column / proc), when known.</summary>
        [Key(5)]
        public string? MissingDependency { get; set; }

        /// <summary>UTC timestamp the scan recorded this row.</summary>
        [Key(6)]
        public DateTime ScannedAtUtc { get; set; }
    }
}
