using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AkmlSql.Core.Models.Analysis;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Analysis.Rules.BestPractices;
using AkmlSql.Engine.Analysis.Rules.Deprecated;
using AkmlSql.Engine.Analysis.Rules.Design;
using AkmlSql.Engine.Analysis.Rules.Execution;
using AkmlSql.Engine.Analysis.Rules.Naming;
using AkmlSql.Engine.Analysis.Rules.Performance;
using AkmlSql.Engine.Analysis.Rules.Security;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;

namespace AkmlSql.Core.Tests.Analysis.Rules;

/// <summary>
/// Direct unit tests for individual analysis rule business logic.
/// Each test parses real T-SQL and runs exactly one rule against it.
/// </summary>
public class AnalysisRuleTests
{
    // ── Parser helper ─────────────────────────────────────────────────────────

    private static AnalysisContext CreateContext(string sql, int batchIndex = 0)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var script = (TSqlScript)parser.Parse(reader, out _);
        return new AnalysisContext
        {
            Script = script,
            CurrentBatch = script.Batches.Count > batchIndex
                ? script.Batches[batchIndex]
                : script.Batches[0],
            Tokens = script.ScriptTokenStream,
            DocumentText = sql,
            Settings = new ResolvedAnalysisSettings(),
            Suppressions = SuppressionMap.Empty
        };
    }

    private static List<AnalysisDiagnostic> Run(IAnalysisRule rule, string sql)
    {
        return rule.Analyze(CreateContext(sql)).ToList();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // PE003 — Missing WHERE on DELETE / UPDATE
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PE003_FiresOnDeleteWithoutWhere()
    {
        var diags = Run(new Pe003MissingWhereOnDeleteUpdate(), "DELETE FROM dbo.Orders");
        Assert.Single(diags);
        Assert.Equal("PE003", diags[0].RuleId);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
    }

    [Fact]
    public void PE003_FiresOnUpdateWithoutWhere()
    {
        var diags = Run(new Pe003MissingWhereOnDeleteUpdate(),
            "UPDATE dbo.Orders SET Status = 'Active'");
        Assert.Single(diags);
        Assert.Equal("PE003", diags[0].RuleId);
    }

    [Fact]
    public void PE003_DoesNotFire_WhenDeleteHasWhere()
    {
        var diags = Run(new Pe003MissingWhereOnDeleteUpdate(),
            "DELETE FROM dbo.Orders WHERE Id = 1");
        Assert.Empty(diags);
    }

    [Fact]
    public void PE003_DoesNotFire_WhenUpdateHasWhere()
    {
        var diags = Run(new Pe003MissingWhereOnDeleteUpdate(),
            "UPDATE dbo.Orders SET Status = 'A' WHERE Id = 1");
        Assert.Empty(diags);
    }

    [Fact]
    public void PE003_FiresForBothDeleteAndUpdate_WhenBothMissingWhere()
    {
        var sql = "DELETE FROM dbo.A\nUPDATE dbo.B SET Col = 1";
        var diags = Run(new Pe003MissingWhereOnDeleteUpdate(), sql);
        Assert.Equal(2, diags.Count);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // BP004 — Null comparison (= NULL instead of IS NULL)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BP004_FiresOnEqualsNull()
    {
        var diags = Run(new Bp004NullComparison(),
            "SELECT 1 WHERE Col = NULL");
        Assert.Single(diags);
        Assert.Equal("BP004", diags[0].RuleId);
    }

    [Fact]
    public void BP004_FiresOnNotEqualsNull_BracketStyle()
    {
        var diags = Run(new Bp004NullComparison(),
            "SELECT 1 WHERE Col <> NULL");
        Assert.Single(diags);
    }

    [Fact]
    public void BP004_FiresOnNotEqualsNull_ExclamationStyle()
    {
        var diags = Run(new Bp004NullComparison(),
            "SELECT 1 WHERE Col != NULL");
        Assert.Single(diags);
    }

    [Fact]
    public void BP004_DoesNotFire_WhenIsNull()
    {
        Assert.Empty(Run(new Bp004NullComparison(), "SELECT 1 WHERE Col IS NULL"));
        Assert.Empty(Run(new Bp004NullComparison(), "SELECT 1 WHERE Col IS NOT NULL"));
    }

    [Fact]
    public void BP004_ProvidesFixAction_WithReplacementText()
    {
        var diags = Run(new Bp004NullComparison(), "SELECT 1 WHERE Col = NULL");
        Assert.Single(diags[0].FixActions);
        Assert.Contains("IS NULL", diags[0].FixActions[0].ReplacementText);
    }

    [Fact]
    public void BP004_FixAction_ForNotEquals_ContainsIsNotNull()
    {
        var diags = Run(new Bp004NullComparison(), "SELECT 1 WHERE Col <> NULL");
        Assert.Contains("IS NOT NULL", diags[0].FixActions[0].ReplacementText);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // PE001 — SELECT * in stored procedures
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PE001_FiresOnSelectStarInProcedure()
    {
        var sql = """
            CREATE PROCEDURE dbo.usp_Test AS
            BEGIN
                SELECT * FROM dbo.Orders
            END
            """;
        var diags = Run(new Pe001AvoidSelectStar(), sql);
        Assert.Single(diags);
        Assert.Equal("PE001", diags[0].RuleId);
    }

    [Fact]
    public void PE001_DoesNotFire_OnSelectStarOutsideProcedure()
    {
        // Ad-hoc SELECT * is not flagged — rule targets procs/views only
        var diags = Run(new Pe001AvoidSelectStar(), "SELECT * FROM dbo.Orders");
        Assert.Empty(diags);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SE002 — Hardcoded password
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SE002_FiresOnDeclareWithHardcodedPassword()
    {
        var diags = Run(new Se002HardcodedPassword(),
            "DECLARE @password VARCHAR(50) = 'secret123'");
        Assert.Single(diags);
        Assert.Equal("SE002", diags[0].RuleId);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
    }

    [Fact]
    public void SE002_FiresOnSetWithPwdVariable()
    {
        var diags = Run(new Se002HardcodedPassword(),
            "DECLARE @pwd VARCHAR(50); SET @pwd = 'abc'");
        Assert.Single(diags);
    }

    [Fact]
    public void SE002_DoesNotFire_WhenPasswordIsParameterized()
    {
        var diags = Run(new Se002HardcodedPassword(),
            "DECLARE @password VARCHAR(50); SET @password = @pwd");
        Assert.Empty(diags);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // NM002 — sp_ prefix on stored procedures
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void NM002_FiresOnSpPrefix()
    {
        var diags = Run(new Nm002SpPrefix(),
            "CREATE PROCEDURE dbo.sp_GetOrders AS RETURN");
        Assert.Single(diags);
        Assert.Equal("NM002", diags[0].RuleId);
    }

    [Fact]
    public void NM002_DoesNotFire_OnUspPrefix()
    {
        var diags = Run(new Nm002SpPrefix(),
            "CREATE PROCEDURE dbo.usp_GetOrders AS RETURN");
        Assert.Empty(diags);
    }

    [Fact]
    public void NM002_DoesNotFire_OnOtherPrefixes()
    {
        Assert.Empty(Run(new Nm002SpPrefix(),
            "CREATE PROCEDURE dbo.GetOrders AS RETURN"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DEP001 — Deprecated data types (text/ntext/image)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DEP001_FiresOnTextDataType()
    {
        var diags = Run(new Dep001DeprecatedDataType(),
            "CREATE TABLE dbo.T (Notes text)");
        Assert.Single(diags);
        Assert.Equal("DEP001", diags[0].RuleId);
    }

    [Fact]
    public void DEP001_FiresOnNtextDataType()
    {
        var diags = Run(new Dep001DeprecatedDataType(),
            "CREATE TABLE dbo.T (Content ntext)");
        Assert.Single(diags);
    }

    [Fact]
    public void DEP001_FiresOnImageDataType()
    {
        var diags = Run(new Dep001DeprecatedDataType(),
            "CREATE TABLE dbo.T (Photo image)");
        Assert.Single(diags);
    }

    [Fact]
    public void DEP001_DoesNotFire_OnVarcharMax()
    {
        Assert.Empty(Run(new Dep001DeprecatedDataType(),
            "CREATE TABLE dbo.T (Notes varchar(max))"));
    }

    [Fact]
    public void DEP001_ProvidesFixAction()
    {
        var diags = Run(new Dep001DeprecatedDataType(),
            "CREATE TABLE dbo.T (Notes text)");
        Assert.Single(diags[0].FixActions);
        Assert.Contains("varchar(max)", diags[0].FixActions[0].ReplacementText,
            StringComparison.OrdinalIgnoreCase);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DE001 — Missing primary key
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DE001_FiresOnTableWithoutPrimaryKey()
    {
        var diags = Run(new De001MissingPrimaryKey(),
            "CREATE TABLE dbo.T (Id INT, Name VARCHAR(100))");
        Assert.Single(diags);
        Assert.Equal("DE001", diags[0].RuleId);
    }

    [Fact]
    public void DE001_DoesNotFire_WhenTableConstraintPkPresent()
    {
        var sql = """
            CREATE TABLE dbo.T (
                Id INT NOT NULL,
                CONSTRAINT PK_T PRIMARY KEY (Id)
            )
            """;
        Assert.Empty(Run(new De001MissingPrimaryKey(), sql));
    }

    [Fact]
    public void DE001_DoesNotFire_WhenInlineColumnPkPresent()
    {
        Assert.Empty(Run(new De001MissingPrimaryKey(),
            "CREATE TABLE dbo.T (Id INT PRIMARY KEY, Name VARCHAR(100))"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // EX001 — Division by zero
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EX001_FiresOnLiteralDivisionByZero()
    {
        var diags = Run(new Ex001DivisionByZero(), "SELECT 10 / 0");
        Assert.Single(diags);
        Assert.Equal("EX001", diags[0].RuleId);
    }

    [Fact]
    public void EX001_DoesNotFire_OnDivisionByVariable()
    {
        Assert.Empty(Run(new Ex001DivisionByZero(), "SELECT 10 / @divisor"));
    }

    [Fact]
    public void EX001_DoesNotFire_OnNormalDivision()
    {
        Assert.Empty(Run(new Ex001DivisionByZero(), "SELECT 10 / 5"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SE001 — SQL injection (EXEC with string concat)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SE001_FiresOnExecWithStringConcatenation()
    {
        var sql = "EXEC('SELECT * FROM ' + @tableName)";
        var diags = Run(new Se001SqlInjectionRisk(), sql);
        Assert.Single(diags);
        Assert.Equal("SE001", diags[0].RuleId);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
    }

    [Fact]
    public void SE001_DoesNotFire_OnExecWithLiteralString()
    {
        // Pure literal — no injection risk
        var diags = Run(new Se001SqlInjectionRisk(), "EXEC('SELECT 1')");
        Assert.Empty(diags);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ResolvedAnalysisSettings — severity override
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Rule_RespectsSeverityOverride_FromResolvedSettings()
    {
        var sql = "DELETE FROM dbo.Orders";
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var script = (TSqlScript)parser.Parse(reader, out _);

        var settings = new ResolvedAnalysisSettings();
        settings.EffectiveRules["PE003"] = new ResolvedRuleConfig
        {
            Enabled = true,
            Severity = DiagnosticSeverity.Warning // override default Error
        };

        var ctx = new AnalysisContext
        {
            Script = script,
            CurrentBatch = script.Batches[0],
            Tokens = script.ScriptTokenStream,
            DocumentText = sql,
            Settings = settings,
            Suppressions = SuppressionMap.Empty
        };

        var diags = new Pe003MissingWhereOnDeleteUpdate().Analyze(ctx).ToList();
        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Warning, diags[0].Severity);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // EX006 — Always-true condition (1=1)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EX006_FiresOnAlwaysTrueCondition()
    {
        var diags = Run(new Ex006AlwaysTrueCondition(), "SELECT 1 WHERE 1 = 1");
        Assert.Single(diags);
        Assert.Equal("EX006", diags[0].RuleId);
    }

    [Fact]
    public void EX006_DoesNotFire_OnRealPredicate()
    {
        Assert.Empty(Run(new Ex006AlwaysTrueCondition(), "SELECT 1 WHERE Id = 1"));
    }
}
