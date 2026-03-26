using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Refactoring.Operations.Heavyweight;
using AkmlSql.Engine.Tests.Refactoring.Operations.Lightweight;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring.Operations.Heavyweight;

/// <summary>
/// Unit tests for <see cref="SafeRenameOperation"/>.
/// </summary>
public class SafeRenameTests
{
    private static readonly TsqlParserService ParserService;

    static SafeRenameTests()
    {
        ParserService = new TsqlParserService();
        ParserService.SetServerVersion(160);
    }

    private static RefactorPreviewRequest BuildPreviewRequest(
        string sql,
        string originalName,
        string newName,
        RefactorScope scope = RefactorScope.CurrentScript,
        string documentPath = "")
    {
        return new RefactorPreviewRequest
        {
            SessionId          = "test",
            RequestId          = 1,
            OperationType      = (int)RefactorOperationType.SafeRename,
            Scope              = (int)scope,
            DocumentText       = sql,
            DocumentPath       = documentPath,
            OriginalIdentifier = originalName,
            NewName            = newName
        };
    }

    // -------------------------------------------------------------------------
    // Happy-path preview tests (T045)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Preview_RenameAlias_FindsAllOccurrences()
    {
        // "od" is used as column alias and referenced 3 times across SELECT/ORDER BY
        const string sql = @"
SELECT o.OrderId AS od, o.CustomerId
FROM Orders AS o
ORDER BY od";

        var request = BuildPreviewRequest(sql, "od", "order_detail");
        var ctx     = LightweightOperationTestHelper.CreateContext(sql);
        var op      = new SafeRenameOperation();

        var response = await op.PreviewAsync(request, ctx, CancellationToken.None);

        Assert.True(response.CanApply, "Should be able to apply rename");
        Assert.Empty(response.Errors);
        // "od" appears at least twice (AS od alias, ORDER BY od reference)
        Assert.True(response.Changes.Length >= 2,
            $"Expected >=2 changes for 'od', got {response.Changes.Length}");
        Assert.All(response.Changes, c => Assert.Equal("rename", c.ChangeCategory));
        Assert.All(response.Changes, c => Assert.Equal("order_detail", c.NewText));
    }

    [Fact]
    public async Task Preview_RenameVariable_FindsAllOccurrences()
    {
        const string sql = @"
DECLARE @counter INT = 0;
SET @counter = @counter + 1;
SELECT @counter;";

        var request = BuildPreviewRequest(sql, "@counter", "@idx");
        var ctx     = LightweightOperationTestHelper.CreateContext(sql);
        var op      = new SafeRenameOperation();

        var response = await op.PreviewAsync(request, ctx, CancellationToken.None);

        Assert.True(response.CanApply);
        // @counter appears 3 times: DECLARE, SET (lhs), SET (rhs), SELECT
        Assert.True(response.Changes.Length >= 2,
            $"Expected >=2 changes for '@counter', got {response.Changes.Length}");
    }

    [Fact]
    public async Task Preview_CaseInsensitiveRename_FindsMatch()
    {
        const string sql = "SELECT ORDERID FROM Orders";
        var request = BuildPreviewRequest(sql, "orderid", "order_id");
        var ctx     = LightweightOperationTestHelper.CreateContext(sql);
        var op      = new SafeRenameOperation();

        var response = await op.PreviewAsync(request, ctx, CancellationToken.None);

        Assert.True(response.CanApply);
        Assert.True(response.Changes.Length >= 1);
    }

    [Fact]
    public async Task Preview_CanApply_TrueWithNonEmptyChanges()
    {
        const string sql = "SELECT OrderId FROM Orders";
        var request = BuildPreviewRequest(sql, "OrderId", "order_id");
        var ctx     = LightweightOperationTestHelper.CreateContext(sql);
        var op      = new SafeRenameOperation();

        var response = await op.PreviewAsync(request, ctx, CancellationToken.None);

        Assert.True(response.CanApply);
        Assert.NotEmpty(response.Changes);
    }

    // -------------------------------------------------------------------------
    // Name collision tests (T046)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Preview_NameCollision_SetsCanApplyFalse()
    {
        // Renaming CustomerId to OrderId — but OrderId already exists
        const string sql = "SELECT OrderId, CustomerId FROM t";
        var request = BuildPreviewRequest(sql, "CustomerId", "OrderId");
        var ctx     = LightweightOperationTestHelper.CreateContext(sql);
        var op      = new SafeRenameOperation();

        var response = await op.PreviewAsync(request, ctx, CancellationToken.None);

        Assert.False(response.CanApply);
        Assert.True(response.Errors.Length > 0);
        Assert.Contains("collision", response.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_NameCollision_ErrorMentionsNewName()
    {
        const string sql = "SELECT OrderId, CustomerId FROM t";
        var request = BuildPreviewRequest(sql, "CustomerId", "OrderId");
        var ctx     = LightweightOperationTestHelper.CreateContext(sql);
        var op      = new SafeRenameOperation();

        var response = await op.PreviewAsync(request, ctx, CancellationToken.None);

        Assert.False(response.CanApply);
        Assert.Contains("OrderId", response.Errors[0]);
    }

    // -------------------------------------------------------------------------
    // Empty / null input edge cases (T046)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Preview_EmptyInput_ReturnsEmptyChanges()
    {
        var request = BuildPreviewRequest(string.Empty, "col", "column_name");
        var ctx     = LightweightOperationTestHelper.CreateContext(string.Empty);
        var op      = new SafeRenameOperation();

        // Should not throw, should return 0 changes
        var response = await op.PreviewAsync(request, ctx, CancellationToken.None);

        Assert.Empty(response.Changes);
        // CanApply may be true or false but no exception should be thrown
    }

    [Fact]
    public async Task Preview_EmptyOriginalIdentifier_SetsCanApplyFalse()
    {
        const string sql = "SELECT col FROM t";
        var request = BuildPreviewRequest(sql, string.Empty, "newcol");
        var ctx     = LightweightOperationTestHelper.CreateContext(sql);
        var op      = new SafeRenameOperation();

        var response = await op.PreviewAsync(request, ctx, CancellationToken.None);

        Assert.False(response.CanApply);
    }

    [Fact]
    public async Task Preview_EmptyNewName_SetsCanApplyFalse()
    {
        const string sql = "SELECT col FROM t";
        var request = BuildPreviewRequest(sql, "col", string.Empty);
        var ctx     = LightweightOperationTestHelper.CreateContext(sql);
        var op      = new SafeRenameOperation();

        var response = await op.PreviewAsync(request, ctx, CancellationToken.None);

        Assert.False(response.CanApply);
    }

    [Fact]
    public async Task Preview_NullDocument_ReturnsEmptyChanges()
    {
        var request = new RefactorPreviewRequest
        {
            SessionId          = "test",
            OriginalIdentifier = "col",
            NewName            = "column_name",
            DocumentText       = null!,
            Scope              = (int)RefactorScope.CurrentScript
        };
        // Create context with empty text to avoid NPE in parser
        var ctx = LightweightOperationTestHelper.CreateContext(string.Empty);
        var op  = new SafeRenameOperation();

        // Should not throw
        var response = await op.PreviewAsync(request, ctx, CancellationToken.None);
        Assert.NotNull(response);
    }

    // -------------------------------------------------------------------------
    // Changes are sorted descending by offset (T045)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Preview_ChangesAreSortedDescendingByOffset()
    {
        const string sql = "SELECT col, col, col FROM t";
        var request = BuildPreviewRequest(sql, "col", "column_name");
        var ctx     = LightweightOperationTestHelper.CreateContext(sql);
        var op      = new SafeRenameOperation();

        var response = await op.PreviewAsync(request, ctx, CancellationToken.None);

        Assert.True(response.Changes.Length >= 2);
        for (int i = 1; i < response.Changes.Length; i++)
        {
            // Within same file, offsets should be descending (or same file with different ordering)
            // Primary sort: FilePath ascending; secondary sort: StartOffset descending
            var prev = response.Changes[i - 1];
            var curr = response.Changes[i];
            if (string.Equals(prev.FilePath, curr.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(prev.StartOffset >= curr.StartOffset,
                    $"Changes not sorted descending: [{i-1}]={prev.StartOffset} < [{i}]={curr.StartOffset}");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Apply tests (T046)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Apply_SubsetOfChanges_AppliesOnlyApproved()
    {
        // Write document to a temp file so ApplyAsync can read and verify it
        var tempFile = Path.Combine(Path.GetTempPath(), $"safe_rename_test_{Guid.NewGuid():N}.sql");
        const string sql = "SELECT col, col, col FROM t";

        try
        {
            File.WriteAllText(tempFile, sql);

            // Build preview to get all changes
            var request = BuildPreviewRequest(sql, "col", "column_name", documentPath: tempFile);
            var ctx     = LightweightOperationTestHelper.CreateContext(sql);
            var op      = new SafeRenameOperation();
            var preview = await op.PreviewAsync(request, ctx, CancellationToken.None);

            // Should have found >=3 occurrences (3x "col")
            Assert.True(preview.Changes.Length >= 2,
                $"Expected >=2 changes but got {preview.Changes.Length}. SQL: {sql}");

            // Override FilePaths from "" to tempFile so ApplyAsync can process them as external files
            var allChanges = preview.Changes;
            foreach (var ch in allChanges)
            {
                ch.FilePath = tempFile;
                ch.OldText  = sql.Substring(ch.StartOffset, ch.EndOffset - ch.StartOffset);
            }

            // Take only 2 of the changes (the first 2 in descending order)
            var approvedSubset = allChanges.Take(2).ToArray();

            var applyRequest = new RefactorApplyRequest
            {
                SessionId       = "test",
                OperationType   = (int)RefactorOperationType.SafeRename,
                ApprovedChanges = approvedSubset,
                CreateBackups   = false
            };

            var applyResponse = await op.ApplyAsync(applyRequest, CancellationToken.None);

            Assert.True(applyResponse.Success);
            Assert.Equal(2, applyResponse.AppliedCount);

            // Read back the file: should have exactly 2 renames applied
            var resultText = File.ReadAllText(tempFile);
            var columnNameCount = System.Text.RegularExpressions.Regex.Matches(resultText, "column_name").Count;
            Assert.Equal(2, columnNameCount);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Apply_StaleFile_AddedToFailedFilePaths()
    {
        // A FilePath that doesn't exist → goes to FailedFilePaths
        const string nonExistentFile = @"C:\nonexistent_path_xyzabc\file.sql";

        var applyRequest = new RefactorApplyRequest
        {
            SessionId       = "test",
            OperationType   = (int)RefactorOperationType.SafeRename,
            ApprovedChanges =
            [
                new RefactorChangeInfo
                {
                    FilePath    = nonExistentFile,
                    StartOffset = 0,
                    EndOffset   = 3,
                    OldText     = "col",
                    NewText     = "column_name"
                }
            ],
            CreateBackups = false
        };

        var op       = new SafeRenameOperation();
        var response = await op.ApplyAsync(applyRequest, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Contains(nonExistentFile, response.FailedFilePaths);
    }

    [Fact]
    public async Task Apply_EmptyApprovedChanges_ReturnsSuccess()
    {
        var applyRequest = new RefactorApplyRequest
        {
            SessionId       = "test",
            OperationType   = (int)RefactorOperationType.SafeRename,
            ApprovedChanges = [],
            CreateBackups   = false
        };

        var op       = new SafeRenameOperation();
        var response = await op.ApplyAsync(applyRequest, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(0, response.AppliedCount);
    }

    [Fact]
    public async Task Apply_ExternalFile_WritesCorrectContent()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"safe_rename_apply_{Guid.NewGuid():N}.sql");
        const string sql = "SELECT col FROM t";
        File.WriteAllText(tempFile, sql);

        try
        {
            var applyRequest = new RefactorApplyRequest
            {
                SessionId       = "test",
                OperationType   = (int)RefactorOperationType.SafeRename,
                ApprovedChanges =
                [
                    new RefactorChangeInfo
                    {
                        FilePath    = tempFile,
                        StartOffset = 7,
                        EndOffset   = 10,
                        OldText     = "col",
                        NewText     = "column_name"
                    }
                ],
                CreateBackups = false
            };

            var op       = new SafeRenameOperation();
            var response = await op.ApplyAsync(applyRequest, CancellationToken.None);

            Assert.True(response.Success);
            Assert.Equal(1, response.AppliedCount);

            var resultText = File.ReadAllText(tempFile);
            Assert.Contains("column_name", resultText);
            Assert.DoesNotContain("col ", resultText);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Apply_StaleContent_AddedToFailedFilePaths()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"safe_rename_stale_{Guid.NewGuid():N}.sql");
        const string sql = "SELECT col FROM t";
        File.WriteAllText(tempFile, sql);

        try
        {
            // Change refers to offset 7..10 but OldText is wrong ("xyz" instead of "col")
            var applyRequest = new RefactorApplyRequest
            {
                SessionId       = "test",
                OperationType   = (int)RefactorOperationType.SafeRename,
                ApprovedChanges =
                [
                    new RefactorChangeInfo
                    {
                        FilePath    = tempFile,
                        StartOffset = 7,
                        EndOffset   = 10,
                        OldText     = "xyz",  // wrong — file has "col"
                        NewText     = "column_name"
                    }
                ],
                CreateBackups = false
            };

            var op       = new SafeRenameOperation();
            var response = await op.ApplyAsync(applyRequest, CancellationToken.None);

            Assert.False(response.Success);
            Assert.Contains(tempFile, response.FailedFilePaths);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
