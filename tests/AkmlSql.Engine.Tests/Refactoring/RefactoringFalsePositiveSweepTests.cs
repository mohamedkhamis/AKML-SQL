using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Refactoring.Operations.Lightweight;
using AkmlSql.Engine.Tests.Refactoring.Operations.Lightweight;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring;

/// <summary>
/// T072 — False-positive sweep: run each lightweight operation on 5 valid SQL corpus scripts
/// and assert the result is still parseable by TSqlParser with zero parse errors.
/// Validates FR-002 and SC-007.
/// </summary>
public sealed class RefactoringFalsePositiveSweepTests
{
    private static readonly TsqlParserService ParserService;

    static RefactoringFalsePositiveSweepTests()
    {
        ParserService = new TsqlParserService();
        ParserService.SetServerVersion(160);
    }

    // ─── Corpus loading ──────────────────────────────────────────────────────

    private static IEnumerable<string> LoadAllCorpusScripts()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(n => n.Contains("Corpus") && n.EndsWith(".sql"))
            .OrderBy(n => n);

        foreach (var name in resourceNames)
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            yield return reader.ReadToEnd();
        }
    }

    private static string[] CorpusScripts { get; } = LoadAllCorpusScripts().ToArray();

    // ─── Validation helper ───────────────────────────────────────────────────

    private static void AssertParseableOutput(string result, string operationName, string corpusName)
    {
        // A completely empty result is acceptable (no-op)
        if (string.IsNullOrWhiteSpace(result))
            return;

        ParserService.Parse(result, out var errors);
        var errorMessages = errors.Select(e => $"Line {e.Line}: {e.Message}").ToArray();
        Assert.True(
            errors.Count == 0,
            $"{operationName} produced invalid SQL from '{corpusName}':\n{string.Join("\n", errorMessages)}");
    }

    // ─── RemoveSemicolons ────────────────────────────────────────────────────

    [Fact]
    public void FalsePositiveSweep_RemoveSemicolons_AllCorpusScriptsParseable()
    {
        var op = new RemoveSemicolonsOperation();
        foreach (var (sql, idx) in CorpusScripts.Select((s, i) => (s, i)))
        {
            var ctx = LightweightOperationTestHelper.CreateContext(sql);
            var (result, _) = op.Apply(ctx);
            AssertParseableOutput(result, "RemoveSemicolons", $"corpus{idx + 1:D2}");
        }
    }

    // ─── AddGroupByColumns ───────────────────────────────────────────────────

    [Fact]
    public void FalsePositiveSweep_AddGroupByColumns_AllCorpusScriptsParseable()
    {
        var op = new AddGroupByColumnsOperation();
        foreach (var (sql, idx) in CorpusScripts.Select((s, i) => (s, i)))
        {
            var ctx = LightweightOperationTestHelper.CreateContext(sql);
            var (result, _) = op.Apply(ctx);
            AssertParseableOutput(result, "AddGroupByColumns", $"corpus{idx + 1:D2}");
        }
    }

    // ─── EncapsulateBeginEnd ─────────────────────────────────────────────────

    [Fact]
    public void FalsePositiveSweep_EncapsulateBeginEnd_AllCorpusScriptsParseable()
    {
        var op = new EncapsulateBeginEndOperation();
        foreach (var (sql, idx) in CorpusScripts.Select((s, i) => (s, i)))
        {
            var ctx = LightweightOperationTestHelper.CreateContext(sql);
            var (result, _) = op.Apply(ctx);
            AssertParseableOutput(result, "EncapsulateBeginEnd", $"corpus{idx + 1:D2}");
        }
    }

    // ─── ConvertOldStyleJoins ────────────────────────────────────────────────

    [Fact]
    public void FalsePositiveSweep_ConvertOldStyleJoins_AllCorpusScriptsParseable()
    {
        var op = new ConvertOldStyleJoinsOperation();
        foreach (var (sql, idx) in CorpusScripts.Select((s, i) => (s, i)))
        {
            var ctx = LightweightOperationTestHelper.CreateContext(sql);
            var (result, _) = op.Apply(ctx);
            AssertParseableOutput(result, "ConvertOldStyleJoins", $"corpus{idx + 1:D2}");
        }
    }

    // ─── ExpandInsertColumns (no schema cache → warns but does not corrupt SQL) ──

    [Fact]
    public void FalsePositiveSweep_ExpandInsertColumns_AllCorpusScriptsParseable()
    {
        var op = new ExpandInsertColumnsOperation();
        foreach (var (sql, idx) in CorpusScripts.Select((s, i) => (s, i)))
        {
            var ctx = LightweightOperationTestHelper.CreateContext(sql);
            var (result, _) = op.Apply(ctx);
            // With no schema cache, it returns original SQL unchanged — still must be parseable
            AssertParseableOutput(result, "ExpandInsertColumns", $"corpus{idx + 1:D2}");
        }
    }

    // ─── ReplaceDeprecatedSyntax ─────────────────────────────────────────────

    [Fact]
    public void FalsePositiveSweep_ReplaceDeprecatedSyntax_AllCorpusScriptsParseable()
    {
        var op = new ReplaceDeprecatedSyntaxOperation();
        foreach (var (sql, idx) in CorpusScripts.Select((s, i) => (s, i)))
        {
            var ctx = LightweightOperationTestHelper.CreateContext(sql);
            var (result, _) = op.Apply(ctx);
            AssertParseableOutput(result, "ReplaceDeprecatedSyntax", $"corpus{idx + 1:D2}");
        }
    }

    // ─── Corpus sanity check ─────────────────────────────────────────────────

    [Fact]
    public void FalsePositiveSweep_CorpusScripts_Are5InCount()
    {
        Assert.Equal(5, CorpusScripts.Length);
    }
}
