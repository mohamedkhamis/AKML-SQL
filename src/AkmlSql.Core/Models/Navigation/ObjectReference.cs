namespace AkmlSql.Core.Models.Navigation
{
    /// <summary>A location where a database object is referenced by another object.</summary>
    public class ObjectReference
    {
        /// <summary>Schema of the referencing object.</summary>
        public string ReferencingObjectSchema { get; set; } = string.Empty;

        /// <summary>Name of the referencing object.</summary>
        public string ReferencingObjectName { get; set; } = string.Empty;

        /// <summary>Type: Procedure, View, Function, Trigger.</summary>
        public string ReferencingObjectType { get; set; } = string.Empty;

        /// <summary>Line within the referencing object's definition (if available).</summary>
        public int? ReferenceLine { get; set; }
    }
}
