using AkmlSql.Engine.Refactoring;

namespace AkmlSql.Engine.Refactoring.Operations;

/// <summary>
/// Contract for all lightweight (instant, no-dialog) refactoring operations
/// that operate via the FormatAction IPC channel.
/// </summary>
public interface ILightweightOperation
{
    /// <summary>
    /// Apply the operation to the document text (or selection if HasSelection is true).
    /// Returns the modified document text and any non-blocking warnings.
    /// </summary>
    (string modifiedText, string[] warnings) Apply(RefactoringContext context);
}
