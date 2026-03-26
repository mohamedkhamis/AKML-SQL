using System.Text;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Refactoring;
using AkmlSql.Engine.Refactoring.Operations.Heavyweight;
using MessagePack;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring;

/// <summary>
/// T015 — MessagePack serialization round-trip tests for all 5 new IPC models.
/// </summary>
public sealed class RefactoringIpcSerializationTests
{
    [Fact]
    public void RefactorChangeInfo_RoundTrip_AllFieldsMatch()
    {
        var original = new RefactorChangeInfo
        {
            FilePath        = @"C:\scripts\query.sql",
            StartOffset     = 42,
            EndOffset       = 99,
            OldText         = "old_name",
            NewText         = "new_name",
            Line            = 5,
            Column          = 12,
            ContextSnippet  = "-- before\r\nold_name\r\n-- after",
            ChangeCategory  = "rename"
        };

        var bytes       = MessagePackSerializer.Serialize(original);
        var roundTripped = MessagePackSerializer.Deserialize<RefactorChangeInfo>(bytes);

        Assert.Equal(original.FilePath,        roundTripped.FilePath);
        Assert.Equal(original.StartOffset,     roundTripped.StartOffset);
        Assert.Equal(original.EndOffset,       roundTripped.EndOffset);
        Assert.Equal(original.OldText,         roundTripped.OldText);
        Assert.Equal(original.NewText,         roundTripped.NewText);
        Assert.Equal(original.Line,            roundTripped.Line);
        Assert.Equal(original.Column,          roundTripped.Column);
        Assert.Equal(original.ContextSnippet,  roundTripped.ContextSnippet);
        Assert.Equal(original.ChangeCategory,  roundTripped.ChangeCategory);
    }

    [Fact]
    public void RefactorPreviewRequest_RoundTrip_AllFieldsMatch()
    {
        var original = new RefactorPreviewRequest
        {
            SessionId           = "session-abc",
            RequestId           = 7,
            OperationType       = (int)RefactorOperationType.SafeRename,
            Scope               = (int)RefactorScope.CurrentScript,
            DocumentText        = "SELECT col FROM dbo.Table1",
            DocumentPath        = @"C:\work\query.sql",
            SelectionStart      = 10,
            SelectionLength     = 5,
            AdditionalFilePaths = [@"C:\work\other.sql", @"C:\work\third.sql"],
            NewName             = "new_col",
            ExtractedUnitName   = "cte_extract",
            OriginalIdentifier  = "col"
        };

        var bytes        = MessagePackSerializer.Serialize(original);
        var roundTripped = MessagePackSerializer.Deserialize<RefactorPreviewRequest>(bytes);

        Assert.Equal(original.SessionId,           roundTripped.SessionId);
        Assert.Equal(original.RequestId,           roundTripped.RequestId);
        Assert.Equal(original.OperationType,       roundTripped.OperationType);
        Assert.Equal(original.Scope,               roundTripped.Scope);
        Assert.Equal(original.DocumentText,        roundTripped.DocumentText);
        Assert.Equal(original.DocumentPath,        roundTripped.DocumentPath);
        Assert.Equal(original.SelectionStart,      roundTripped.SelectionStart);
        Assert.Equal(original.SelectionLength,     roundTripped.SelectionLength);
        Assert.Equal(original.AdditionalFilePaths, roundTripped.AdditionalFilePaths);
        Assert.Equal(original.NewName,             roundTripped.NewName);
        Assert.Equal(original.ExtractedUnitName,   roundTripped.ExtractedUnitName);
        Assert.Equal(original.OriginalIdentifier,  roundTripped.OriginalIdentifier);
    }

    [Fact]
    public void RefactorPreviewResponse_RoundTrip_AllFieldsMatch()
    {
        var original = new RefactorPreviewResponse
        {
            Changes =
            [
                new RefactorChangeInfo
                {
                    FilePath       = "a.sql",
                    StartOffset    = 0,
                    EndOffset      = 10,
                    OldText        = "foo",
                    NewText        = "bar",
                    Line           = 1,
                    Column         = 1,
                    ContextSnippet = "ctx1",
                    ChangeCategory = "rename"
                },
                new RefactorChangeInfo
                {
                    FilePath       = "b.sql",
                    StartOffset    = 50,
                    EndOffset      = 60,
                    OldText        = "foo2",
                    NewText        = "bar2",
                    Line           = 3,
                    Column         = 5,
                    ContextSnippet = "ctx2",
                    ChangeCategory = "structure"
                }
            ],
            Warnings             = ["warn1", "warn2"],
            Errors               = ["err1"],
            CanApply             = false,
            GeneratedObjectTexts = ["CREATE PROCEDURE dbo.Proc AS BEGIN END"]
        };

        var bytes        = MessagePackSerializer.Serialize(original);
        var roundTripped = MessagePackSerializer.Deserialize<RefactorPreviewResponse>(bytes);

        Assert.Equal(2,     roundTripped.Changes.Length);
        Assert.Equal("a.sql", roundTripped.Changes[0].FilePath);
        Assert.Equal("b.sql", roundTripped.Changes[1].FilePath);
        Assert.Equal(original.Warnings,             roundTripped.Warnings);
        Assert.Equal(original.Errors,               roundTripped.Errors);
        Assert.False(roundTripped.CanApply);
        Assert.Equal(original.GeneratedObjectTexts, roundTripped.GeneratedObjectTexts);
    }

    [Fact]
    public void RefactorApplyRequest_RoundTrip_AllFieldsMatch()
    {
        var original = new RefactorApplyRequest
        {
            SessionId           = "session-xyz",
            RequestId           = 99,
            OperationType       = (int)RefactorOperationType.ExtractToCte,
            ApprovedChanges     =
            [
                new RefactorChangeInfo
                {
                    FilePath       = "c.sql",
                    StartOffset    = 5,
                    EndOffset      = 15,
                    OldText        = "old",
                    NewText        = "new",
                    Line           = 2,
                    Column         = 3,
                    ContextSnippet = "ctx",
                    ChangeCategory = "wrap"
                }
            ],
            CreateBackups       = false,
            FormatAfterRefactor = false,
            SessionProfileName  = "MyProfile"
        };

        var bytes        = MessagePackSerializer.Serialize(original);
        var roundTripped = MessagePackSerializer.Deserialize<RefactorApplyRequest>(bytes);

        Assert.Equal(original.SessionId,          roundTripped.SessionId);
        Assert.Equal(original.RequestId,          roundTripped.RequestId);
        Assert.Equal(original.OperationType,      roundTripped.OperationType);
        Assert.Single(roundTripped.ApprovedChanges);
        Assert.Equal("c.sql",                     roundTripped.ApprovedChanges[0].FilePath);
        Assert.Equal(original.CreateBackups,      roundTripped.CreateBackups);
        Assert.Equal(original.FormatAfterRefactor, roundTripped.FormatAfterRefactor);
        Assert.Equal(original.SessionProfileName, roundTripped.SessionProfileName);
    }

    [Fact]
    public void RefactorApplyResponse_RoundTrip_AllFieldsMatch()
    {
        var original = new RefactorApplyResponse
        {
            Success             = true,
            AppliedCount        = 3,
            FailedFilePaths     = [@"C:\locked.sql"],
            BackupFilePaths     = [@"C:\scripts\query.sql.bak"],
            UpdatedDocumentText = "SELECT 1 -- updated"
        };

        var bytes        = MessagePackSerializer.Serialize(original);
        var roundTripped = MessagePackSerializer.Deserialize<RefactorApplyResponse>(bytes);

        Assert.True(roundTripped.Success);
        Assert.Equal(original.AppliedCount,        roundTripped.AppliedCount);
        Assert.Equal(original.FailedFilePaths,     roundTripped.FailedFilePaths);
        Assert.Equal(original.BackupFilePaths,     roundTripped.BackupFilePaths);
        Assert.Equal(original.UpdatedDocumentText, roundTripped.UpdatedDocumentText);
    }
}

/// <summary>
/// T071 — Undo integration test: apply an inline rename of multiple references, then simulate
/// a single-undo step by reversing each change and verify the original document is restored.
/// SC-006 / FR-009: in-document undo is always possible by reversing the approved changes.
///
/// Note: SafeRenameOperation.ApplyAsync returns empty UpdatedDocumentText for in-memory
/// documents (the shell holds the original and applies changes to its buffer directly).
/// These tests simulate the shell-side apply by applying changes to the original text.
/// </summary>
public sealed class RefactoringUndoIntegrationTests
{
    // SQL document with multiple column references to "sale_amt" (no variables with same name).
    // Using a column-only document avoids the @variable vs column ambiguity.
    private const string Document =
        "SELECT sale_amt, MAX(sale_amt) AS max_sale\r\n" +
        "FROM   dbo.Orders\r\n" +
        "WHERE  sale_amt > 0\r\n" +
        "  AND  sale_amt < 9999\r\n" +
        "GROUP BY sale_amt;\r\n";

    [Fact]
    public async Task Rename_MultipleColumnReferences_PreviewFindsAllOccurrences()
    {
        var op  = new SafeRenameOperation();
        var ctx = BuildContext(Document);

        var request = new RefactorPreviewRequest
        {
            SessionId          = "undo-t1",
            OperationType      = (int)RefactorOperationType.SafeRename,
            Scope              = (int)RefactorScope.CurrentScript,
            DocumentText       = Document,
            OriginalIdentifier = "sale_amt",
            NewName            = "sale_amount"
        };

        var preview = await op.PreviewAsync(request, ctx, CancellationToken.None);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors ?? []));
        Assert.True(preview.Changes.Length >= 2, "Expected at least 2 column references to sale_amt");

        Assert.All(preview.Changes, c =>
        {
            Assert.Contains("sale_amt", c.OldText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("sale_amount", c.NewText, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Rename_ApplyChangesToText_RenamesAllOccurrences()
    {
        var op  = new SafeRenameOperation();
        var ctx = BuildContext(Document);

        var request = new RefactorPreviewRequest
        {
            SessionId          = "undo-t2",
            OperationType      = (int)RefactorOperationType.SafeRename,
            Scope              = (int)RefactorScope.CurrentScript,
            DocumentText       = Document,
            OriginalIdentifier = "sale_amt",
            NewName            = "sale_amount"
        };

        var preview = await op.PreviewAsync(request, ctx, CancellationToken.None);
        Assert.True(preview.CanApply);

        // Simulate what the shell does: apply all changes directly to the original buffer
        var renamedText = ApplyChanges(Document, preview.Changes);

        Assert.DoesNotContain("sale_amt", renamedText, StringComparison.Ordinal);
        Assert.Contains("sale_amount", renamedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rename_SingleUndoStep_FullyRestoresOriginalDocument()
    {
        var op  = new SafeRenameOperation();
        var ctx = BuildContext(Document);

        var request = new RefactorPreviewRequest
        {
            SessionId          = "undo-t3",
            OperationType      = (int)RefactorOperationType.SafeRename,
            Scope              = (int)RefactorScope.CurrentScript,
            DocumentText       = Document,
            OriginalIdentifier = "sale_amt",
            NewName            = "sale_amount"
        };

        var preview = await op.PreviewAsync(request, ctx, CancellationToken.None);
        Assert.True(preview.CanApply);

        // Step 1: apply the rename to get the modified document
        var renamedText = ApplyChanges(Document, preview.Changes);
        Assert.DoesNotContain("sale_amt", renamedText, StringComparison.Ordinal);

        // Step 2: compute undo changes.
        // ApplyChanges applies in descending offset order, so when a lower-offset change
        // is applied it shifts all higher-offset text. In the renamed text the position of
        // each NewText span = original StartOffset + cumulative delta of all lower-offset changes.
        var sortedAscending = preview.Changes.OrderBy(c => c.StartOffset).ToArray();
        int cumulativeDelta = 0;
        var undoChanges = new List<RefactorChangeInfo>();
        foreach (var ch in sortedAscending)
        {
            int renamedStart = ch.StartOffset + cumulativeDelta;
            undoChanges.Add(new RefactorChangeInfo
            {
                FilePath       = ch.FilePath,
                StartOffset    = renamedStart,
                EndOffset      = renamedStart + ch.NewText.Length,
                OldText        = ch.NewText,
                NewText        = ch.OldText,
                ChangeCategory = ch.ChangeCategory
            });
            cumulativeDelta += ch.NewText.Length - ch.OldText.Length;
        }

        // Step 3: apply undo (descending order) to the renamed text
        var restoredText = ApplyChanges(renamedText, undoChanges);

        // SC-006 / FR-009: a single undo step fully restores the original document
        Assert.Equal(Document, restoredText);
    }

    [Fact]
    public async Task Rename_SelectiveApply_UnapprovedReferencesArePreserved()
    {
        var op  = new SafeRenameOperation();
        var ctx = BuildContext(Document);

        var request = new RefactorPreviewRequest
        {
            SessionId          = "undo-t4",
            OperationType      = (int)RefactorOperationType.SafeRename,
            Scope              = (int)RefactorScope.CurrentScript,
            DocumentText       = Document,
            OriginalIdentifier = "sale_amt",
            NewName            = "sale_amount"
        };

        var preview = await op.PreviewAsync(request, ctx, CancellationToken.None);
        Assert.True(preview.CanApply);
        Assert.True(preview.Changes.Length >= 2, "Need at least 2 changes for selective test");

        // Only approve the first change (lowest offset)
        var firstOnly = preview.Changes
            .OrderBy(c => c.StartOffset)
            .Take(1)
            .ToArray();

        var partialText = ApplyChanges(Document, firstOnly);

        // The document must have the renamed identifier where approved
        Assert.Contains("sale_amount", partialText, StringComparison.Ordinal);
        // And must still have the old identifier where NOT approved
        Assert.Contains("sale_amt", partialText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Applies change records to source text in descending offset order (preserves lower offsets).
    /// Replicates the shell's buffer-apply logic for testing purposes.
    /// </summary>
    private static string ApplyChanges(string text, IEnumerable<RefactorChangeInfo> changes)
    {
        var ordered = changes.OrderByDescending(c => c.StartOffset).ToArray();
        var sb = new StringBuilder(text);
        foreach (var ch in ordered)
        {
            if (ch.StartOffset >= 0 && ch.EndOffset <= sb.Length && ch.EndOffset >= ch.StartOffset)
                sb.Remove(ch.StartOffset, ch.EndOffset - ch.StartOffset).Insert(ch.StartOffset, ch.NewText);
        }
        return sb.ToString();
    }

    private static RefactoringContext BuildContext(string documentText)
    {
        var parser = new AkmlSql.Engine.Parser.TsqlParserService();
        var script = parser.Parse(documentText, out _)
                     ?? new Microsoft.SqlServer.TransactSql.ScriptDom.TSqlScript();

        return new RefactoringContext
        {
            DocumentText   = documentText,
            Script         = script,
            Tokens         = parser.GetTokenStream(documentText),
            SessionId      = string.Empty
        };
    }
}
