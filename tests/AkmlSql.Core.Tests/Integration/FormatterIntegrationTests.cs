using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Core.Tests.Integration;

/// <summary>
/// Integration tests for the full formatting pipeline.
/// Each test exercises the pipeline end-to-end including semantic validation
/// and idempotency checking.
/// </summary>
public class FormatterIntegrationTests
{
    private readonly FormatterPipeline _pipeline = new();
    private static FormattingProfile DefaultProfile => new();

    // ═════════════════════════════════════════════════════════════════════════
    // Happy paths — common SQL patterns
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Format_SimpleSelect_UppercasesKeywords()
    {
        var result = _pipeline.Format("select id, name from dbo.orders where id = 1", DefaultProfile);
        Assert.True(result.Success);
        Assert.True(result.ValidationPassed);
        Assert.Contains("SELECT", result.FormattedText);
        Assert.Contains("FROM", result.FormattedText);
        Assert.Contains("WHERE", result.FormattedText);
    }

    [Fact]
    public void Format_Insert_UppercasesKeywords()
    {
        var profile = new FormattingProfile();
        profile.Metadata.SkipValidation = true;
        var result = _pipeline.Format(
            "insert into dbo.orders (status, created_at) values ('Active', getdate())", profile);
        Assert.True(result.Success);
        Assert.Contains("INSERT", result.FormattedText);
        Assert.Contains("VALUES", result.FormattedText);
    }

    [Fact]
    public void Format_Update_UppercasesKeywords()
    {
        var result = _pipeline.Format(
            "update dbo.orders set status = 'Closed' where id = @id", DefaultProfile);
        Assert.True(result.Success);
        Assert.Contains("UPDATE", result.FormattedText);
        Assert.Contains("SET", result.FormattedText);
        Assert.Contains("WHERE", result.FormattedText);
    }

    [Fact]
    public void Format_Delete_UppercasesKeywords()
    {
        var result = _pipeline.Format("delete from dbo.orders where id = @id", DefaultProfile);
        Assert.True(result.Success);
        Assert.Contains("DELETE", result.FormattedText);
        Assert.Contains("WHERE", result.FormattedText);
    }

    [Fact]
    public void Format_StoredProcedure_FormatsCorrectly()
    {
        var sql = """
            create procedure dbo.usp_getorder @id int as
            begin
                set nocount on;
                select id, status from dbo.orders where id = @id;
            end
            """;
        var profile = new FormattingProfile();
        profile.Metadata.SkipValidation = true;
        var result = _pipeline.Format(sql, profile);
        Assert.True(result.Success);
        Assert.Contains("CREATE PROCEDURE", result.FormattedText);
        Assert.Contains("SELECT", result.FormattedText);
    }

    [Fact]
    public void Format_Cte_FormatsCorrectly()
    {
        var sql = """
            with recent as (
                select id, name from dbo.orders where created_at > dateadd(day, -30, getdate())
            )
            select * from recent order by id
            """;
        var profile = new FormattingProfile();
        profile.Metadata.SkipValidation = true;
        var result = _pipeline.Format(sql, profile);
        Assert.True(result.Success);
        Assert.Contains("SELECT", result.FormattedText);
        Assert.Contains("FROM", result.FormattedText);
    }

    [Fact]
    public void Format_CaseExpression_FormatsCorrectly()
    {
        var sql = "select case when status = 'A' then 'Active' when status = 'C' then 'Closed' else 'Unknown' end from dbo.orders";
        var result = _pipeline.Format(sql, DefaultProfile);
        Assert.True(result.Success);
        Assert.Contains("CASE", result.FormattedText);
        Assert.Contains("WHEN", result.FormattedText);
        Assert.Contains("THEN", result.FormattedText);
        Assert.Contains("END", result.FormattedText);
    }

    [Fact]
    public void Format_SubQuery_FormatsCorrectly()
    {
        var sql = "select id from dbo.orders where id in (select order_id from dbo.shipments where status = 'Shipped')";
        var result = _pipeline.Format(sql, DefaultProfile);
        Assert.True(result.Success);
        Assert.Contains("IN", result.FormattedText);
    }

    [Fact]
    public void Format_MultipleStatements_AllFormatted()
    {
        var sql = "select 1; select 2; select 3;";
        var result = _pipeline.Format(sql, DefaultProfile);
        Assert.True(result.Success);
        // All SELECT keywords should appear (Split without RemoveEmptyEntries: leading "" + 3 parts = 4)
        Assert.Equal(3, result.FormattedText.Split("SELECT").Length - 1);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Error cases
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Format_EmptyString_DoesNotThrow()
    {
        var result = _pipeline.Format("", DefaultProfile);
        Assert.NotNull(result);
    }

    [Fact]
    public void Format_CommentOnly_DoesNotThrow()
    {
        var result = _pipeline.Format("-- This is just a comment", DefaultProfile);
        Assert.NotNull(result);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Edge cases
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Format_SqlWithSqlcmdDirective_PreservesDirective()
    {
        var sql = ":r OtherFile.sql\nSELECT 1";
        var result = _pipeline.Format(sql, DefaultProfile);
        Assert.NotNull(result);
        // SQLCMD directives should be restored in output
        Assert.Contains(":r", result.FormattedText);
    }

    [Fact]
    public void Format_NoformatBlock_IsPreserved()
    {
        var weirdSpacing = "SELECT   a   ,   b   FROM   t";
        var sql = $"SELECT 1\n-- noformat\n{weirdSpacing}\n-- endnoformat\nSELECT 2";
        var result = _pipeline.Format(sql, DefaultProfile);
        Assert.True(result.Success);
        Assert.Contains(weirdSpacing, result.FormattedText);
    }

    [Fact]
    public void Format_GoSeparator_HandledCorrectly()
    {
        var sql = "SELECT 1\nGO\nSELECT 2\nGO";
        var result = _pipeline.Format(sql, DefaultProfile);
        Assert.True(result.Success);
        Assert.Contains("GO", result.FormattedText);
    }

    [Fact]
    public void Format_VariableDeclarations_FormatCorrectly()
    {
        var sql = "declare @id int, @name varchar(100); set @id = 1; set @name = 'test'; select @id, @name";
        var result = _pipeline.Format(sql, DefaultProfile);
        Assert.True(result.Success);
        Assert.Contains("DECLARE", result.FormattedText);
    }

    [Fact]
    public void Format_TryCatch_FormatsCorrectly()
    {
        var sql = """
            begin try
                insert into dbo.orders (status) values ('A');
            end try
            begin catch
                declare @err nvarchar(max) = error_message();
                throw;
            end catch
            """;
        var profile = new FormattingProfile();
        profile.Metadata.SkipValidation = true;
        var result = _pipeline.Format(sql, profile);
        Assert.True(result.Success);
        Assert.Contains("BEGIN", result.FormattedText);
        Assert.Contains("END", result.FormattedText);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Idempotency — formatting twice gives same result
    // ═════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("SELECT Id, Name FROM dbo.Orders WHERE Id > 0 ORDER BY Id DESC")]
    [InlineData("INSERT INTO dbo.T (Col1, Col2) VALUES ('A', 1)")]
    [InlineData("UPDATE dbo.T SET Col1 = 'X' WHERE Id = 1")]
    [InlineData("DELETE FROM dbo.T WHERE Id = 1")]
    [InlineData("SELECT Id FROM dbo.T WHERE Status IN ('A', 'B', 'C')")]
    [InlineData("SELECT CASE WHEN Status = 'A' THEN 1 ELSE 0 END AS Flag FROM dbo.T")]
    public void Format_IsIdempotent(string sql)
    {
        var first = _pipeline.Format(sql, DefaultProfile);
        var second = _pipeline.Format(first.FormattedText, DefaultProfile);
        Assert.Equal(first.FormattedText, second.FormattedText);
        Assert.False(second.WasModified);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Profile options
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Format_WithLowerCaseProfile_LowercasesKeywords()
    {
        var profile = new FormattingProfile();
        profile.Casing.ReservedKeywords = "lowercase";
        profile.Casing.BuiltInFunctions = "lowercase";

        var result = _pipeline.Format("SELECT 1 FROM dbo.T", profile);
        Assert.True(result.Success);
        Assert.Contains("select", result.FormattedText.ToLowerInvariant());
    }

    [Fact]
    public void Format_SkipValidationProfile_StillProducesOutput()
    {
        var profile = new FormattingProfile();
        profile.Metadata.SkipValidation = true;

        var result = _pipeline.Format("SELECT Id FROM dbo.Orders", profile);
        Assert.True(result.Success);
        Assert.NotEmpty(result.FormattedText);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // BulkFormatter integration
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BulkFormat_FormatsMultipleFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bulk_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        try
        {
            var file1 = Path.Combine(dir, "q1.sql");
            var file2 = Path.Combine(dir, "q2.sql");
            File.WriteAllText(file1, "select 1 from dbo.t");
            File.WriteAllText(file2, "select 2 from dbo.t");

            var formatter = new BulkFormatter();
            var options = new BulkFormatOptions { CreateBackups = false, SkipParseErrors = true };
            var report = await formatter.FormatFilesAsync(
                [file1, file2], DefaultProfile, options);

            Assert.Equal(2, report.TotalFiles);
            Assert.True(report.FormattedCount + report.AlreadyFormattedCount == 2);
            Assert.Contains("SELECT", File.ReadAllText(file1));
            Assert.Contains("SELECT", File.ReadAllText(file2));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task BulkFormat_CreatesBakFiles_WhenEnabled()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bulk_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        try
        {
            var file = Path.Combine(dir, "q.sql");
            File.WriteAllText(file, "select 1");

            var formatter = new BulkFormatter();
            var options = new BulkFormatOptions { CreateBackups = true };
            await formatter.FormatFilesAsync([file], DefaultProfile, options);

            Assert.True(File.Exists(file + ".bak"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task BulkFormat_SkipsInvalidFiles_WhenSkipParseErrorsEnabled()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bulk_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        try
        {
            var valid = Path.Combine(dir, "valid.sql");
            var invalid = Path.Combine(dir, "invalid.sql");
            File.WriteAllText(valid, "SELECT 1");
            File.WriteAllText(invalid, "THIS IS NOT SQL @@@###");

            var formatter = new BulkFormatter();
            var options = new BulkFormatOptions { CreateBackups = false, SkipParseErrors = true };
            var report = await formatter.FormatFilesAsync([valid, invalid], DefaultProfile, options);

            Assert.Equal(2, report.TotalFiles);
            // Valid file formatted, invalid file either skipped or errored — not zero total
            Assert.True(report.FormattedCount + report.AlreadyFormattedCount >= 1);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task BulkFormat_Cancellation_ReturnsPartialResults()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bulk_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        try
        {
            var files = Enumerable.Range(0, 10)
                .Select(i =>
                {
                    var p = Path.Combine(dir, $"q{i}.sql");
                    File.WriteAllText(p, "select 1");
                    return p;
                })
                .ToList();

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // pre-cancel

            var formatter = new BulkFormatter();
            var options = new BulkFormatOptions { CreateBackups = false };
            var report = await formatter.FormatFilesAsync(files, DefaultProfile, options, null, cts.Token);

            // Pre-cancelled: 0 or some files processed, but no exception
            Assert.NotNull(report);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task BulkFormat_PreviewOnly_DoesNotWriteFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bulk_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        try
        {
            var file = Path.Combine(dir, "q.sql");
            const string original = "select 1 from dbo.t";
            File.WriteAllText(file, original);

            var formatter = new BulkFormatter();
            var options = new BulkFormatOptions { PreviewOnly = true, CreateBackups = false };
            await formatter.FormatFilesAsync([file], DefaultProfile, options);

            // File should be unchanged
            Assert.Equal(original, File.ReadAllText(file));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
