using System.Diagnostics.CodeAnalysis;
using Xunit;
using AkmlSql.Engine.Completion.Dictionaries;
using AkmlSql.Engine.Parser;

namespace AkmlSql.Engine.Tests.Completion.Dictionaries;

[SuppressMessage("ReSharper", "RedundantArgumentDefaultValue")]
public class KeywordDictionaryTests
{
    // ── Version constants ──────────────────────────────────────────────────

    [Fact]
    public void VersionConstants_Values()
    {
        Assert.Equal(2016, KeywordDictionary.SqlServer2016);
        Assert.Equal(2017, KeywordDictionary.SqlServer2017);
        Assert.Equal(2019, KeywordDictionary.SqlServer2019);
        Assert.Equal(2022, KeywordDictionary.SqlServer2022);
        Assert.Equal(2025, KeywordDictionary.SqlServer2025);
    }

    // ── Static arrays ──────────────────────────────────────────────────────

    [Fact]
    public void Dml_ContainsExpectedKeywords()
    {
        Assert.Contains("SELECT", KeywordDictionary.Dml);
        Assert.Contains("INSERT", KeywordDictionary.Dml);
        Assert.Contains("UPDATE", KeywordDictionary.Dml);
        Assert.Contains("DELETE", KeywordDictionary.Dml);
        Assert.Contains("MERGE", KeywordDictionary.Dml);
        Assert.Contains("TRUNCATE", KeywordDictionary.Dml);
    }

    [Fact]
    public void Ddl_ContainsExpectedKeywords()
    {
        Assert.Contains("CREATE", KeywordDictionary.Ddl);
        Assert.Contains("ALTER", KeywordDictionary.Ddl);
        Assert.Contains("DROP", KeywordDictionary.Ddl);
        Assert.Contains("TABLE", KeywordDictionary.Ddl);
        Assert.Contains("VIEW", KeywordDictionary.Ddl);
        Assert.Contains("INDEX", KeywordDictionary.Ddl);
    }

    [Fact]
    public void Clauses_ContainsExpectedKeywords()
    {
        Assert.Contains("FROM", KeywordDictionary.Clauses);
        Assert.Contains("WHERE", KeywordDictionary.Clauses);
        Assert.Contains("JOIN", KeywordDictionary.Clauses);
        Assert.Contains("GROUP BY", KeywordDictionary.Clauses);
        Assert.Contains("ORDER BY", KeywordDictionary.Clauses);
        Assert.Contains("HAVING", KeywordDictionary.Clauses);
    }

    [Fact]
    public void JoinTypes_ContainsExpectedKeywords()
    {
        Assert.Contains("INNER JOIN", KeywordDictionary.JoinTypes);
        Assert.Contains("LEFT JOIN", KeywordDictionary.JoinTypes);
        Assert.Contains("RIGHT JOIN", KeywordDictionary.JoinTypes);
        Assert.Contains("CROSS JOIN", KeywordDictionary.JoinTypes);
        Assert.Contains("FULL JOIN", KeywordDictionary.JoinTypes);
    }

    [Fact]
    public void Predicates_ContainsExpectedKeywords()
    {
        Assert.Contains("IN", KeywordDictionary.Predicates);
        Assert.Contains("EXISTS", KeywordDictionary.Predicates);
        Assert.Contains("BETWEEN", KeywordDictionary.Predicates);
        Assert.Contains("LIKE", KeywordDictionary.Predicates);
        Assert.Contains("IS NULL", KeywordDictionary.Predicates);
        Assert.Contains("IS NOT NULL", KeywordDictionary.Predicates);
    }

    [Fact]
    public void ScalarFunctions_ContainsExpectedKeywords()
    {
        Assert.Contains("LEN", KeywordDictionary.ScalarFunctions);
        Assert.Contains("GETDATE", KeywordDictionary.ScalarFunctions);
        Assert.Contains("CAST", KeywordDictionary.ScalarFunctions);
        Assert.Contains("ISNULL", KeywordDictionary.ScalarFunctions);
        Assert.Contains("COUNT", KeywordDictionary.ScalarFunctions);
        Assert.Contains("ROW_NUMBER", KeywordDictionary.ScalarFunctions);
    }

    [Fact]
    public void DataTypes_ContainsExpectedKeywords()
    {
        Assert.Contains("INT", KeywordDictionary.DataTypes);
        Assert.Contains("BIGINT", KeywordDictionary.DataTypes);
        Assert.Contains("VARCHAR", KeywordDictionary.DataTypes);
        Assert.Contains("NVARCHAR", KeywordDictionary.DataTypes);
        Assert.Contains("DATETIME", KeywordDictionary.DataTypes);
        Assert.Contains("UNIQUEIDENTIFIER", KeywordDictionary.DataTypes);
    }

    [Fact]
    public void ControlFlow_ContainsExpectedKeywords()
    {
        Assert.Contains("BEGIN", KeywordDictionary.ControlFlow);
        Assert.Contains("END", KeywordDictionary.ControlFlow);
        Assert.Contains("IF", KeywordDictionary.ControlFlow);
        Assert.Contains("WHILE", KeywordDictionary.ControlFlow);
        Assert.Contains("DECLARE", KeywordDictionary.ControlFlow);
        Assert.Contains("BEGIN TRY", KeywordDictionary.ControlFlow);
    }

    [Fact]
    public void SqlServer2022Keywords_ContainsExpectedKeywords()
    {
        Assert.Contains("GREATEST", KeywordDictionary.SqlServer2022Keywords);
        Assert.Contains("LEAST", KeywordDictionary.SqlServer2022Keywords);
        Assert.Contains("GENERATE_SERIES", KeywordDictionary.SqlServer2022Keywords);
    }

    [Fact]
    public void SqlServer2025Keywords_ContainsExpectedKeywords()
    {
        Assert.Contains("JSON_OBJECTAGG", KeywordDictionary.SqlServer2025Keywords);
        Assert.Contains("JSON_ARRAYAGG", KeywordDictionary.SqlServer2025Keywords);
        Assert.Contains("VECTOR", KeywordDictionary.SqlServer2025Keywords);
    }

    // ── GetKeywordsForClause ───────────────────────────────────────────────

    [Theory]
    [InlineData(ClauseType.Select, "TOP")]
    [InlineData(ClauseType.Select, "FROM")]
    [InlineData(ClauseType.Select, "COUNT")]
    public void GetKeywordsForClause_Select_ContainsExpected(ClauseType clause, string keyword)
    {
        var keywords = KeywordDictionary.GetKeywordsForClause(clause);
        Assert.Contains(keyword, keywords);
    }

    [Theory]
    [InlineData(ClauseType.From, "INNER JOIN")]
    [InlineData(ClauseType.From, "WHERE")]
    [InlineData(ClauseType.From, "NOLOCK")]
    public void GetKeywordsForClause_From_ContainsExpected(ClauseType clause, string keyword)
    {
        Assert.Contains(keyword, KeywordDictionary.GetKeywordsForClause(clause));
    }

    [Theory]
    [InlineData(ClauseType.Where, "AND")]
    [InlineData(ClauseType.Where, "OR")]
    [InlineData(ClauseType.Where, "IN")]
    [InlineData(ClauseType.Where, "LIKE")]
    public void GetKeywordsForClause_Where_ContainsExpected(ClauseType clause, string keyword)
    {
        Assert.Contains(keyword, KeywordDictionary.GetKeywordsForClause(clause));
    }

    [Fact]
    public void GetKeywordsForClause_JoinOn_ContainsAndOr()
    {
        var kw = KeywordDictionary.GetKeywordsForClause(ClauseType.JoinOn);
        Assert.Contains("AND", kw);
        Assert.Contains("OR", kw);
    }

    [Fact]
    public void GetKeywordsForClause_GroupBy_ContainsHaving()
    {
        var kw = KeywordDictionary.GetKeywordsForClause(ClauseType.GroupBy);
        Assert.Contains("HAVING", kw);
        Assert.Contains("ORDER BY", kw);
    }

    [Fact]
    public void GetKeywordsForClause_Having_ContainsAggregateFunctions()
    {
        var kw = KeywordDictionary.GetKeywordsForClause(ClauseType.Having);
        Assert.Contains("COUNT", kw);
        Assert.Contains("SUM", kw);
    }

    [Fact]
    public void GetKeywordsForClause_OrderBy_ContainsAscDesc()
    {
        var kw = KeywordDictionary.GetKeywordsForClause(ClauseType.OrderBy);
        Assert.Contains("ASC", kw);
        Assert.Contains("DESC", kw);
    }

    [Fact]
    public void GetKeywordsForClause_InsertColumns_ContainsValues()
    {
        var kw = KeywordDictionary.GetKeywordsForClause(ClauseType.InsertColumns);
        Assert.Contains("VALUES", kw);
        Assert.Contains("SELECT", kw);
    }

    [Fact]
    public void GetKeywordsForClause_UpdateSet_ContainsWhere()
    {
        var kw = KeywordDictionary.GetKeywordsForClause(ClauseType.UpdateSet);
        Assert.Contains("WHERE", kw);
    }

    [Fact]
    public void GetKeywordsForClause_With_ContainsAs()
    {
        var kw = KeywordDictionary.GetKeywordsForClause(ClauseType.With);
        Assert.Contains("AS", kw);
        Assert.Contains("NOLOCK", kw);
    }

    [Fact]
    public void GetKeywordsForClause_Exec_ReturnsEmpty()
    {
        var kw = KeywordDictionary.GetKeywordsForClause(ClauseType.Exec);
        Assert.Empty(kw);
    }

    [Fact]
    public void GetKeywordsForClause_Unknown_ReturnsGeneralKeywords()
    {
        var kw = KeywordDictionary.GetKeywordsForClause(ClauseType.Unknown);
        Assert.Contains("SELECT", kw);
        Assert.Contains("BEGIN", kw);
    }

    [Fact]
    public void GetKeywordsForClause_Delete_ReturnsGeneralKeywords()
    {
        // Delete falls through to the default _ case → GeneralKeywords
        var kw = KeywordDictionary.GetKeywordsForClause(ClauseType.Delete);
        Assert.NotEmpty(kw);
    }

    [Fact]
    public void GetKeywordsForClause_Create_ReturnsGeneralKeywords()
    {
        var kw = KeywordDictionary.GetKeywordsForClause(ClauseType.Create);
        Assert.NotEmpty(kw);
    }

    [Fact]
    public void GetKeywordsForClause_Alter_ReturnsGeneralKeywords()
    {
        var kw = KeywordDictionary.GetKeywordsForClause(ClauseType.Alter);
        Assert.NotEmpty(kw);
    }

    [Fact]
    public void GetKeywordsForClause_InsertValues_ReturnsGeneralKeywords()
    {
        var kw = KeywordDictionary.GetKeywordsForClause(ClauseType.InsertValues);
        Assert.NotEmpty(kw);
    }

    // ── GetAllKeywords ─────────────────────────────────────────────────────

    [Fact]
    public void GetAllKeywords_Default2022_ContainsCoreKeywords()
    {
        var all = KeywordDictionary.GetAllKeywords();
        Assert.Contains("SELECT", all);
        Assert.Contains("CREATE", all);
        Assert.Contains("FROM", all);
        Assert.Contains("INT", all);
        Assert.Contains("BEGIN", all);
    }

    [Fact]
    public void GetAllKeywords_2022_Includes2022Keywords()
    {
        var all = KeywordDictionary.GetAllKeywords(2022);
        Assert.Contains("GREATEST", all);
        Assert.Contains("LEAST", all);
    }

    [Fact]
    public void GetAllKeywords_2025_Includes2022And2025Keywords()
    {
        var all = KeywordDictionary.GetAllKeywords(2025);
        Assert.Contains("GREATEST", all);
        Assert.Contains("VECTOR", all);
        Assert.Contains("JSON_OBJECTAGG", all);
    }

    [Fact]
    public void GetAllKeywords_Before2022_Excludes2022Keywords()
    {
        var all = KeywordDictionary.GetAllKeywords(2019);
        Assert.DoesNotContain("GREATEST", all);
        Assert.DoesNotContain("LEAST", all);
    }

    [Fact]
    public void GetAllKeywords_Before2025_Excludes2025Keywords()
    {
        var all = KeywordDictionary.GetAllKeywords(2022);
        Assert.DoesNotContain("VECTOR", all);
        Assert.DoesNotContain("JSON_OBJECTAGG", all);
    }

    [Fact]
    public void GetAllKeywords_2016_ExcludesBoth()
    {
        var all = KeywordDictionary.GetAllKeywords(2016);
        Assert.DoesNotContain("GREATEST", all);
        Assert.DoesNotContain("VECTOR", all);
    }
}
