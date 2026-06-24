using System.Collections.Generic;
using System.Linq;
using AkmlSql.Engine.Completion.Providers;
using Xunit;

namespace AkmlSql.Engine.Tests.Completion;

/// <summary>
/// Spec 030 parity — AliasProvider must split on hyphens the same way it splits on underscores,
/// so that "my-order-table" → "mot" (SQL Prompt behaviour). Driven through the public
/// BuildAliasItems (GenerateAliasCandidates is internal).
/// </summary>
public class Parity_AliasHyphenTests
{
    private static readonly HashSet<string> None = new(System.StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<(string Display, string Insert)> Items(string tableName)
        => new AliasProvider { IncludeAs = false }.BuildAliasItems(tableName, None);

    [Theory]
    [InlineData("my-order-table", "mot")]
    [InlineData("order-details", "od")]
    [InlineData("order-detail", "od")]
    public void HyphenSeparated_ProducesInitialsAlias(string tableName, string expected)
    {
        Assert.Contains(Items(tableName), i => string.Equals(i.Display, expected, System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PascalCase_Unaffected_RegressionGuard()
    {
        // Existing PascalCase behaviour must be preserved: "OrderDetails" → "od".
        Assert.Contains(Items("OrderDetails"), i => string.Equals(i.Display, "od", System.StringComparison.OrdinalIgnoreCase));
    }
}
