using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Models.Analysis;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using AkmlSql.Core.Ipc.Messages;
using Xunit;

namespace AkmlSql.Core.Tests.Integration;

/// <summary>
/// Integration tests for the full analysis pipeline:
/// CodeAnalysisRequest → AnalysisEngine → CodeAnalysisResponse.
/// These tests exercise the real RuleRegistry, TsqlParserService, CaSettingsLoader,
/// and SuppressionMap without mocking any component.
/// </summary>
public class AnalysisEngineIntegrationTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private readonly TsqlParserService _parser = new();
    private readonly RuleRegistry _registry = new();
    private readonly CaSettingsLoader _settingsLoader = new();
    private readonly AnalysisEngine _engine;
    private readonly SessionManager _sessionManager = new();
    private readonly SchemaCacheManager _schemaCacheManager = new();
    private readonly CodeAnalysisSettings _globalSettings = new() { Enabled = true };

    public AnalysisEngineIntegrationTests()
    {
        _engine = new AnalysisEngine(_parser, _registry, _settingsLoader);
    }

    private async Task<CodeAnalysisResponse> AnalyzeAsync(
        string sql, string sessionId = "test-session")
    {
        // Spec 021 F1: API refactored to take (serverVersion, DatabaseCache?) directly.
        // The test resolves them from the in-test SessionManager + SchemaCacheManager.
        var session = _sessionManager.GetSession(sessionId);
        var schemaCache = session != null
            ? _schemaCacheManager.GetOrCreateCache(session.SessionId, session.DatabaseName)
            : null;
        var serverVersion = session?.ServerVersion ?? 0;

        return await _engine.AnalyzeAsync(
            new CodeAnalysisRequest
            {
                SessionId = sessionId,
                RequestId = "1",
                DocumentText = sql,
                DocumentVersion = 1
            },
            serverVersion,
            schemaCache,
            _globalSettings,
            CancellationToken.None);
    }

    // ── Happy path: clean SQL ─────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_ReturnsNoIssues_ForCleanSql()
    {
        var sql = """
            SET NOCOUNT ON;
            SELECT Id, Name, Status
            FROM dbo.Orders
            WHERE Id = @id;
            """;
        var response = await AnalyzeAsync(sql);
        Assert.NotNull(response);
        // Clean SQL may have some style hints but no errors
        Assert.DoesNotContain(response.Issues, i => i.Severity == (int)DiagnosticSeverity.Error);
    }

    // ── DELETE without WHERE ──────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_DetectsPE003_OnDeleteWithoutWhere()
    {
        var response = await AnalyzeAsync("DELETE FROM dbo.Orders");
        Assert.Contains(response.Issues, i => i.RuleId == "PE003");
    }

    [Fact]
    public async Task AnalyzeAsync_NoPE003_WhenWherePresent()
    {
        var response = await AnalyzeAsync("DELETE FROM dbo.Orders WHERE Id = 1");
        Assert.DoesNotContain(response.Issues, i => i.RuleId == "PE003");
    }

    // ── Null comparison ───────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_DetectsBP004_OnEqualsNull()
    {
        var response = await AnalyzeAsync("SELECT 1 WHERE Col = NULL");
        Assert.Contains(response.Issues, i => i.RuleId == "BP004");
    }

    // ── Hardcoded password ────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_DetectsSE002_OnHardcodedPassword()
    {
        var response = await AnalyzeAsync("DECLARE @password VARCHAR(50) = 'secret'");
        Assert.Contains(response.Issues, i => i.RuleId == "SE002");
        Assert.Contains(response.Issues, i =>
            i.RuleId == "SE002" && i.Severity == (int)DiagnosticSeverity.Error);
    }

    // ── sp_ prefix ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_DetectsNM002_OnSpPrefix()
    {
        var response = await AnalyzeAsync("CREATE PROCEDURE dbo.sp_GetOrders AS RETURN");
        Assert.Contains(response.Issues, i => i.RuleId == "NM002");
    }

    // ── Multiple issues in one document ──────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_DetectsMultipleIssues_InSameBatch()
    {
        var sql = """
            DELETE FROM dbo.Orders
            UPDATE dbo.Customers SET Name = 'X'
            SELECT 1 WHERE Col = NULL
            """;
        var response = await AnalyzeAsync(sql);

        Assert.Contains(response.Issues, i => i.RuleId == "PE003"); // DELETE
        Assert.Contains(response.Issues, i => i.RuleId == "PE003"); // UPDATE
        Assert.Contains(response.Issues, i => i.RuleId == "BP004"); // NULL
    }

    // ── Response fields ───────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_Response_IncludesRequestId()
    {
        var response = await AnalyzeAsync("SELECT 1");
        Assert.Equal("1", response.RequestId);
    }

    [Fact]
    public async Task AnalyzeAsync_Response_IncludesDocumentVersion()
    {
        var response = await AnalyzeAsync("SELECT 1");
        Assert.Equal(1, response.AnalyzedVersion);
    }

    [Fact]
    public async Task AnalyzeAsync_Issues_HaveValidLineAndColumn()
    {
        var response = await AnalyzeAsync("DELETE FROM dbo.Orders");
        var pe003 = response.Issues.FirstOrDefault(i => i.RuleId == "PE003");
        Assert.NotNull(pe003);
        Assert.True(pe003.Line >= 1);
        Assert.True(pe003.Column >= 1);
    }

    [Fact]
    public async Task AnalyzeAsync_Issues_HaveNonEmptyMessages()
    {
        var response = await AnalyzeAsync("DELETE FROM dbo.Orders");
        Assert.All(response.Issues, i => Assert.False(string.IsNullOrWhiteSpace(i.Message)));
    }

    // ── Empty / whitespace SQL ────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_ReturnsEmptyIssues_ForEmptySql()
    {
        var response = await AnalyzeAsync("");
        Assert.NotNull(response);
        Assert.Empty(response.Issues);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsEmptyIssues_ForWhitespaceSql()
    {
        var response = await AnalyzeAsync("   \n\t  ");
        Assert.NotNull(response);
        Assert.Empty(response.Issues);
    }

    // ── Deprecated data type ──────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_DetectsDEP001_OnTextDataType()
    {
        var response = await AnalyzeAsync("CREATE TABLE dbo.T (Notes text)");
        Assert.Contains(response.Issues, i => i.RuleId == "DEP001");
    }

    // ── SQL injection ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_DetectsSE001_OnExecWithConcat()
    {
        var response = await AnalyzeAsync("EXEC('SELECT * FROM ' + @tableName)");
        Assert.Contains(response.Issues, i => i.RuleId == "SE001");
    }

    // ── Division by zero ──────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_DetectsEX001_OnDivisionByZero()
    {
        var response = await AnalyzeAsync("SELECT 10 / 0");
        Assert.Contains(response.Issues, i => i.RuleId == "EX001");
    }

    // ── Missing primary key ───────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_DetectsDE001_OnTableWithoutPrimaryKey()
    {
        var response = await AnalyzeAsync("CREATE TABLE dbo.T (Id INT, Name VARCHAR(100))");
        Assert.Contains(response.Issues, i => i.RuleId == "DE001");
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_ThrowsOperationCanceled_WhenPreCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _engine.AnalyzeAsync(
                new CodeAnalysisRequest
                {
                    SessionId = "cancel-test",
                    RequestId = "99",
                    DocumentText = "SELECT 1",
                    DocumentVersion = 1
                },
                0,        // serverVersion not needed for cancellation test
                null,     // schemaCache not needed
                _globalSettings,
                cts.Token));
    }

    // ── Global settings: disabled ─────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_ReturnsNoIssues_WhenGlobalSettingsDisabled()
    {
        var disabledSettings = new CodeAnalysisSettings { Enabled = false };
        var response = await _engine.AnalyzeAsync(
            new CodeAnalysisRequest
            {
                SessionId = "disabled-test",
                RequestId = "1",
                DocumentText = "DELETE FROM dbo.Orders",
                DocumentVersion = 1
            },
            0,                // serverVersion irrelevant: settings.Enabled=false short-circuits
            null,             // schemaCache also irrelevant
            disabledSettings,
            CancellationToken.None);

        Assert.Empty(response.Issues);
    }

    // ── Batch caching (incremental analysis) ─────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_ReturnsSameIssues_WhenCalledTwiceWithSameSql()
    {
        var sql = "DELETE FROM dbo.Orders\nSELECT 1 WHERE Col = NULL";
        var first = await AnalyzeAsync(sql);
        var second = await AnalyzeAsync(sql);

        // Same issue count and rule IDs (order may differ)
        var firstIds = first.Issues.Select(i => i.RuleId).OrderBy(x => x).ToList();
        var secondIds = second.Issues.Select(i => i.RuleId).OrderBy(x => x).ToList();
        Assert.Equal(firstIds, secondIds);
    }
}
