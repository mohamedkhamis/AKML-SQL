using System.Collections.Generic;
using AkmlSql.Engine.Ai;
using AkmlSql.Engine.Ai.Prompts;
using Xunit;

namespace AkmlSql.AI.Tests;

/// <summary>
/// Spec 047 (AI-001, contracts/workspace-ai.md §6) — with no schema shared (the No schema privacy mode, or no snapshot yet),
/// a prompt does not announce a schema it does not carry: no empty "following schema:" section, no rule to use "only tables
/// and columns from the provided schema", and Index Analysis does not point at indexes "provided in the schema context". It
/// says instead that no schema is shared. With a schema the prompts read as they did, character for character
/// (PromptDialectTests keeps the goldens).
/// </summary>
public sealed class PromptPrivacyModeTests
{
    private const string Schema = "Table: dbo.Customers (CustomerID nchar(5) PK, CompanyName nvarchar(40))";
    private const string Sql = "SELECT * FROM dbo.Customers WHERE Country = 'Mexico';";

    public static IEnumerable<object[]> Builders()
    {
        foreach (var dialect in new[] { SqlDialect.SqlServer, SqlDialect.PostgreSql })
        {
            yield return new object[] { "explain", dialect };
            yield return new object[] { "fix", dialect };
            yield return new object[] { "optimize", dialect };
            yield return new object[] { "texttosql", dialect };
            yield return new object[] { "indexanalysis", dialect };
            yield return new object[] { "ghosttext", dialect };
            yield return new object[] { "chat", dialect };
        }
    }

    private static string SystemPrompt(string feature, SqlDialect dialect, string schema) => feature switch
    {
        "explain" => ExplainPrompt.Build(schema, Sql, dialect).systemMessage,
        "fix" => FixPrompt.Build(schema, "SELEC 1", "Incorrect syntax near 'SELEC'.", 102, dialect).systemMessage,
        "optimize" => OptimizePrompt.Build(schema, Sql, dialect).systemMessage,
        "texttosql" => TextToSqlPrompt.Build(schema, "the customers in Mexico", dialect).systemMessage,
        "indexanalysis" => IndexAnalysisPrompt.Build(schema, Sql, null, dialect).systemMessage,
        "ghosttext" => GhostTextPrompt.Build(schema, "SELECT * FROM ", dialect).systemMessage,
        "chat" => ChatSystemPrompt.Build(schema, dialect),
        _ => throw new System.ArgumentOutOfRangeException(nameof(feature)),
    };

    [Theory]
    [MemberData(nameof(Builders))]
    public void Without_a_schema_the_prompt_says_none_is_shared_and_never_points_at_one(string feature, SqlDialect dialect)
    {
        foreach (var blank in new[] { string.Empty, "   " })
        {
            var prompt = SystemPrompt(feature, dialect, blank);

            Assert.DoesNotContain("following schema:", prompt);
            Assert.DoesNotContain("provided schema", prompt);
            Assert.DoesNotContain("provided in the schema context", prompt);
            Assert.DoesNotContain("already exist in the schema context", prompt);
            Assert.Contains("No database schema is shared", prompt);
        }
    }

    [Theory]
    [MemberData(nameof(Builders))]
    public void With_a_schema_the_prompt_carries_it_as_before(string feature, SqlDialect dialect)
    {
        var prompt = SystemPrompt(feature, dialect, Schema);

        Assert.Contains("following schema:", prompt);
        Assert.Contains(Schema, prompt);
        Assert.Contains("provided schema", prompt);
        Assert.DoesNotContain("No database schema is shared", prompt);
    }

    [Theory]
    [InlineData(SqlDialect.SqlServer)]
    [InlineData(SqlDialect.PostgreSql)]
    public void Index_advice_without_a_schema_says_the_existing_indexes_are_unknown(SqlDialect dialect)
    {
        Assert.Contains("Existing indexes are unknown; say which ones to check first.", SystemPrompt("indexanalysis", dialect, string.Empty));
        Assert.Contains("Existing indexes are unknown; say which ones to check first.", SystemPrompt("optimize", dialect, string.Empty));
        Assert.DoesNotContain("Existing indexes are unknown", SystemPrompt("indexanalysis", dialect, Schema));
        Assert.DoesNotContain("Existing indexes are unknown", SystemPrompt("optimize", dialect, Schema));
    }
}
