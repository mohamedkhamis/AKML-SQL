using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.SqlPrompt;
using Xunit;

namespace AkmlSql.Formatting.Tests.SqlPrompt;

/// <summary>
/// Whatever a SQL Prompt style says, formatting must never change what the SQL means, and
/// formatting the result again must change nothing. Runs the whole parity corpus through styles
/// that pull every layout decision in a different direction.
/// </summary>
public class SqlPromptLayoutSafetyTests
{
    public static readonly Dictionary<string, string> Styles = new()
    {
        ["sql-prompt-defaults"] = "{}",
        ["mohamed-khamis"] = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MohamedKhamis-style.json")),
        ["collapsed"] = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Collapsed-style.json")),
        ["river"] = """
            {"dml":{"clauses":{"clauseAlignment":"rightAligned"}},"lists":{"placeCommasBeforeItems":true,"commaAlignment":"beforeItem","alignAliases":true,"alignComments":true},
             "parentheses":{"parenthesisStyle":"expandedSplit"},"operators":{"andOr":{"alignment":"rightAligned","placeKeywordBeforeCondition":false},"comparison":{"align":true}},
             "joinStatements":{"join":{"keywordAlignment":"rightAlignedToFrom"},"on":{"keywordAlignment":"rightAlignedToJoin"}}}
            """,
        ["expanded-tabs"] = """
            {"whitespace":{"spacesOrTabs":"tabs","wrapLinesLongerThan":60,"whiteSpaceBeforeSemiColon":"newLineBefore"},
             "lists":{"placeFirstItemOnNewLine":"always","alignItemsAcrossClauses":false},
             "parentheses":{"parenthesisStyle":"expandedIndented","indentParenthesesContents":true,"addSpacesInsideParentheses":true},
             "ddl":{"parenthesisStyle":"expandedRightAligned","placeConstraintsOnNewLines":true,"placeConstraintColumnsOnNewLines":"always"},
             "cte":{"parenthesisStyle":"expandedSplit","placeNameOnNewLine":true,"placeColumnsOnNewLine":true,"columnAlignment":"indented"},
             "caseExpressions":{"placeThenOnNewLine":true,"thenAlignment":"toWhenExpression","endAlignment":"rightAlignedToWhen"},
             "joinStatements":{"join":{"placeJoinTableOnNewLine":true,"keywordAlignment":"indented","insertEmptyLineBetweenJoinClauses":true},
                               "on":{"placeConditionOnNewLine":true,"keywordAlignment":"indented","conditionAlignment":"indented"}},
             "controlFlow":{"placeBeginAndEndOnNewLine":false,"indentBeginAndEndKeywords":true},
             "functionCalls":{"placeArgumentsOnNewLines":"always","addSpacesAroundParentheses":true,"addSpaceBetweenEmptyParentheses":true},
             "operators":{"in":{"placeFirstValueOnNewLine":"always","placeSubsequentValuesOnNewLines":"always","alignment":"rightAligned"},
                          "between":{"placeAndKeywordOnNewLine":true,"andAlignment":"toBeginningOfExpression"}}}
            """,
        ["everything-collapsed"] = """
            {"dml":{"collapseShortStatements":true,"collapseStatementsShorterThan":1000,"collapseShortSubqueries":true,"collapseSubqueriesShorterThan":1000},
             "ddl":{"collapseShortStatements":true,"collapseStatementsShorterThan":1000},"controlFlow":{"collapseShortStatements":true,"collapseStatementsShorterThan":1000},
             "caseExpressions":{"collapseShortCaseExpressions":true,"collapseCaseExpressionsShorterThan":1000},"whitespace":{"wrapLongLines":false}}
            """,
    };

    public static TheoryData<string, string> CorpusByStyle()
    {
        var data = new TheoryData<string, string>();
        var corpus = Path.Combine(RepoRoot(), "tests", "format-parity", "corpus");
        foreach (var file in Directory.EnumerateFiles(corpus, "*.sql").OrderBy(f => f))
            foreach (var style in Styles.Keys)
                data.Add(Path.GetFileNameWithoutExtension(file), style);
        data.Add("<rich sample>", "sql-prompt-defaults");
        return data;
    }

    [Theory]
    [MemberData(nameof(CorpusByStyle))]
    public void Formatting_keeps_the_meaning_and_is_idempotent(string corpusFile, string style)
    {
        var sql = corpusFile == "<rich sample>"
            ? SqlPromptOptionSensitivityTests.Rich
            : File.ReadAllText(Path.Combine(RepoRoot(), "tests", "format-parity", "corpus", corpusFile + ".sql"));
        var document = SqlPromptStyleDocument.Parse(Styles[style]);

        var once = SqlPromptOptionSensitivityTests.Format(sql, document);
        var twice = SqlPromptOptionSensitivityTests.Format(once, document);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Every_comment_survives()
    {
        const string sql = """
            -- leading comment
            SELECT a, -- after a
                   b /* inline */ , c -- after c
            FROM t -- after t
            /* before where */
            WHERE x = 1 -- end
            ;
            """;
        foreach (var style in Styles.Values)
        {
            var output = SqlPromptOptionSensitivityTests.Format(sql, SqlPromptStyleDocument.Parse(style));
            foreach (var comment in new[] { "-- leading comment", "-- after a", "/* inline */", "-- after c", "-- after t", "/* before where */", "-- end" })
                Assert.Contains(comment, output);
        }
    }

    [Fact]
    public void Formatting_off_regions_are_left_exactly_as_written()
    {
        const string kept = "select   a ,b\n   from t";
        var sql = "SELECT 1;\n-- SQL Prompt formatting off\n" + kept + ";\n-- SQL Prompt formatting on\nselect c from t;\n";
        var output = SqlPromptOptionSensitivityTests.Format(sql, SqlPromptStyleDocument.Parse("""{"casing":{"reservedKeywords":"uppercase"}}"""));
        Assert.Contains(kept, output);
        Assert.Contains("SELECT c", output);
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root (AKML-SQL.slnx) not found.");
    }
}
