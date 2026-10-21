using AkmlSql.Formatting.SqlPrompt;
using AkmlSql.PostgreSql.Formatting;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Formatting;

/// <summary>
/// Spec 042 (task T145, FR-143). Keywords, built-in functions and built-in data types are cased by their own options; names are never
/// recased - an unquoted name keeps the spelling it was written with, a quoted one is untouched - and a word that is only a keyword
/// where the grammar says so (<c>name</c>, <c>type</c>, <c>key</c>, <c>first</c> ...) is not turned into one elsewhere.
/// </summary>
public class PgCasingTests
{
    private static SqlPromptStyle Style(string keywords = "leaveAsIs", string functions = "leaveAsIs", string types = "leaveAsIs")
    {
        var document = SqlPromptStyleDocument.CreateDefault("casing", "casing");
        document.Set("casing.reservedKeywords", keywords);
        document.Set("casing.builtInFunctions", functions);
        document.Set("casing.builtInDataTypes", types);
        return new SqlPromptStyle(document);
    }

    private static string Format(string sql, SqlPromptStyle style) =>
        PgFormatter.Format(sql, style, new PgFormatOptions(ApplyLayout: false)).Text;

    [Theory]
    [InlineData("uppercase", "SELECT a FROM t WHERE b IS NULL ORDER BY a DESC")]
    [InlineData("lowercase", "select a from t where b is null order by a desc")]
    [InlineData("upperCamelCase", "Select a From t Where b Is Null Order By a Desc")]
    [InlineData("lowerCamelCase", "select a from t where b is null order by a desc")]
    [InlineData("leaveAsIs", "Select a fROM t WHERE b iS null Order by a desc")]
    public void Keywords_are_cased_by_their_option(string mode, string expected)
    {
        var result = Format("Select a fROM t WHERE b iS null Order by a desc", Style(keywords: mode));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Functions_and_data_types_have_their_own_options()
    {
        var style = Style(keywords: "leaveAsIs", functions: "uppercase", types: "lowercase");

        var result = Format("select count(*), coalesce(a, 1), x::INTEGER, cast(y as VARCHAR(10)) from t", style);

        Assert.Equal("select COUNT(*), COALESCE(a, 1), x::integer, CAST(y as varchar(10)) from t", result);
    }

    [Fact]
    public void Multi_word_types_are_cased_word_by_word()
    {
        var style = Style(types: "uppercase");

        var result = Format("select a::timestamp with time zone, b::double precision, c::character varying(5) from t", style);

        Assert.Equal("select a::TIMESTAMP WITH TIME ZONE, b::DOUBLE PRECISION, c::CHARACTER VARYING(5) from t", result);
    }

    [Fact]
    public void An_unquoted_name_keeps_its_spelling_whatever_the_options_say()
    {
        var style = Style("uppercase", "uppercase", "uppercase");

        var result = Format("select CamelCase, lower_case, UPPER_CASE, t.Column1 from MyTable t", style);

        Assert.Equal("SELECT CamelCase, lower_case, UPPER_CASE, t.Column1 FROM MyTable t", result);
    }

    [Fact]
    public void Words_that_are_keywords_only_in_their_place_stay_names_elsewhere()
    {
        var style = Style("uppercase");

        var result = Format("select name, type, key, value, first, last, index, status, data from items order by first nulls last", style);

        Assert.Equal("SELECT name, type, key, value, first, last, index, status, data FROM items ORDER BY first NULLS LAST", result);
    }

    [Fact]
    public void A_column_called_like_a_function_is_not_cased_as_one()
    {
        var style = Style(functions: "uppercase");

        var result = Format("select count, now, lower from t", style);

        Assert.Equal("select count, now, lower from t", result);
    }

    [Fact]
    public void Casing_switched_off_leaves_every_word_as_written()
    {
        var style = Style("uppercase", "uppercase", "uppercase");

        var result = PgFormatter.Format("select Count(*) from t where a::Integer is null", style, new PgFormatOptions(ApplyCasing: false, ApplyLayout: false)).Text;

        Assert.Equal("select Count(*) from t where a::Integer is null", result);
    }

    [Fact]
    public void Keywords_in_definitions_are_cased_and_the_names_are_not()
    {
        var style = Style("uppercase", types: "uppercase");

        var result = Format("create unique index concurrently if not exists idx on t using btree (a desc nulls last) where a > 1", style);

        Assert.Equal("CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS idx ON t USING btree (a DESC NULLS LAST) WHERE a > 1", result);
    }

    [Fact]
    public void Constraint_words_in_a_table_definition_are_keywords_and_column_names_are_not()
    {
        var style = Style("uppercase", types: "uppercase");

        var result = Format("create table t (id serial primary key, key text not null default 'x', index int references o (id) on delete cascade)", style);

        Assert.Equal("CREATE TABLE t (id SERIAL PRIMARY KEY, key TEXT NOT NULL DEFAULT 'x', index INT REFERENCES o (id) ON DELETE CASCADE)", result);
    }
}
