using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    public enum RefactorOperationType
    {
        SafeRename            = 0,
        ExtractToCte          = 1,
        ExtractToProc         = 2,
        ExtractToDerivedTable = 3,
        EncapsulateAsView     = 4,
        ConvertTempToTableVar = 5,
        ConvertTableVarToTemp = 6,
        ParameterizeValues    = 7,
        SplitTable            = 8,
        InlineExec            = 9,  // Spec 030 T064
        InsertToUpdate        = 10, // Spec 030 T065
        InlineStoredProcedure = 11  // Spec 030 T063
    }

    public enum RefactorScope
    {
        CurrentScript    = 0,
        ProjectDirectory = 1,

        /// <summary>
        /// Spec 030 / FR-018 / R8 — database-wide Smart Rename. The discriminator (not a new
        /// <see cref="RefactorOperationType"/>) for the connected rename path on
        /// <c>SafeRenameOperation</c>: the engine enumerates referencing modules via
        /// <c>sys.sql_expression_dependencies</c> and emits a reviewable <c>sp_rename</c> +
        /// per-dependent <c>ALTER</c> script.
        /// </summary>
        Database         = 2
    }

    [MessagePackObject]
    public class RefactorPreviewRequest
    {
        [Key(0)]  public string   SessionId            { get; set; } = string.Empty;
        [Key(1)]  public int      RequestId            { get; set; }
        [Key(2)]  public int      OperationType        { get; set; }
        [Key(3)]  public int      Scope                { get; set; }
        [Key(4)]  public string   DocumentText         { get; set; } = string.Empty;
        [Key(5)]  public string   DocumentPath         { get; set; } = string.Empty;
        [Key(6)]  public int      SelectionStart       { get; set; }
        [Key(7)]  public int      SelectionLength      { get; set; }
        [Key(8)]  public string[] AdditionalFilePaths  { get; set; } = [];
        [Key(9)]  public string   NewName              { get; set; } = string.Empty;
        [Key(10)] public string   ExtractedUnitName    { get; set; } = string.Empty;
        [Key(11)] public string   OriginalIdentifier   { get; set; } = string.Empty;
    }
}
