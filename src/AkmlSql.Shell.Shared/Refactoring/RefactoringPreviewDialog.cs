// TODO (T039 / Phase 4 US2): Implement RefactoringPreviewDialog as a WinForms Form.
// - Left panel: TreeView with file-level nodes and per-reference checkbox children
// - Right panel: read-only RichTextBox showing unified diff (- old / + new)
// - Bottom bar: "Apply Selected" Button + "Cancel" Button
// - ApprovedChanges property returns only the checked RefactorChangeInfo items
// - Constructor accepts RefactorPreviewResponse
// This requires the full .NET 4.7.2 WinForms shell project context.

namespace AkmlSql.Shell.Shared.Refactoring
{
    /// <summary>
    /// WinForms preview dialog stub for Safe Rename and other heavyweight refactoring operations.
    /// </summary>
    internal static class RefactoringPreviewDialog
    {
        // Stub — full WinForms implementation pending (T039)
    }
}
