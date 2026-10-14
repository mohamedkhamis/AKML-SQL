using AkmlSql.Core.Text;
using Xunit;

namespace AkmlSql.Core.Tests.Text;

/// <summary>
/// Spec 042 (FR-061, FR-045): PostgreSQL folds unquoted identifiers to lower case, so a name needs
/// double quotes exactly when it is not a plain lower-case identifier or when it is a keyword that cannot
/// stand as a table or column name.
/// </summary>
public class PgIdentifierQuotingTests
{
    [Theory]
    [InlineData("orders")]
    [InlineData("order_details")]
    [InlineData("_private")]
    [InlineData("a1")]
    [InlineData("cost$center")]
    [InlineData("values")]      // col_name_keyword: valid as a table or column name
    [InlineData("timestamp")]   // col_name_keyword
    [InlineData("name")]        // unreserved
    public void Plain_lower_case_names_are_not_quoted(string name)
    {
        Assert.False(PgIdentifierQuoting.NeedsQuoting(name));
        Assert.Equal(name, PgIdentifierQuoting.Quote(name));
    }

    [Theory]
    [InlineData("Orders", "\"Orders\"")]
    [InlineData("order details", "\"order details\"")]
    [InlineData("1st", "\"1st\"")]
    [InlineData("a-b", "\"a-b\"")]
    [InlineData("café", "\"café\"")]
    [InlineData("we\"ird", "\"we\"\"ird\"")]
    [InlineData("", "\"\"")]
    public void Names_that_would_be_folded_or_misread_are_double_quoted(string name, string expected)
    {
        Assert.True(PgIdentifierQuoting.NeedsQuoting(name));
        Assert.Equal(expected, PgIdentifierQuoting.Quote(name));
    }

    [Theory]
    [InlineData("select")]
    [InlineData("user")]
    [InlineData("table")]
    [InlineData("order")]
    [InlineData("left")]        // type_func_name_keyword: not valid as a column name
    [InlineData("join")]
    [InlineData("SELECT")]
    public void Reserved_words_are_quoted(string name)
    {
        Assert.True(PgIdentifierQuoting.NeedsQuoting(name));
        Assert.StartsWith("\"", PgIdentifierQuoting.Quote(name));
    }

    [Fact]
    public void QualifiedName_quotes_each_part_and_skips_an_empty_schema()
    {
        Assert.Equal("public.orders", PgIdentifierQuoting.QualifiedName("public", "orders"));
        Assert.Equal("sales.\"Order Details\"", PgIdentifierQuoting.QualifiedName("sales", "Order Details"));
        Assert.Equal("orders", PgIdentifierQuoting.QualifiedName(null, "orders"));
        Assert.Equal("orders", PgIdentifierQuoting.QualifiedName("", "orders"));
        Assert.Equal("\"User\".\"select\"", PgIdentifierQuoting.QualifiedName("User", "select"));
    }

    [Fact]
    public void The_reserved_set_has_the_reserved_and_type_function_name_keywords()
    {
        Assert.Contains("select", PgIdentifierQuoting.ReservedWords);
        Assert.Contains("left", PgIdentifierQuoting.ReservedWords);
        Assert.DoesNotContain("values", PgIdentifierQuoting.ReservedWords);
        Assert.True(PgIdentifierQuoting.ReservedWords.Count > 80);
    }
}
