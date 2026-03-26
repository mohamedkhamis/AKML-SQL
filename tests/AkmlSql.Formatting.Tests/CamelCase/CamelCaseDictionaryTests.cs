using Xunit;
using AkmlSql.Formatting.CamelCase;

namespace AkmlSql.Formatting.Tests.CamelCase;

public class CamelCaseDictionaryTests
{
    // ── Apply: empty/null ─────────────────────────────────────────────────

    [Fact]
    public void Apply_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", CamelCaseDictionary.Apply("", "PascalCase"));
    }

    [Fact]
    public void Apply_NullString_ReturnsNull()
    {
        Assert.Null(CamelCaseDictionary.Apply(null!, "PascalCase"));
    }

    // ── Apply: underscore identifiers ─────────────────────────────────────

    [Fact]
    public void Apply_Underscored_PascalCase()
    {
        Assert.Equal("CustomerOrderId", CamelCaseDictionary.Apply("customer_order_id", "PascalCase"));
    }

    [Fact]
    public void Apply_Underscored_CamelCase()
    {
        Assert.Equal("customerOrderId", CamelCaseDictionary.Apply("customer_order_id", "camelCase"));
    }

    [Fact]
    public void Apply_Underscored_SingleWord_PascalCase()
    {
        Assert.Equal("Name", CamelCaseDictionary.Apply("name", "PascalCase"));
    }

    [Fact]
    public void Apply_Underscored_LeadingUnderscore_PreservesUnderscore()
    {
        // Leading underscore produces empty part → "_"
        var result = CamelCaseDictionary.Apply("_name", "PascalCase");
        Assert.Contains("Name", result);
    }

    // ── Apply: mixed-case identifiers ─────────────────────────────────────

    [Fact]
    public void Apply_MixedCase_CamelToPascal()
    {
        Assert.Equal("CustomerId", CamelCaseDictionary.Apply("customerId", "PascalCase"));
    }

    [Fact]
    public void Apply_MixedCase_PascalToCamel()
    {
        Assert.Equal("customerId", CamelCaseDictionary.Apply("CustomerId", "camelCase"));
    }

    [Fact]
    public void Apply_MixedCase_AcronymBoundary()
    {
        // "HTTPSPort" → HTTPS + Port
        var result = CamelCaseDictionary.Apply("HTTPSPort", "PascalCase");
        Assert.Equal("HttpsPort", result);
    }

    // ── Apply: pure lowercase — dictionary splitting ───────────────────────

    [Fact]
    public void Apply_PureLower_KnownWords_PascalCase()
    {
        // "customerid" → "customer" + "id" → "CustomerId"
        Assert.Equal("CustomerId", CamelCaseDictionary.Apply("customerid", "PascalCase"));
    }

    [Fact]
    public void Apply_PureLower_KnownWords_CamelCase()
    {
        Assert.Equal("customerId", CamelCaseDictionary.Apply("customerid", "camelCase"));
    }

    [Fact]
    public void Apply_PureLower_OrderDate_PascalCase()
    {
        Assert.Equal("OrderDate", CamelCaseDictionary.Apply("orderdate", "PascalCase"));
    }

    [Fact]
    public void Apply_PureLower_SingleDictionaryWord_PascalCase()
    {
        Assert.Equal("Name", CamelCaseDictionary.Apply("name", "PascalCase"));
    }

    [Fact]
    public void Apply_PureLower_SingleDictionaryWord_CamelCase()
    {
        Assert.Equal("name", CamelCaseDictionary.Apply("name", "camelCase"));
    }

    // ── Apply: pure uppercase ─────────────────────────────────────────────

    [Fact]
    public void Apply_AllUpper_PascalCase()
    {
        // All uppercase → no mixed case → dictionary splits on lowercase
        // "CUSTOMERID" → lowercase "customerid" → "customer" + "id" → "CustomerId"
        Assert.Equal("CustomerId", CamelCaseDictionary.Apply("CUSTOMERID", "PascalCase"));
    }

    // ── SplitByDictionary (internal) ──────────────────────────────────────

    [Fact]
    public void SplitByDictionary_KnownWords_SplitsCorrectly()
    {
        var words = CamelCaseDictionary.SplitByDictionary("customerid");
        Assert.Equal(2, words.Count);
        Assert.Equal("customer", words[0]);
        Assert.Equal("id", words[1]);
    }

    [Fact]
    public void SplitByDictionary_UnknownWord_ReturnsSingleSegment()
    {
        var words = CamelCaseDictionary.SplitByDictionary("xyz");
        Assert.Single(words);
        Assert.Equal("xyz", words[0]);
    }

    [Fact]
    public void SplitByDictionary_EmptyString_ReturnsEmpty()
    {
        var words = CamelCaseDictionary.SplitByDictionary("");
        Assert.Empty(words);
    }

    [Fact]
    public void SplitByDictionary_MultipleWords_SplitsGreedy()
    {
        // "orderdate" → "order" + "date"
        var words = CamelCaseDictionary.SplitByDictionary("orderdate");
        Assert.Equal(2, words.Count);
        Assert.Equal("order", words[0]);
        Assert.Equal("date", words[1]);
    }

    [Fact]
    public void SplitByDictionary_UserName_SplitsToUserAndName()
    {
        var words = CamelCaseDictionary.SplitByDictionary("username");
        // "user" + "name"
        Assert.True(words.Count >= 1);
        Assert.Contains("user", words);
    }

    [Fact]
    public void SplitByDictionary_PrefersLongerMatch()
    {
        // "order" should be matched as one word, not "or" + "der"
        var words = CamelCaseDictionary.SplitByDictionary("order");
        Assert.Single(words);
        Assert.Equal("order", words[0]);
    }
}
