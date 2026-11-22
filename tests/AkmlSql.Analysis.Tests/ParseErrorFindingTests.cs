using System.Threading;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Parser;
using Xunit;

namespace AkmlSql.Analysis.Tests;

/// <summary>
/// Spec 047 (WEB-005, contracts/analysis-display.md §4) — with <c>reportParseErrors</c> the analysis reports each parse
/// error as a <c>SYNTAX</c> Error finding. Without it the web's Problems tab said "No problems found." for SQL that does
/// not parse: the engine gave up on a script with no tree and returned nothing. The SSMS engine handler and the CLI keep
/// the default (<c>false</c>): SSMS underlines syntax errors itself.
/// </summary>
public sealed class ParseErrorFindingTests
{
    private static AnalysisEngine CreateEngine() =>
        new(new TsqlParserService(), new RuleRegistry(), new CaSettingsLoader());

    private static CodeAnalysisRequest Request(string sql) => new()
    {
        SessionId = "test-" + Guid.NewGuid().ToString("N"),
        RequestId = "1",
        DocumentText = sql,
        DocumentVersion = 1,
    };

    private static Task<CodeAnalysisResponse> AnalyzeAsync(string sql, bool reportParseErrors, bool enabled = true) =>
        CreateEngine().AnalyzeAsync(Request(sql), serverVersion: 16, schemaCache: null,
            new CodeAnalysisSettings { Enabled = enabled }, CancellationToken.None, reportParseErrors);

    [Fact]
    public async Task A_statement_that_does_not_parse_is_one_syntax_error()
    {
        const string sql = "SELEC * FROM dbo.Customers;";
        new TsqlParserService().Parse(sql, out var errors);
        var parserError = Assert.Single(errors);

        var response = await AnalyzeAsync(sql, reportParseErrors: true);

        var issue = Assert.Single(response.Issues);
        Assert.Equal("SYNTAX", issue.RuleId);
        Assert.Equal(3, issue.Severity);
        Assert.Equal(1, issue.Line);
        Assert.Equal(parserError.Column, issue.Column);
        Assert.Equal(parserError.Message, issue.Message);
        Assert.Equal(parserError.Offset, issue.StartOffset);
        Assert.True(issue.EndOffset > issue.StartOffset, "the finding covers the offending token");
    }

    [Fact]
    public async Task A_bad_batch_after_GO_is_reported_beside_the_findings_of_the_good_one()
    {
        const string sql = "SELECT 1\nGO\nSELEC * FROM dbo.Customers;";

        var response = await AnalyzeAsync(sql, reportParseErrors: true);

        var syntax = Assert.Single(response.Issues, i => i.RuleId == "SYNTAX");
        Assert.Equal(3, syntax.Line);
        Assert.Equal("SYNTAX", response.Issues[0].RuleId);   // syntax errors come first
        Assert.Contains(response.Issues, i => i.RuleId == "ST004" && i.Line == 1);
    }

    [Fact]
    public async Task A_GO_with_a_repeat_count_is_not_a_syntax_error()
    {
        // ScriptDom reports "GO 3" as 46010 "Incorrect syntax near '3'."; SSMS and sqlcmd run the batch three times.
        const string sql = "select 1\r\nGO\r\nselect 2\r\nGO 3\r\nprint 'x'\r\nGO 2 -- twice\r\nselect 3\r\nGO\r\n";
        new TsqlParserService().Parse(sql, out var errors);
        Assert.NotEmpty(errors);   // the parser's own view

        var response = await AnalyzeAsync(sql, reportParseErrors: true);

        Assert.DoesNotContain(response.Issues, i => i.RuleId == "SYNTAX");
    }

    [Fact]
    public async Task Analysis_switched_off_reports_no_syntax_error_either()
    {
        var response = await AnalyzeAsync("SELEC * FROM dbo.Customers;", reportParseErrors: true, enabled: false);

        Assert.Empty(response.Issues);
    }

    [Fact]
    public async Task By_default_a_parse_error_is_not_a_finding()
    {
        var response = await AnalyzeAsync("SELEC * FROM dbo.Customers;", reportParseErrors: false);

        Assert.DoesNotContain(response.Issues, i => i.RuleId == "SYNTAX");
    }
}
